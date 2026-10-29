using RoAura.Services;
using Xunit;

namespace RoAura.Tests;

public sealed class DevelopmentIsolationTests
{
    [Fact]
    public void PathsAndProcessIdentityFollowBuildMode()
    {
        var folder = AppEnvironment.IsDevelopment ? "RoAura-Dev" : "RoAura";
        Assert.Equal(folder, AppEnvironment.DataFolderName);
        Assert.Equal(Path.Combine(AppEnvironment.RoamingDirectory, "settings.json"), new SettingsStore().SettingsPath);
        Assert.Equal(Path.Combine(AppEnvironment.LocalDirectory, MarketWatchHistoryStore.FileName), new MarketWatchHistoryStore().FilePath);
        Assert.Equal(Path.Combine(AppEnvironment.RoamingDirectory, "debug.log"), DebugLog.PathName);
        Assert.NotEqual(AppEnvironment.SingleInstanceMutexName, AppEnvironment.ActivateEventName);
        Assert.Contains(folder == "RoAura-Dev" ? "RoAura.Dev." : "RoAura.",
            AppEnvironment.SingleInstanceMutexName);
    }
}
