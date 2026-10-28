using RoAura.Services;
using Xunit;

namespace RoAura.Tests;

public sealed class CharacterSessionManagerTests
{
    private static CharacterSessionFlow Flow(ushort clientPort) =>
        new(0x01020304, clientPort, 0x05060708, 57143);

    [Fact]
    public void Character_select_A_to_B_to_A_tracks_each_connection()
    {
        var sessions = new CharacterSessionManager();
        var phases = new List<(uint? Id, CharacterSessionPhase Phase)>();
        sessions.StateChanged += state => phases.Add((state.CharacterId, state.Phase));

        sessions.OnCandidate("SpringRO", Flow(62763), 154386);
        Assert.Equal(CharacterSessionPhase.Connecting, sessions.Current.Phase);
        sessions.OnServerPayload(Flow(62763));
        Assert.Equal(CharacterSessionPhase.ActiveObserved, sessions.Current.Phase);

        sessions.OnCandidate("SpringRO", Flow(62790), 155665);
        sessions.OnServerPayload(Flow(62790));
        sessions.OnConnectionClosed(Flow(62763));
        Assert.Equal((uint)155665, sessions.Current.CharacterId);
        Assert.Equal(CharacterSessionPhase.ActiveObserved, sessions.Current.Phase);

        sessions.OnCandidate("SpringRO", Flow(49240), 154386);
        sessions.OnServerPayload(Flow(49240));
        Assert.Equal((uint)154386, sessions.Current.CharacterId);
        Assert.Equal(CharacterSessionPhase.ActiveObserved, sessions.Current.Phase);
        Assert.Equal(8, phases.Count);
        Assert.Equal((154386u, CharacterSessionPhase.Suspended), phases[2]);
        Assert.Equal((155665u, CharacterSessionPhase.Suspended), phases[5]);
    }

    [Fact]
    public void Session_binds_player_AID_and_never_learns_nearby_actor_as_self()
    {
        var manager = new CharacterSessionManager();
        manager.OnCandidate("SpringRO", Flow(60000), 154386, 5002);
        manager.OnServerPayload(Flow(60000));
        Assert.Equal((uint)5002, manager.Current.PlayerAid);
        Assert.True(manager.TryResolve(Flow(60000), out var identity));
        Assert.Equal((uint)5002, identity.PlayerAid);
        Assert.True(manager.IsCurrent(identity));
        manager.OnCandidate("SpringRO", Flow(60001), 155665, 7004);
        Assert.False(manager.IsCurrent(identity));
        manager.OnServerPayload(Flow(60001));
        Assert.Equal((uint)7004, manager.Current.PlayerAid);
    }

    [Fact]
    public void Duplicate_probe_or_wrong_flow_response_does_not_change_state()
    {
        var sessions = new CharacterSessionManager();
        var events = 0;
        sessions.StateChanged += _ => events++;
        sessions.OnCandidate("SpringRO", Flow(60000), 1001);
        sessions.OnCandidate("SpringRO", Flow(60000), 1001);
        sessions.OnServerPayload(Flow(60001));
        Assert.Equal(CharacterSessionPhase.Connecting, sessions.Current.Phase);
        sessions.OnServerPayload(Flow(60000));
        sessions.OnServerPayload(Flow(60000));
        Assert.Equal(CharacterSessionPhase.ActiveObserved, sessions.Current.Phase);
        Assert.Equal(2, events);
    }

    [Fact]
    public void Closing_old_flow_or_capture_reset_never_assigns_wrong_character()
    {
        var sessions = new CharacterSessionManager();
        sessions.OnCandidate("RTM", Flow(10001), 42);
        sessions.OnServerPayload(Flow(10001));
        sessions.OnCandidate("RTM", Flow(10002), 99);
        sessions.OnConnectionClosed(Flow(10001));
        Assert.Equal((uint)99, sessions.Current.CharacterId);
        Assert.Equal(CharacterSessionPhase.Connecting, sessions.Current.Phase);
        sessions.OnCaptureInterrupted();
        Assert.Equal(CharacterSessionPhase.Suspended, sessions.Current.Phase);
        sessions.OnServerPayload(Flow(10002));
        Assert.Equal(CharacterSessionPhase.Suspended, sessions.Current.Phase);
    }

    [Fact]
    public void Late_start_has_no_assumed_character()
    {
        var sessions = new CharacterSessionManager();
        Assert.Null(sessions.Current.CharacterId);
        Assert.Equal(CharacterSessionPhase.Unknown, sessions.Current.Phase);
        sessions.OnServerPayload(Flow(60000));
        sessions.OnConnectionClosed(Flow(60000));
        Assert.Equal(CharacterSessionPhase.Unknown, sessions.Current.Phase);
        sessions.OnCandidate("", Flow(60000), 100);
        sessions.OnCandidate("SpringRO", Flow(60000), 0);
        Assert.Null(sessions.Current.CharacterId);
    }

    [Fact]
    public void Closing_active_flow_marks_suspended_and_retains_last_identity_as_history_only()
    {
        var sessions = new CharacterSessionManager();
        sessions.OnCandidate("SpringRO", Flow(60000), 123);
        sessions.OnServerPayload(Flow(60000));
        sessions.OnConnectionClosed(Flow(60000));
        Assert.Equal(CharacterSessionPhase.Suspended, sessions.Current.Phase);
        Assert.Equal((uint)123, sessions.Current.CharacterId);
        sessions.OnConnectionClosed(Flow(60000));
        Assert.Equal(CharacterSessionPhase.Suspended, sessions.Current.Phase);
    }
}
