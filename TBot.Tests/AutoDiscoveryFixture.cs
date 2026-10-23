using System.Reflection;
using Microsoft.Extensions.Logging;
using Tbot.Common.Settings;
using Tbot.Includes;
using Tbot.Services;
using Tbot.Workers;
using TBot.Common.Logging;
using TBot.Ogame.Infrastructure;
using TBot.Ogame.Infrastructure.Enums;
using TBot.Ogame.Infrastructure.Models;

namespace TBot.Tests;

internal sealed class AutoDiscoveryFixture : IAsyncDisposable {
    public dynamic Settings;
    public UserData Data;
    public TestableAutoDiscoveryWorker Worker;
    public int Discoveries = 3;
    public bool SendSucceeds = true;
    public int FailedSendsRemaining;
    public List<Coordinate> Destinations = new();
    private readonly List<(LogLevel Level, string Message)> _logs = new();

    public static async Task<AutoDiscoveryFixture> Create() {
        var fixture = new AutoDiscoveryFixture {
            Settings = await SettingsService.GetSettings(Path.Combine(AppContext.BaseDirectory, "instance_settings.json"))
        };
        fixture.Settings.AutoDiscovery.Active = true;
        fixture.Settings.AutoDiscovery.RandomizeDestination = false;
        fixture.Settings.AutoDiscovery.MaxSlots = 8;
        fixture.Settings.AutoDiscovery.CheckIntervalMin = 1;
        fixture.Settings.AutoDiscovery.CheckIntervalMax = 2;
        fixture.Settings.SleepMode.Active = false;
        fixture.Settings.General.SlotsToLeaveFree = 0;
        fixture.Settings.General.SlotPriorityLevel.AutoDiscovery = 0;
        fixture.Settings.Brain.Active = false;
        fixture.Settings.Expeditions.Active = false;
        fixture.Settings.AutoFarm.Active = false;
        fixture.Settings.AutoColonize.Active = false;
        fixture.Settings.AutoHarvest.Active = false;
        dynamic originConfig = new ConfigObject();
        originConfig.Galaxy = 1;
        originConfig.System = 2;
        originConfig.Position = 5;
        originConfig.Type = "Planet";
        fixture.Settings.AutoDiscovery.Origin = new ConfigObject[] { originConfig };
        var planet = new Celestial {
            ID = 101, Name = "Test planet", Coordinate = new Coordinate(1, 2, 5),
            Resources = new Resources(), Ships = new Ships(), LFBonuses = new LFBonuses()
        };
        fixture.Data = new UserData {
            celestials = new List<Celestial> { planet },
            fleets = new List<Fleet>(),
            slots = new Slots { Total = 8, InUse = 0 },
            serverData = new ServerData { Galaxies = 1, Systems = 3, DonutGalaxy = true, DonutSystem = true }
        };
        var main = StrictProxy.Make<ITBotMain>((method, args) => method.Name switch {
            "get_InstanceSettings" => (object)fixture.Settings,
            "get_InstanceAlias" => "tests",
            "get_UserData" => fixture.Data,
            "log" => fixture.Log((LogLevel)args[0], (string)args[2]),
            _ => throw new NotSupportedException(method.Name)
        });
        var ogame = StrictProxy.Make<IOgameService>((method, args) => method.Name switch {
            "GetCelestials" => Task.FromResult(fixture.Data.celestials),
            "GetAvailableDiscoveries" => Task.FromResult(fixture.Discoveries),
            "SendDiscovery" => fixture.SendDiscovery((Celestial)args[0], (Coordinate)args[1]),
            _ => throw new NotSupportedException(method.Name)
        });
        var bridge = StrictProxy.Make<ITBotOgamedBridge>((method, args) => method.Name switch {
            "GetDateTime" => Task.FromResult(DateTime.Now),
            "UpdateSlots" => Task.FromResult(fixture.Data.slots),
            "UpdatePlanet" => Task.FromResult((Celestial)args[0]),
            "CheckCelestials" => Task.FromResult(true),
            _ => throw new NotSupportedException(method.Name)
        });
        var scheduler = StrictProxy.Make<IFleetScheduler>((method, args) => method.Name switch {
            "UpdateFleets" => Task.FromResult(fixture.Data.fleets),
            _ => throw new NotSupportedException(method.Name)
        });
        var logger = StrictProxy.Make<ILoggerService<CalculationService>>((method, args) => method.Name switch {
            "WriteLog" => null,
            _ => throw new NotSupportedException(method.Name)
        });
        fixture.Worker = new TestableAutoDiscoveryWorker(main, ogame, scheduler, new CalculationService(logger, ogame), bridge);
        return fixture;
    }

