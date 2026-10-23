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

public class ExpeditionsWorkerTests {
    [Fact]
    public async Task Existing_expedition_usage_is_subtracted_once() {
        await using var fixture = await ExpeditionFixture.Create();
        Assert.Equal(4, fixture.Calculation.CalcSlotsPriority(Feature.Expeditions,
            [new(Feature.Expeditions, 1, true, 6, 2), new(Feature.BrainAutoMine, 2, true, 5, 2)],
            new Slots { Total = 16, InUse = 4, ExpTotal = 6, ExpInUse = 2 },
            [Expedition(60), Expedition(300), new() { Mission = Missions.Transport }, new() { Mission = Missions.Transport }], 1));
    }

    [Fact]
    public async Task Higher_priority_reservation_limits_new_expeditions() {
        await using var fixture = await ExpeditionFixture.Create();
        Assert.Equal(3, fixture.Calculation.CalcSlotsPriority(Feature.Expeditions,
            [new(Feature.Expeditions, 2, true, 6, 0), new(Feature.BrainAutoMine, 1, true, 12, 3)],
            new Slots { Total = 16, InUse = 3, ExpTotal = 6, ExpInUse = 0 },
            Enumerable.Range(0, 3).Select(_ => new Fleet { Mission = Missions.Transport }).ToList(), 1));
    }

    [Fact]
    public async Task Lower_priority_active_fleets_preserve_a_free_slot() {
        await using var fixture = await ExpeditionFixture.Create();
        Assert.Equal(2, fixture.Calculation.CalcSlotsPriority(Feature.Expeditions,
            [new(Feature.Expeditions, 1, true, 6, 2), new(Feature.AutoFarm, 3, true, 5, 5)],
            new Slots { Total = 10, InUse = 7, ExpTotal = 6, ExpInUse = 2 },
            Enumerable.Range(0, 7).Select(i => new Fleet { Mission = i < 2 ? Missions.Expedition : Missions.Attack }).ToList(), 1));
    }

    [Fact]
    public async Task Unranked_expeditions_use_free_slots_minus_reserve() {
        await using var fixture = await ExpeditionFixture.Create();
        Assert.Equal(2, fixture.Calculation.CalcSlotsPriority(Feature.Expeditions,
            [new(Feature.Expeditions, 0, true, 6, 2), new(Feature.AutoFarm, 3, true, 5, 5)],
            new Slots { Total = 10, InUse = 7, ExpTotal = 6, ExpInUse = 2 },
            Enumerable.Range(0, 7).Select(i => new Fleet { Mission = i < 2 ? Missions.Expedition : Missions.Attack }).ToList(), 1));
    }

    public static IEnumerable<object[]> IdleCases() {
        yield return new object[] { "zero allocation", new Slots { Total = 16, ExpTotal = 2 },
            Array.Empty<Fleet>(), 16, false, false, 240_000L, 359_999L };
        yield return new object[] { "full expedition slots", new Slots { Total = 16, InUse = 2, ExpTotal = 2, ExpInUse = 2 },
            new[] { Expedition(60), Expedition(300) }, 1, false, false, 60_000L, 60_000L };
        yield return new object[] { "wait for all", new Slots { Total = 16, InUse = 2, ExpTotal = 6, ExpInUse = 2 },
            new[] { Expedition(60), Expedition(300) }, 1, true, false, 300_000L, 300_000L };
        yield return new object[] { "wait for majority", new Slots { Total = 16, InUse = 4, ExpTotal = 6, ExpInUse = 4 },
            new[] { Expedition(60), Expedition(120), Expedition(180), Expedition(300) }, 1, false, true, 60_000L, 60_000L };
        yield return new object[] { "full general slots", new Slots { Total = 2, InUse = 2, ExpTotal = 2 },
            new[] { new Fleet { Mission = Missions.Transport }, new Fleet { Mission = Missions.Transport } },
            1, false, false, 240_000L, 359_999L };
    }

    [Theory]
    [MemberData(nameof(IdleCases))]
    public async Task Idle_cycles_schedule_the_correct_retry(string scenario, Slots slots, Fleet[] fleets,
        int leaveFree, bool waitAll, bool waitMajority, long minMs, long maxMs) {
        await using var fixture = await ExpeditionFixture.Create();
        fixture.Data.slots = slots;
        fixture.Data.fleets = fleets.ToList();
        fixture.Settings.General.SlotsToLeaveFree = leaveFree;
        fixture.Settings.Expeditions.WaitForAllExpeditions = waitAll;
        fixture.Settings.Expeditions.WaitForMajorityOfExpeditions = waitMajority;

        await fixture.RunCycle();

        fixture.AssertNoDependencyErrors();
        Assert.Empty(fixture.Sends);
        Assert.True(fixture.Worker.Period.TotalMilliseconds >= minMs && fixture.Worker.Period.TotalMilliseconds <= maxMs,
            $"{scenario}: expected retry in [{minMs}, {maxMs}] ms, got {fixture.Worker.Period.TotalMilliseconds} ms");
    }

