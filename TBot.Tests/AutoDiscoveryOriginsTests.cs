using TBot.Ogame.Infrastructure.Models;

namespace TBot.Tests;

public class AutoDiscoveryOriginsTests {
    [Fact]
    public async Task Deterministic_selection_sends_from_the_closest_configured_origin() {
        await using var fixture = await AutoDiscoveryFixture.Create();
        fixture.Data.serverData.Systems = 7;
        fixture.Data.serverData.DonutSystem = false;
        fixture.SetOrigins(new Coordinate(1, 1, 5), new Coordinate(1, 5, 5));
        fixture.Discoveries = 1;
        fixture.LeaveTargetsAvailable(new Coordinate(1, 4, 1));

        await fixture.RunCycle();

        var send = Assert.Single(fixture.Sends);
        Assert.Equal((1, 5, 5), Coords(send.Origin));
        Assert.Equal((1, 4, 1), Coords(send.Destination));
        fixture.AssertRetryScheduled();
    }

    [Fact]
    public async Task Origins_in_different_galaxies_send_to_their_local_targets() {
        await using var fixture = await AutoDiscoveryFixture.Create();
        fixture.Data.serverData.Galaxies = 2;
        fixture.SetOrigins(new Coordinate(1, 2, 5), new Coordinate(2, 2, 5));
        fixture.Discoveries = 2;
        fixture.LeaveTargetsAvailable(new Coordinate(1, 2, 1), new Coordinate(2, 2, 1));

        await fixture.RunCycle();

        Assert.Equal(2, fixture.Sends.Count);
        Assert.Equal(new[] { 1, 2 }, fixture.Sends.Select(send => send.Origin.Galaxy).Order().ToArray());
        Assert.All(fixture.Sends, send => Assert.Equal(send.Origin.Galaxy, send.Destination.Galaxy));
        fixture.AssertRetryScheduled();
    }

    [Fact]
    public async Task Overlapping_equal_distance_origins_send_each_target_only_once() {
        await using var fixture = await AutoDiscoveryFixture.Create();
        fixture.Data.serverData.Systems = 7;
        fixture.Data.serverData.DonutSystem = false;
        // Reverse configuration order to exercise the worker's stable coordinate ordering.
        fixture.SetOrigins(new Coordinate(1, 4, 5), new Coordinate(1, 2, 5));
        fixture.Discoveries = 3;
        fixture.LeaveTargetsAvailable(new Coordinate(1, 3, 1), new Coordinate(1, 3, 2));

        await fixture.RunCycle();

        Assert.Equal(2, fixture.Sends.Count);
        Assert.Equal(2, fixture.Sends.Select(send => Coords(send.Destination)).Distinct().Count());
        Assert.All(fixture.Sends, send => Assert.Equal((1, 2, 5), Coords(send.Origin)));
        fixture.AssertRetryScheduled();
    }

    [Theory]
    [InlineData(true, 1)]
    [InlineData(false, 4)]
    public async Task Nearest_origin_respects_system_donut_wrapping(bool donut, int expectedOriginSystem) {
        await using var fixture = await AutoDiscoveryFixture.Create();
        fixture.Data.serverData.Systems = 7;
        fixture.Data.serverData.DonutSystem = donut;
        fixture.SetOrigins(new Coordinate(1, 1, 5), new Coordinate(1, 4, 5));
        fixture.Discoveries = 1;
        fixture.LeaveTargetsAvailable(new Coordinate(1, 7, 1));

        await fixture.RunCycle();

        var send = Assert.Single(fixture.Sends);
        Assert.Equal((1, expectedOriginSystem, 5), Coords(send.Origin));
        Assert.Equal((1, 7, 1), Coords(send.Destination));
        fixture.AssertRetryScheduled();
    }

    [Fact]
    public async Task Missing_configured_origin_falls_back_to_an_owned_celestial() {
        await using var fixture = await AutoDiscoveryFixture.Create();
        fixture.SetOrigins(new Coordinate(1, 1, 5), new Coordinate(1, 3, 5));
        // Both configured locations are stale; the owned celestial is the valid fallback.
        fixture.Data.celestials.RemoveAt(0);
        fixture.Data.celestials[0].Coordinate = new Coordinate(1, 2, 5);
        fixture.Discoveries = 1;
        fixture.LeaveTargetsAvailable(new Coordinate(1, 2, 1));

        await fixture.RunCycle();

        var send = Assert.Single(fixture.Sends);
        Assert.Equal((1, 2, 5), Coords(send.Origin));
        Assert.Equal((1, 2, 1), Coords(send.Destination));
        fixture.AssertRetryScheduled();
    }

    private static (int Galaxy, int System, int Position) Coords(Coordinate coordinate) =>
        (coordinate.Galaxy, coordinate.System, coordinate.Position);
}
