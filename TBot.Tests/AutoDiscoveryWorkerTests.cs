using TBot.Ogame.Infrastructure.Enums;
using TBot.Ogame.Infrastructure.Models;

namespace TBot.Tests;

public class AutoDiscoveryWorkerTests {
    [Fact]
    public async Task Normal_cycle_sends_discoveries_and_schedules_next_check() {
        await using var fixture = await AutoDiscoveryFixture.Create();

        await fixture.RunCycle();

        Assert.Equal(3, fixture.Destinations.Count);
        Assert.Equal(3, fixture.Destinations.Select(c => (c.Galaxy, c.System, c.Position)).Distinct().Count());
        fixture.AssertRetryScheduled();
    }

    [Fact]
    public async Task Occupied_slots_do_not_prevent_recovery_on_later_cycles() {
        await using var fixture = await AutoDiscoveryFixture.Create();
        fixture.Data.slots.InUse = 8;
        await fixture.RunCycle();
        Assert.Empty(fixture.Destinations);
        fixture.AssertFallbackRetryScheduled();

        fixture.Data.slots.InUse = 0;
        await fixture.RunCycle();
        Assert.Equal(3, fixture.Destinations.Count);
        fixture.AssertRetryScheduled();

        await fixture.RunCycle();
        Assert.Equal(6, fixture.Destinations.Count);
        fixture.AssertRetryScheduled();
    }

    [Fact]
    public async Task Expired_blacklist_entries_become_available_between_live_entries() {
        await using var fixture = await AutoDiscoveryFixture.Create();
        fixture.FillBlacklist();
        var expired = fixture.Data.discoveryBlackList.Keys.Single(c => c.System == 2 && c.Position == 2);
        fixture.Data.discoveryBlackList[expired] = DateTime.Now.AddDays(-1);
        fixture.Discoveries = 1;

        await fixture.RunCycle();

        var destination = Assert.Single(fixture.Destinations);
        Assert.Equal((1, 2, 2), (destination.Galaxy, destination.System, destination.Position));
        fixture.AssertRetryScheduled();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Fully_blacklisted_targets_finish_and_schedule_retry(bool random) {
        await using var fixture = await AutoDiscoveryFixture.Create();
        fixture.Settings.AutoDiscovery.RandomizeDestination = random;
        // Random selection stays in the origin galaxy even if another galaxy is available.
        if (random)
            fixture.Data.serverData.Galaxies = 2;
        fixture.FillBlacklist();

        await fixture.RunBoundedCycle();

        Assert.Empty(fixture.Destinations);
        fixture.AssertFallbackRetryScheduled();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Failed_sends_finish_and_schedule_retry_without_an_inflight_discovery(bool random) {
        await using var fixture = await AutoDiscoveryFixture.Create();
        fixture.Settings.AutoDiscovery.RandomizeDestination = random;
        fixture.SendSucceeds = false;

        await fixture.RunBoundedCycle();

        Assert.NotEmpty(fixture.Destinations);
        Assert.Empty(fixture.Data.fleets);
        fixture.AssertFallbackRetryScheduled();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_failed_send_does_not_consume_the_successful_discovery_budget(bool random) {
        await using var fixture = await AutoDiscoveryFixture.Create();
        fixture.Settings.AutoDiscovery.RandomizeDestination = random;
        fixture.FailedSendsRemaining = 1;

        await fixture.RunBoundedCycle();

        Assert.Equal(4, fixture.Destinations.Count);
        Assert.Equal(3, fixture.Data.fleets.Count);
        fixture.AssertRetryScheduled();
    }

    [Fact]
    public async Task Exhausted_targets_keep_waiting_for_an_existing_discovery() {
        await using var fixture = await AutoDiscoveryFixture.Create();
        fixture.FillBlacklist();
        fixture.Data.fleets.Add(new Fleet { Mission = Missions.Discovery, BackIn = 60 });
        fixture.Data.fleets.Add(new Fleet { Mission = Missions.Discovery, BackIn = 120 });
        fixture.Data.slots.InUse = 2;

        await fixture.RunCycle();

        Assert.Empty(fixture.Destinations);
        fixture.AssertRetryScheduled();
        Assert.InRange(fixture.Worker.Period.TotalMilliseconds, 120_000, 170_000);
    }
}
