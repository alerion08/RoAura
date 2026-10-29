using RoAura.Services;
using Xunit;

namespace RoAura.Tests;

public sealed class CharacterPacketRoutingTests
{
    private static CharacterSessionFlow Flow(ushort clientPort) =>
        new(0x01020304, clientPort, 0x05060708, 57143);

    [Fact]
    public void Only_current_observed_TCP_flow_can_supply_statistics_packets()
    {
        var sessions = new CharacterSessionManager();
        Assert.False(sessions.TryResolve(Flow(1000), out _));
        sessions.OnCandidate("SpringRO", Flow(1000), 154386);
        Assert.False(sessions.TryResolve(Flow(1000), out _));
        sessions.OnServerPayload(Flow(1000));
        Assert.True(sessions.TryResolve(Flow(1000), out var first));
        Assert.True(sessions.IsCurrent(first));

        sessions.OnCandidate("SpringRO", Flow(1001), 155665);
        Assert.False(sessions.IsCurrent(first));
        Assert.False(sessions.TryResolve(Flow(1000), out _));
        sessions.OnServerPayload(Flow(1001));
        Assert.True(sessions.TryResolve(Flow(1001), out var second));
        Assert.NotEqual(first.Revision, second.Revision);
        Assert.True(sessions.IsCurrent(second));

        // A delayed FIN/RST for A must not switch or stop B.
        sessions.OnConnectionClosed(Flow(1000));
        Assert.True(sessions.IsCurrent(second));

        sessions.OnConnectionClosed(Flow(1001));
        Assert.False(sessions.IsCurrent(second));
    }

    [Fact]
    public void Relog_to_same_character_must_not_replay_stale_queued_events()
    {
        var sessions = new CharacterSessionManager();
        sessions.OnCandidate("SpringRO", Flow(1000), 154386);
        sessions.OnServerPayload(Flow(1000));
        Assert.True(sessions.TryResolve(Flow(1000), out var previous));
        sessions.OnConnectionClosed(Flow(1000));
        sessions.OnCandidate("SpringRO", Flow(1002), 154386);
        sessions.OnServerPayload(Flow(1002));
        Assert.True(sessions.TryResolve(Flow(1002), out var current));
        Assert.False(sessions.IsCurrent(previous));
        Assert.True(sessions.IsCurrent(current));
    }

    [Fact]
    public void Capture_restart_invalidates_previous_character_revision()
    {
        var sessions = new CharacterSessionManager();
        sessions.OnCandidate("RTM", Flow(1000), 100);
        sessions.OnServerPayload(Flow(1000));
        Assert.True(sessions.TryResolve(Flow(1000), out var previous));
        sessions.OnCaptureInterrupted();
        Assert.False(sessions.IsCurrent(previous));
        Assert.False(sessions.TryResolve(Flow(1000), out _));
    }
}