    public async Task RunCycle() {
        await Worker.StopWorker();
        // Observe real period changes without starting a timer or a background worker.
        var timerType = typeof(WorkerBase).Assembly.GetType("Tbot.Includes.AsyncTimer");
        var timer = Activator.CreateInstance(timerType, (AsyncTimerCallback)(_ => Task.CompletedTask), "discovery-tests");
        typeof(WorkerBase).GetField("_timer", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(Worker, timer);
        await Worker.RunCycle();
    }

    public void FillBlacklist() {
        Data.discoveryBlackList = new Dictionary<Coordinate, DateTime>();
        for (int system = 1; system <= Data.serverData.Systems; system++)
            for (int position = 1; position <= 15; position++)
                Data.discoveryBlackList.Add(new Coordinate(1, system, position), DateTime.Now.AddDays(1));
    }

    public async Task RunBoundedCycle() {
        var cycle = Task.Run(RunCycle);
        try {
            await cycle.WaitAsync(TimeSpan.FromSeconds(2));
        } finally {
            // Let the unfixed random loop exit after a timeout; leave no spinning task.
            Data.slots.InUse = Data.slots.Total;
            await cycle.WaitAsync(TimeSpan.FromSeconds(2));
        }
    }

    public void AssertRetryScheduled() {
        Assert.Empty(_logs.Where(entry => entry.Level >= LogLevel.Error));
        Assert.True(Worker.Period > TimeSpan.Zero, "The worker must schedule another check.");
    }

    public void AssertFallbackRetryScheduled() {
        AssertRetryScheduled();
        Assert.InRange(Worker.Period.TotalMilliseconds, 60_000, 120_000);
    }

    private object Log(LogLevel level, string message) {
        _logs.Add((level, message));
        return null;
    }

    private Task<bool> SendDiscovery(Celestial origin, Coordinate destination) {
        Destinations.Add(destination);
        bool success = SendSucceeds;
        if (FailedSendsRemaining > 0) {
            FailedSendsRemaining--;
            success = false;
        }
        if (success) {
            Data.slots.InUse++;
            Data.fleets.Add(new Fleet { Mission = Missions.Discovery, BackIn = 30, Origin = origin.Coordinate, Destination = destination });
        }
        return Task.FromResult(success);
    }

    public ValueTask DisposeAsync() => new(Worker.StopWorker());
}

internal sealed class TestableAutoDiscoveryWorker : AutoDiscoveryWorker {
    public TestableAutoDiscoveryWorker(ITBotMain main, IOgameService ogame, IFleetScheduler scheduler,
        ICalculationService calculation, ITBotOgamedBridge bridge) : base(main, ogame, scheduler, calculation, bridge) { }

    public Task RunCycle() => Execute();
}

public class StrictProxy : DispatchProxy {
    private Func<MethodInfo, object[], object> _handler;
    protected override object Invoke(MethodInfo method, object[] args) => _handler(method, args);

    public static T Make<T>(Func<MethodInfo, object[], object> handler) where T : class {
        var proxy = Create<T, StrictProxy>();
        ((StrictProxy)(object)proxy)._handler = handler;
        return proxy;
    }
}
