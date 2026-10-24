using System.Reflection;
using Tbot.Common.Settings;
using Tbot.Includes;
using Tbot.Services;
using Tbot.Workers;
using TBot.Ogame.Infrastructure;
using TBot.Ogame.Infrastructure.Enums;
using TBot.Ogame.Infrastructure.Models;

namespace TBot.Tests;

public class DefenderColonizePreservationTests {
    [Theory]
    [InlineData("defender")]
    [InlineData("colonize")]
    public async Task Stopping_a_worker_cancels_and_awaits_its_background_actions(string feature) {
        var settings = SettingsService.GetMergedSettingsFromContents("{\"AutoColonize\":{\"Active\":true,\"Abandon\":{\"Active\":true}}}", new List<string> { "{}" });
        var planet = BrainDataFixture.Planet(91004, 5);
        var data = new UserData { celestials = new() { planet }, fleets = new() };
        var parent = Parent(settings, data);
        var scheduler = AuditProxy<IFleetScheduler>.Make((m, _) => AuditProxy<IFleetScheduler>.Default(m.ReturnType));
        var ogame = AuditProxy<IOgameService>.Make((m, _) => AuditProxy<IOgameService>.Default(m.ReturnType));
        var calc = AuditProxy<ICalculationService>.Make((m, _) => AuditProxy<ICalculationService>.Default(m.ReturnType));
        var worker = feature == "defender" ? (WorkerBase)Defender(parent, scheduler, Bridge()) : new ColonizeWorker(parent, ogame, scheduler, calc, Bridge());
        if (feature == "defender") {
            worker.GetType().GetMethod("ScheduleFleetSave", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(worker, new object[] { planet, new AttackerFleet { ArriveIn = 3600 } });
            var dictionary = (System.Collections.IDictionary)worker.GetType().GetField("_scheduledFleetSaves", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(worker);
            var save = dictionary[planet.ID];
            var token = ((CancellationTokenSource)save.GetType().GetField("Cts").GetValue(save)).Token;
            try {
                await worker.StopWorker();
                Assert.True(token.IsCancellationRequested);
                Assert.Empty(dictionary.Keys.Cast<object>());
            } finally { try { ((CancellationTokenSource)save.GetType().GetField("Cts").GetValue(save)).Cancel(); } catch (ObjectDisposedException) { } }
        } else {
            worker.GetType().GetMethod("EnsureAbandonWatcherRunning", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(worker, null);
            var task = (Task)worker.GetType().GetField("_abandonWatcherTask", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(worker);
            try {
                await worker.StopWorker();
                Assert.True(task.IsCompleted);
            } finally { worker.GetType().GetMethod("StopAbandonWatcher", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(worker, null); }
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Unknown_attack_composition_still_saves_even_below_ignore_thresholds(bool missingShips) {
        var settings = SettingsService.GetMergedSettingsFromContents("{\"Defender\":{\"Active\":true,\"IgnoreAttackIfIHave\":{\"Active\":true,\"MinResourcesToSave\":999999,\"MinFleetToSave\":999999},\"Autofleet\":{\"Active\":true}}}", new List<string> { "{}" });
        var planet = BrainDataFixture.Planet(91001, 5);
        var data = new UserData { celestials = new() { planet }, fleets = new(), researches = new Researches { EspionageTechnology = 1 } };
        var saved = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var parent = Parent(settings, data);
        var scheduler = AuditProxy<IFleetScheduler>.Make((m, _) => m.Name switch {
            nameof(IFleetScheduler.UpdateFleets) => Task.FromResult(data.fleets),
            nameof(IFleetScheduler.AutoFleetSave) => Signal(),
            _ => AuditProxy<IFleetScheduler>.Default(m.ReturnType)
        });
        using var cts = new CancellationTokenSource();
        var worker = Defender(parent, scheduler, Bridge());
        typeof(WorkerBase).GetField("_ct", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(worker, cts.Token);
        try {
            await Invoke(worker, "HandleAttack", new AttackerFleet { ID = 910001 + (missingShips ? 1 : 0), Destination = planet.Coordinate, Origin = new Coordinate(1, 3, 4), ArriveIn = 0, Ships = missingShips ? null : new Ships() });
            await saved.Task.WaitAsync(TimeSpan.FromSeconds(3));
        } finally { cts.Cancel(); }
        Task Signal() { saved.TrySetResult(); return Task.CompletedTask; }
    }

    [Fact]
    public void Distant_attack_is_scheduled_and_an_earlier_attack_supersedes_it() {
        var settings = SettingsService.GetMergedSettingsFromContents("{}", new List<string> { "{}" });
        var planet = BrainDataFixture.Planet(91002, 5);
        var data = new UserData { celestials = new() { planet }, fleets = new() };
        using var cts = new CancellationTokenSource();
        var worker = Defender(Parent(settings, data), AuditProxy<IFleetScheduler>.Make((m, _) => AuditProxy<IFleetScheduler>.Default(m.ReturnType)), Bridge());
        typeof(WorkerBase).GetField("_ct", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(worker, cts.Token);
        var schedule = worker.GetType().GetMethod("ScheduleFleetSave", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(schedule);
        try {
            schedule.Invoke(worker, new object[] { planet, new AttackerFleet { ArriveIn = 3600 } });
            var dictionary = (System.Collections.IDictionary)worker.GetType().GetField("_scheduledFleetSaves", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(worker);
            var first = dictionary[planet.ID];
            var type = first.GetType();
            var firstTime = (DateTime)type.GetField("ScheduledAtUtc").GetValue(first);
            var token = ((CancellationTokenSource)type.GetField("Cts").GetValue(first)).Token;
            Assert.True(firstTime > DateTime.UtcNow.AddMinutes(50));
            schedule.Invoke(worker, new object[] { planet, new AttackerFleet { ArriveIn = 120 } });
            var second = dictionary[planet.ID];
            Assert.NotSame(first, second);
            Assert.True(token.IsCancellationRequested);
            Assert.True((DateTime)type.GetField("ScheduledAtUtc").GetValue(second) < firstTime);
            schedule.Invoke(worker, new object[] { planet, new AttackerFleet { ArriveIn = 7200 } });
            Assert.Same(second, dictionary[planet.ID]);
        } finally { cts.Cancel(); }
    }

    [Theory]
    [InlineData("worker")]
    [InlineData("abandon")]
    public async Task Settings_disabled_during_refresh_prevent_any_planet_abandonment(string disabled) {
        var settings = SettingsService.GetMergedSettingsFromContents("{\"AutoColonize\":{\"Active\":true,\"Abandon\":{\"Active\":true}}}", new List<string> { "{}" });
        var planet = BrainDataFixture.Planet(91003, 5); planet.Fields.Built = 0;
        var data = new UserData { celestials = new() { planet }, fleets = new(), researches = new() };
        var abandoned = new List<int>();
        var ogame = AuditProxy<IOgameService>.Make((m, args) => {
            if (m.Name == nameof(IOgameService.AbandonCelestial)) { abandoned.Add(((Celestial)args[0]).ID); return Task.FromResult(true); }
            return AuditProxy<IOgameService>.Default(m.ReturnType);
        });
        var bridge = AuditProxy<ITBotOgamedBridge>.Make((m, args) => {
            if (m.Name == nameof(ITBotOgamedBridge.UpdatePlanet)) {
                settings.AutoColonize.Active = false;
                if (disabled == "abandon") settings.AutoColonize.Abandon.Active = false;
                return Task.FromResult((Celestial)args[0]);
            }
            return m.Name == nameof(ITBotOgamedBridge.CheckCelestials) ? Task.FromResult(true) : AuditProxy<ITBotOgamedBridge>.Default(m.ReturnType);
        });
        var calc = AuditProxy<ICalculationService>.Make((m, _) => m.Name == nameof(ICalculationService.ShouldAbandon) ? true : AuditProxy<ICalculationService>.Default(m.ReturnType));
        var scheduler = AuditProxy<IFleetScheduler>.Make((m, _) => AuditProxy<IFleetScheduler>.Default(m.ReturnType));
        var worker = new ColonizeWorker(Parent(settings, data), ogame, scheduler, calc, bridge);
        await Invoke(worker, "Execute");
        Assert.Empty(abandoned);
    }

    private static object Defender(ITBotMain parent, IFleetScheduler scheduler, ITBotOgamedBridge bridge) => Activator.CreateInstance(
        typeof(FleetScheduler).Assembly.GetType("Tbot.Workers.DefenderWorker"), parent,
        AuditProxy<IOgameService>.Make((m, _) => AuditProxy<IOgameService>.Default(m.ReturnType)), scheduler, bridge);
    private static ITBotMain Parent(object settings, UserData data) => AuditProxy<ITBotMain>.Make((m, _) => m.Name switch {
        "get_InstanceSettings" => settings, "get_InstanceAlias" => "offline-defense", "get_UserData" => data,
        _ => AuditProxy<ITBotMain>.Default(m.ReturnType)
    });
    private static ITBotOgamedBridge Bridge() => AuditProxy<ITBotOgamedBridge>.Make((m, a) => m.Name switch {
        nameof(ITBotOgamedBridge.UpdatePlanet) => Task.FromResult((Celestial)a[0]),
        _ => AuditProxy<ITBotOgamedBridge>.Default(m.ReturnType)
    });
    private static async Task Invoke(object worker, string method, params object[] args) => await (Task)worker.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(worker, args);
}
