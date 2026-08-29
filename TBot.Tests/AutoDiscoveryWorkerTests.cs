using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Tbot.Common.Settings;
using Tbot.Includes;
using Tbot.Services;
using Tbot.Workers;
using TBot.Common.Logging;
using TBot.Model;
using TBot.Ogame.Infrastructure;
using TBot.Ogame.Infrastructure.Enums;
using TBot.Ogame.Infrastructure.Models;
using Xunit;

namespace TBot.Tests {
	public class AutoDiscoveryWorkerTests {
		[Fact]
		public async Task RejectedPositionIsNotQuarantinedAndLaterPositionsAreStillTried() {
			var alias = $"autodiscovery-{Guid.NewGuid():N}";
			var origin = new Planet {
				ID = 100,
				Name = "Discovery origin",
				Coordinate = new Coordinate(1, 30, 8, Celestials.Planet),
				Ships = new Ships(pathfinder: 100),
				Resources = new Resources(0, 0, 100000)
			};
			var userData = new UserData {
				celestials = new List<Celestial> { origin },
				fleets = new List<Fleet>(),
				slots = new Slots { Total = 20, InUse = 0 },
				serverData = new ServerData { Galaxies = 6, Systems = 499 },
				researches = new Researches(),
				userInfo = new UserInfo()
			};
			var settings = SettingsService.GetMergedSettingsFromContents(
				$$"""
{
  "General": {
    "SlotsToLeaveFree": 0,
    "SlotPriorityLevel": { "Brain": 1, "Expeditions": 1, "AutoFarm": 1, "Colonize": 1, "AutoDiscovery": 1, "AutoHarvest": 1 }
  },
  "SleepMode": { "Active": false, "GoToSleep": "23:00", "WakeUp": "07:00" },
  "Brain": {
    "Active": false,
    "Transports": { "Active": false, "MaxSlots": 0 },
    "AutoMine": { "Active": false }, "AutoResearch": { "Active": false },
    "LifeformAutoMine": { "Active": false }, "LifeformAutoResearch": { "Active": false }
  },
  "Expeditions": { "Active": false },
  "AutoFarm": { "Active": false, "MaxSlots": 0 },
  "AutoColonize": { "Active": false, "IntensiveResearch": { "Active": false, "MaxSlots": 0 } },
  "AutoHarvest": { "Active": false, "MaxSlots": 0 },
  "AutoDiscovery": {
    "Active": true,
    "MaxSlots": 3,
    "CheckIntervalMin": 1,
    "CheckIntervalMax": 1,
    "Origin": [{"Galaxy":1,"System":30,"Position":8,"Type":"Planet"}]
  }
}
""",
				new List<string> { "{}" });
			var parent = Proxy<ITBotMain>.Create((method, _) => method.Name switch {
				"get_InstanceAlias" => alias,
				"get_InstanceSettings" => settings,
				"get_UserData" => userData,
				_ => Proxy<ITBotMain>.Default(method.ReturnType)
			});

			var sentPositions = new List<int>();
			var responses = new Queue<DiscoveryDispatchStatus>(new[] {
				DiscoveryDispatchStatus.Rejected,
				DiscoveryDispatchStatus.Sent,
				DiscoveryDispatchStatus.Sent
			});
			var ogame = Proxy<IOgameService>.Create((method, args) => method.Name switch {
				nameof(IOgameService.GetCelestials) => Task.FromResult(new List<Celestial> { origin }),
				nameof(IOgameService.GetAvailableDiscoveries) => Task.FromResult(3),
				nameof(IOgameService.GetPositionsAvailableForDiscoveryFleet) => Task.FromResult(
					new List<Coordinate> {
						new Coordinate(1, 30, 1), new Coordinate(1, 30, 2), new Coordinate(1, 30, 3)
					}),
				_ => Proxy<IOgameService>.Default(method.ReturnType)
			});
			var bridge = Proxy<ITBotOgamedBridge>.Create((method, _) => method.Name switch {
				nameof(ITBotOgamedBridge.GetDateTime) => Task.FromResult(DateTime.UtcNow),
				nameof(ITBotOgamedBridge.UpdateSlots) => Task.FromResult(new Slots { Total = 20, InUse = 0 }),
				nameof(ITBotOgamedBridge.CheckCelestials) => Task.FromResult(true),
				_ => Proxy<ITBotOgamedBridge>.Default(method.ReturnType)
			});
			var scheduler = Proxy<IFleetScheduler>.Create((method, args) => method.Name switch {
				nameof(IFleetScheduler.UpdateFleets) => Task.FromResult(new List<Fleet>()),
				nameof(IFleetScheduler.SendDiscovery) => SendDiscovery(args, sentPositions, responses),
				_ => Proxy<IFleetScheduler>.Default(method.ReturnType)
			});
			var calculation = Proxy<ICalculationService>.Create((method, _) => method.Name switch {
				nameof(ICalculationService.CalcSlotsPriority) => 3,
				_ => Proxy<ICalculationService>.Default(method.ReturnType)
			});

			try {
				var worker = new AutoDiscoveryWorker(parent, ogame, scheduler, calculation, bridge);
				await Execute(worker);

				Assert.Equal(new[] { 1, 2, 3 }, sentPositions);
				Assert.DoesNotContain(userData.discoveryBlackList.Keys, coordinate => coordinate.Position == 1);
				Assert.Contains(userData.discoveryBlackList.Keys, coordinate => coordinate.Position == 2);
				Assert.Contains(userData.discoveryBlackList.Keys, coordinate => coordinate.Position == 3);
			} finally {
				var cursorPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data", $"autodiscovery_cursors_{alias}.json");
				if (System.IO.File.Exists(cursorPath))
					System.IO.File.Delete(cursorPath);
			}
		}

