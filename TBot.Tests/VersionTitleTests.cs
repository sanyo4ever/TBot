using System.Text.Json;
using Tbot.Helpers;
using Xunit;

namespace TBot.Tests;

public class VersionTitleTests {
    private static string ExpectedVersion() {
        using var config = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "fork-version.json")));
        return config.RootElement.GetProperty("upstream_release").GetString()
            + config.RootElement.GetProperty("revision").GetString();
    }

    [Fact]
    public void WindowTitleDisplaysAuthorVersionAndForkLetter() {
        Assert.Equal($"TBot v{ExpectedVersion()}", ConsoleHelpers.GetTitle());
    }

    [Fact]
    public void StatusPrefixKeepsTheForkVersionVisible() {
        Assert.Equal($"Checking - TBot v{ExpectedVersion()}", ConsoleHelpers.GetTitle("Checking"));
    }
}