    [Fact]
    public async Task Successful_dispatches_respect_higher_priority_reservations() {
        await using var fixture = await ExpeditionFixture.Create();
        fixture.Data.slots = new Slots { Total = 10, ExpTotal = 6 };
        fixture.Settings.General.SlotPriorityLevel.Brain = 1;
        fixture.Settings.General.SlotPriorityLevel.Expeditions = 2;
        fixture.Settings.Brain.Active = true;
        fixture.Settings.Brain.Transports.Active = true;
        fixture.Settings.Brain.AutoMine.Active = true;
        fixture.Settings.Brain.Transports.MaxSlots = 6;

        await fixture.RunCycle();

        fixture.AssertNoDependencyErrors();
        // Ten total minus six reserved for Brain minus one kept free = three dispatches.
        Assert.Equal(3, fixture.Sends.Count);
        Assert.Equal(3, fixture.Data.fleets.Count);
        Assert.Equal(7, fixture.Data.slots.Free);
        Assert.All(fixture.Sends, send => {
            Assert.Equal((1, 2, 5), (send.Origin.Galaxy, send.Origin.System, send.Origin.Position));
            Assert.Equal((1, 2, 16), (send.Destination.Galaxy, send.Destination.System, send.Destination.Position));
            Assert.Equal(Missions.Expedition, send.Mission);
        });
        Assert.True(fixture.Worker.Period > TimeSpan.Zero);
    }

    private static Fleet Expedition(int backIn) => new() { Mission = Missions.Expedition, BackIn = backIn };
}

internal sealed class ExpeditionFixture : IAsyncDisposable {
    public dynamic Settings;
    public UserData Data;
    public CalculationService Calculation;
    public TestableExpeditionsWorker Worker;
    public List<(Coordinate Origin, Coordinate Destination, Missions Mission)> Sends = new();
    private readonly List<string> _dependencyErrors = new();
    private readonly List<string> _exceptionLogs = new();