		[Fact]
		public async Task PathfinderShortageSkipsOnlyTheCurrentOrigin() {
			var alias = $"autodiscovery-{Guid.NewGuid():N}";
			var firstOrigin = new Planet {
				ID = 101,
				Name = "First discovery origin",
				Coordinate = new Coordinate(1, 30, 8, Celestials.Planet)
			};
			var secondOrigin = new Planet {
				ID = 102,
				Name = "Second discovery origin",
				Coordinate = new Coordinate(1, 31, 8, Celestials.Planet)
			};
			var userData = new UserData {
				celestials = new List<Celestial> { firstOrigin, secondOrigin },
				fleets = new List<Fleet>(),
				slots = new Slots { Total = 20, InUse = 0 },
				serverData = new ServerData { Galaxies = 6, Systems = 499 },
				researches = new Researches(),
				userInfo = new UserInfo()
			};
			var settings = SettingsService.GetMergedSettingsFromContents(
				$$"""
{
  "General": {
    "SlotsToLeaveFree": 0,
    "SlotPriorityLevel": { "Brain": 1, "Expeditions": 1, "AutoFarm": 1, "Colonize": 1, "AutoDiscovery": 1, "AutoHarvest": 1 }
  },
  "SleepMode": { "Active": false, "GoToSleep": "23:00", "WakeUp": "07:00" },
  "Brain": {
    "Active": false,
    "Transports": { "Active": false, "MaxSlots": 0 },
    "AutoMine": { "Active": false }, "AutoResearch": { "Active": false },
    "LifeformAutoMine": { "Active": false }, "LifeformAutoResearch": { "Active": false }
  },
  "Expeditions": { "Active": false },
  "AutoFarm": { "Active": false, "MaxSlots": 0 },
  "AutoColonize": { "Active": false, "IntensiveResearch": { "Active": false, "MaxSlots": 0 } },
  "AutoHarvest": { "Active": false, "MaxSlots": 0 },
  "AutoDiscovery": {
					"Active": true,
					"MaxSlots": 2,
    "CheckIntervalMin": 1,
    "CheckIntervalMax": 1,
    "Origin": [
      {"Galaxy":1,"System":30,"Position":8,"Type":"Planet"},
      {"Galaxy":1,"System":31,"Position":8,"Type":"Planet"}
    ]
  }
}
""",
				new List<string> { "{}" });
			var parent = Proxy<ITBotMain>.Create((method, _) => method.Name switch {
				"get_InstanceAlias" => alias,
				"get_InstanceSettings" => settings,
				"get_UserData" => userData,
				_ => Proxy<ITBotMain>.Default(method.ReturnType)
			});

			var attemptedOrigins = new List<int>();
			var responses = new Queue<DiscoveryDispatchStatus>(new[] {
				DiscoveryDispatchStatus.NoShips,
				DiscoveryDispatchStatus.Sent,
				DiscoveryDispatchStatus.Sent
			});
			var ogame = Proxy<IOgameService>.Create((method, args) => method.Name switch {
				 nameof(IOgameService.GetCelestials) => Task.FromResult(new List<Celestial> { firstOrigin, secondOrigin }),
				 nameof(IOgameService.GetAvailableDiscoveries) => Task.FromResult(2),
				 nameof(IOgameService.GetPositionsAvailableForDiscoveryFleet) => Task.FromResult(
					 new List<Coordinate> {
						 new Coordinate(1, ((Celestial)args[0]).Coordinate.System, 1),
						 new Coordinate(1, ((Celestial)args[0]).Coordinate.System, 2)
					 }),
				_ => Proxy<IOgameService>.Default(method.ReturnType)
			});
			var bridge = Proxy<ITBotOgamedBridge>.Create((method, _) => method.Name switch {
				nameof(ITBotOgamedBridge.GetDateTime) => Task.FromResult(DateTime.UtcNow),
				nameof(ITBotOgamedBridge.UpdateSlots) => Task.FromResult(new Slots { Total = 20, InUse = 0 }),
				nameof(ITBotOgamedBridge.CheckCelestials) => Task.FromResult(true),
				_ => Proxy<ITBotOgamedBridge>.Default(method.ReturnType)
			});
			var scheduler = Proxy<IFleetScheduler>.Create((method, args) => method.Name switch {
				nameof(IFleetScheduler.UpdateFleets) => Task.FromResult(new List<Fleet>()),
				nameof(IFleetScheduler.SendDiscovery) => SendDiscoveryWithOrigins(args, attemptedOrigins, responses),
				_ => Proxy<IFleetScheduler>.Default(method.ReturnType)
			});
			var calculation = Proxy<ICalculationService>.Create((method, _) => method.Name switch {
				 nameof(ICalculationService.CalcSlotsPriority) => 2,
				_ => Proxy<ICalculationService>.Default(method.ReturnType)
			});

			try {
				var worker = new AutoDiscoveryWorker(parent, ogame, scheduler, calculation, bridge);
				await Execute(worker);

				Assert.Equal(new[] { 101, 102, 102 }, attemptedOrigins);
			} finally {
				var cursorPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data", $"autodiscovery_cursors_{alias}.json");
				if (System.IO.File.Exists(cursorPath))
					System.IO.File.Delete(cursorPath);
			}
		}