    public static async Task<ExpeditionFixture> Create() {
        var fixture = new ExpeditionFixture {
            Settings = await SettingsService.GetSettings(Path.Combine(AppContext.BaseDirectory, "instance_settings.json"))
        };
        fixture.Settings.General.SlotsToLeaveFree = 1;
        fixture.Settings.General.SlotPriorityLevel.Expeditions = 1;
        fixture.Settings.Brain.Active = false;
        fixture.Settings.AutoFarm.Active = false;
        fixture.Settings.AutoColonize.Active = false;
        fixture.Settings.AutoDiscovery.Active = false;
        fixture.Settings.AutoHarvest.Active = false;
        fixture.Settings.Expeditions.Active = true;
        fixture.Settings.Expeditions.WaitForAllExpeditions = false;
        fixture.Settings.Expeditions.WaitForMajorityOfExpeditions = false;
        fixture.Settings.Expeditions.MinWaitNextRound = 0;
        fixture.Settings.Expeditions.MaxWaitNextRound = 0;
        fixture.Settings.Expeditions.MinWaitNextFleet = 0;
        fixture.Settings.Expeditions.MaxWaitNextFleet = 0;
        fixture.Settings.Expeditions.RandomizeOrder = false;
        fixture.Settings.Expeditions.FuelToCarry = 0;
        fixture.Settings.Expeditions.MaxExpeditionsPerOrigin = 10;
        fixture.Settings.Expeditions.SplitExpeditionsBetweenSystems.Active = false;
        fixture.Settings.Expeditions.ManualShips.Active = true;
        dynamic manualShips = new ConfigObject();
        foreach (string ship in new[] { "LightFighter", "HeavyFighter", "Cruiser", "Battleship", "Battlecruiser", "Bomber",
            "Destroyer", "Deathstar", "SmallCargo", "LargeCargo", "ColonyShip", "Recycler", "EspionageProbe", "Reaper", "Pathfinder" })
            ((IDictionary<string, object>)manualShips)[ship] = ship == "LargeCargo" ? 1L : 0L;
        fixture.Settings.Expeditions.ManualShips.Ships = manualShips;
        dynamic origin = new ConfigObject();
        origin.Galaxy = 1;
        origin.System = 2;
        origin.Position = 5;
        origin.Type = "Planet";
        fixture.Settings.Expeditions.Origin = new ConfigObject[] { origin };
        fixture.Data = new UserData {
            researches = new Researches { Astrophysics = 16 }, serverData = new ServerData { Galaxies = 1, Systems = 3 },
            userInfo = new UserInfo(), fleets = new List<Fleet>(), slots = new Slots { Total = 16, ExpTotal = 6 },
            celestials = new List<Celestial> { new() {
                ID = 101, Name = "Expedition origin", Coordinate = new Coordinate(1, 2, 5),
                Ships = new Ships { LargeCargo = 100 }, Resources = new Resources(), LFBonuses = new LFBonuses()
            } }
        };
        var main = StrictInterfaceProxy.Make<ITBotMain>((method, args) => method.Name switch {
            "get_InstanceSettings" => (object)fixture.Settings,
            "get_InstanceAlias" => "tests",
            "get_UserData" => fixture.Data,
            "log" => fixture.Log((LogLevel)args[0], (string)args[2]),
            _ => fixture.Unexpected(method.Name)
        });
        var ogame = StrictInterfaceProxy.Make<IOgameService>((method, args) => method.Name switch {
            "GetServerData" => Task.FromResult(fixture.Data.serverData),
            _ => fixture.Unexpected(method.Name)
        });
        var bridge = StrictInterfaceProxy.Make<ITBotOgamedBridge>((method, args) => method.Name switch {
            "UpdateResearches" => Task.FromResult(fixture.Data.researches),
            "UpdateSlots" => Task.FromResult(fixture.Data.slots),
            "GetDateTime" => Task.FromResult(new DateTime(2026, 10, 23, 12, 0, 0)),
            "CheckCelestials" => Task.FromResult(true),
            "UpdatePlanet" => Task.FromResult((Celestial)args[0]),
            _ => fixture.Unexpected(method.Name)
        });
        var scheduler = StrictInterfaceProxy.Make<IFleetScheduler>((method, args) => method.Name switch {
            "UpdateFleets" => Task.FromResult(fixture.Data.fleets),
            "SendFleet" => fixture.SendFleet((Celestial)args[0], (Ships)args[1], (Coordinate)args[2], (Missions)args[3]),
            _ => fixture.Unexpected(method.Name)
        });
        var logger = StrictInterfaceProxy.Make<ILoggerService<CalculationService>>((method, args) => method.Name switch {
            "WriteLog" => null,
            _ => fixture.Unexpected(method.Name)
        });
        fixture.Calculation = new CalculationService(logger, ogame);
        fixture.Worker = new TestableExpeditionsWorker(main, ogame, scheduler, fixture.Calculation, bridge);
        return fixture;
    }

    public async Task RunCycle() {
        await Worker.StopWorker();
        var timerType = typeof(WorkerBase).Assembly.GetType("Tbot.Includes.AsyncTimer");
        var timer = Activator.CreateInstance(timerType, (AsyncTimerCallback)(_ => Task.CompletedTask), "expedition-tests");
        typeof(WorkerBase).GetField("_timer", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(Worker, timer);
        await Worker.RunCycle();
    }

    public void AssertNoDependencyErrors() {
        Assert.Empty(_dependencyErrors);
        Assert.Empty(_exceptionLogs);
    }

    private object Unexpected(string method) {
        _dependencyErrors.Add(method);
        throw new NotSupportedException(method);
    }

    private object Log(LogLevel level, string message) {
        if (level >= LogLevel.Error || message.Contains("exception", StringComparison.OrdinalIgnoreCase)
            || message.Contains("Stacktrace", StringComparison.OrdinalIgnoreCase))
            _exceptionLogs.Add(message);
        return null;
    }

    private Task<int> SendFleet(Celestial origin, Ships ships, Coordinate destination, Missions mission) {
        Sends.Add((origin.Coordinate, destination, mission));
        Data.slots.InUse++;
        Data.slots.ExpInUse++;
        origin.Ships.LargeCargo -= ships.LargeCargo;
        Data.fleets.Add(new Fleet { Mission = mission, BackIn = 60, Origin = origin.Coordinate, Destination = destination });
        return Task.FromResult(Sends.Count);
    }

    public ValueTask DisposeAsync() => new(Worker.StopWorker());
}

internal sealed class TestableExpeditionsWorker : ExpeditionsWorker {
    public TestableExpeditionsWorker(ITBotMain main, IOgameService ogame, IFleetScheduler scheduler,
        ICalculationService calculation, ITBotOgamedBridge bridge) : base(main, ogame, scheduler, calculation, bridge) { }

    public Task RunCycle() => Execute();
}