		[Fact]
		public async Task MultipleOriginsAreUsedInRoundRobinOrderBeforeReusingOne() {
			var alias = $"autodiscovery-{Guid.NewGuid():N}";
			var firstOrigin = new Planet {
				ID = 201,
				Name = "First discovery origin",
				Coordinate = new Coordinate(1, 30, 8, Celestials.Planet),
				Ships = new Ships(pathfinder: 100)
			};
			var secondOrigin = new Planet {
				ID = 202,
				Name = "Second discovery origin",
				Coordinate = new Coordinate(1, 31, 8, Celestials.Planet),
				Ships = new Ships(pathfinder: 100)
			};
			var userData = new UserData {
				celestials = new List<Celestial> { firstOrigin, secondOrigin },
				fleets = new List<Fleet>(),
				slots = new Slots { Total = 20, InUse = 0 },
				serverData = new ServerData { Galaxies = 6, Systems = 499 },
				researches = new Researches(),
				userInfo = new UserInfo()
			};
			var settings = SettingsService.GetMergedSettingsFromContents(
				$$"""
{
  "General": {
    "SlotsToLeaveFree": 0,
    "SlotPriorityLevel": { "Brain": 1, "Expeditions": 1, "AutoFarm": 1, "Colonize": 1, "AutoDiscovery": 1, "AutoHarvest": 1 }
  },
  "SleepMode": { "Active": false, "GoToSleep": "23:00", "WakeUp": "07:00" },
  "Brain": {
    "Active": false,
    "Transports": { "Active": false, "MaxSlots": 0 },
    "AutoMine": { "Active": false }, "AutoResearch": { "Active": false },
    "LifeformAutoMine": { "Active": false }, "LifeformAutoResearch": { "Active": false }
  },
  "Expeditions": { "Active": false },
  "AutoFarm": { "Active": false, "MaxSlots": 0 },
  "AutoColonize": { "Active": false, "IntensiveResearch": { "Active": false, "MaxSlots": 0 } },
  "AutoHarvest": { "Active": false, "MaxSlots": 0 },
  "AutoDiscovery": {
    "Active": true,
    "MaxSlots": 3,
    "CheckIntervalMin": 1,
    "CheckIntervalMax": 1,
    "Origin": [
      {"Galaxy":1,"System":30,"Position":8,"Type":"Planet"},
      {"Galaxy":1,"System":31,"Position":8,"Type":"Planet"}
    ]
  }
}
""",
				new List<string> { "{}" });
			var parent = Proxy<ITBotMain>.Create((method, _) => method.Name switch {
				"get_InstanceAlias" => alias,
				"get_InstanceSettings" => settings,
				"get_UserData" => userData,
				_ => Proxy<ITBotMain>.Default(method.ReturnType)
			});

			var attemptedOrigins = new List<int>();
			var responses = new Queue<DiscoveryDispatchStatus>(new[] {
				DiscoveryDispatchStatus.Sent,
				DiscoveryDispatchStatus.Sent,
				DiscoveryDispatchStatus.Sent
			});
			var ogame = Proxy<IOgameService>.Create((method, args) => method.Name switch {
				nameof(IOgameService.GetCelestials) => Task.FromResult(new List<Celestial> { firstOrigin, secondOrigin }),
				nameof(IOgameService.GetAvailableDiscoveries) => Task.FromResult(3),
				nameof(IOgameService.GetPositionsAvailableForDiscoveryFleet) => Task.FromResult(
					new List<Coordinate> {
						new Coordinate(1, ((Celestial)args[0]).Coordinate.System, 1),
						new Coordinate(1, ((Celestial)args[0]).Coordinate.System, 2)
					}),
				_ => Proxy<IOgameService>.Default(method.ReturnType)
			});
			var bridge = Proxy<ITBotOgamedBridge>.Create((method, _) => method.Name switch {
				nameof(ITBotOgamedBridge.GetDateTime) => Task.FromResult(DateTime.UtcNow),
				nameof(ITBotOgamedBridge.UpdateSlots) => Task.FromResult(new Slots { Total = 20, InUse = 0 }),
				nameof(ITBotOgamedBridge.CheckCelestials) => Task.FromResult(true),
				_ => Proxy<ITBotOgamedBridge>.Default(method.ReturnType)
			});
			var scheduler = Proxy<IFleetScheduler>.Create((method, args) => method.Name switch {
				nameof(IFleetScheduler.UpdateFleets) => Task.FromResult(new List<Fleet>()),
				nameof(IFleetScheduler.SendDiscovery) => SendDiscoveryWithOrigins(args, attemptedOrigins, responses),
				_ => Proxy<IFleetScheduler>.Default(method.ReturnType)
			});
			var calculation = Proxy<ICalculationService>.Create((method, _) => method.Name switch {
				nameof(ICalculationService.CalcSlotsPriority) => 3,
				_ => Proxy<ICalculationService>.Default(method.ReturnType)
			});

			try {
				var worker = new AutoDiscoveryWorker(parent, ogame, scheduler, calculation, bridge);
				await Execute(worker);

				Assert.Equal(new[] { 201, 202, 201 }, attemptedOrigins);
			} finally {
				var cursorPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data", $"autodiscovery_cursors_{alias}.json");
				if (System.IO.File.Exists(cursorPath))
					System.IO.File.Delete(cursorPath);
			}
		}

		private static Task<DiscoveryDispatchResult> SendDiscovery(object[] args, List<int> sentPositions, Queue<DiscoveryDispatchStatus> responses) {
			sentPositions.Add(((Coordinate)args[1]).Position);
			var status = responses.Dequeue();
			return Task.FromResult(new DiscoveryDispatchResult(status, status.ToString()));
		}

		private static Task<DiscoveryDispatchResult> SendDiscoveryWithOrigins(object[] args, List<int> attemptedOrigins, Queue<DiscoveryDispatchStatus> responses) {
			attemptedOrigins.Add(((Celestial)args[0]).ID);
			var status = responses.Dequeue();
			return Task.FromResult(new DiscoveryDispatchResult(status, status.ToString()));
		}

		private static async Task Execute(AutoDiscoveryWorker worker) {
			var execute = typeof(AutoDiscoveryWorker).GetMethod(
				"Execute", BindingFlags.Instance | BindingFlags.NonPublic);
			Assert.NotNull(execute);
			await (Task)execute.Invoke(worker, null);
		}

		private class Proxy<T> : DispatchProxy where T : class {
			private Func<MethodInfo, object[], object> _handler;

			public static T Create(Func<MethodInfo, object[], object> handler) {
				var proxy = DispatchProxy.Create<T, Proxy<T>>();
				((Proxy<T>)(object)proxy)._handler = handler;
				return proxy;
			}

			public static object Default(Type returnType) {
				if (returnType == typeof(void)) return null;
				if (returnType == typeof(Task)) return Task.CompletedTask;
				if (returnType.IsGenericType && returnType.GetGenericTypeDefinition() == typeof(Task<>)) {
					var valueType = returnType.GetGenericArguments()[0];
					var value = valueType.IsValueType ? Activator.CreateInstance(valueType) : null;
					return typeof(Task).GetMethod(nameof(Task.FromResult))
						.MakeGenericMethod(valueType).Invoke(null, new[] { value });
				}
				if (returnType.IsValueType) return Activator.CreateInstance(returnType);
				return null;
			}

			protected override object Invoke(MethodInfo targetMethod, object[] args) {
				return _handler?.Invoke(targetMethod, args ?? Array.Empty<object>()) ?? Default(targetMethod.ReturnType);
			}
		}
	}
}
