using RoAura.Services;
using Xunit;

namespace RoAura.Tests;

public sealed class PlayerAidObserverTests
{
    private static CharacterSessionFlow Flow(ushort port) =>
        new(0x7f000001, port, 0x7f000001, 57143);

    [Fact]
    public void Login_may_be_unknown_but_exp_anchors_own_actor_independently()
    {
        var observer = new PlayerAidObserver();
        Assert.Equal(0u, observer.Aid);
        Assert.False(observer.Observe(Flow(50001), 400, "STATUS"));
        Assert.True(observer.Observe(Flow(50001), 12345, "EXP"));
        Assert.Equal(12345u, observer.Aid);
        Assert.Equal("EXP", observer.Source);
        Assert.False(observer.Observe(Flow(50001), 12345, "ZC_AID"));
    }

    [Fact]
    public void Conflicting_aid_on_same_flow_fails_closed()
    {
        var observer = new PlayerAidObserver();
        Assert.True(observer.Observe(Flow(50001), 12345, "ZC_AID"));
        Assert.False(observer.Observe(Flow(50001), 98765, "EXP"));
        Assert.Equal(0u, observer.Aid);
        Assert.Equal("conflict", observer.Source);
        Assert.False(observer.Observe(Flow(50001), 12345, "ZC_AID"));
    }

    [Fact]
    public void Late_fin_for_old_flow_must_not_remove_new_identity()
    {
        var observer = new PlayerAidObserver();
        Assert.True(observer.Observe(Flow(50001), 12345, "ZC_AID"));
        Assert.True(observer.Observe(Flow(50002), 67890, "ZC_AID"));
        observer.OnClosed(Flow(50001));
        Assert.Equal(67890u, observer.Aid);
        observer.OnClosed(Flow(50002));
        Assert.Equal(0u, observer.Aid);
    }

    [Fact]
    public void Reset_discards_stale_identity_from_prior_capture_generation()
    {
        var observer = new PlayerAidObserver();
        observer.Observe(Flow(50001), 12345, "EXP");
        observer.Reset();
        Assert.Equal(0u, observer.Aid);
        Assert.Equal("", observer.Source);
    }

    [Fact]
    public void Embedded_0283_signature_in_unrelated_payload_cannot_identify_self()
    {
        var scanner = new RoPacketStreamScanner();
        var observed = new List<RoSelfAidPacket>();
        scanner.SelfAidObserved += observed.Add;
        var arbitrary = new byte[] {0xF0, 0x0D, 0x83, 0x02, 0x39, 0x30, 0x00, 0x00, 0xAA};
        scanner.Process(arbitrary);
        Assert.Empty(observed);
    }

    [Fact]
    public void Self_actor_evidence_is_restricted_to_the_observed_TCP_flow()
    {
        var observer = new PlayerAidObserver();
        var oldFlow = Flow(50200);
        var newFlow = Flow(50201);
        Assert.True(observer.Observe(oldFlow, 4001, "EXP"));
        Assert.Equal(4001u, observer.AidForFlow(oldFlow));
        Assert.Equal(0u, observer.AidForFlow(newFlow));
        Assert.Equal("", observer.SourceForFlow(newFlow));
        Assert.True(observer.Observe(newFlow, 5002, "EXP"));
        Assert.Equal(0u, observer.AidForFlow(oldFlow));
        Assert.Equal(5002u, observer.AidForFlow(newFlow));
        Assert.Equal("EXP", observer.SourceForFlow(newFlow));
    }

    [Fact]
    public void Conflicting_evidence_disables_only_the_current_flow_identity()
    {
        var observer = new PlayerAidObserver();
        Assert.True(observer.Observe(Flow(50200), 4001, "ZC_AID"));
        Assert.False(observer.Observe(Flow(50200), 5002, "EXP"));
        Assert.Equal("conflict", observer.SourceForFlow(Flow(50200)));
        Assert.Equal(0u, observer.AidForFlow(Flow(50200)));
        Assert.True(observer.Observe(Flow(50201), 5002, "EXP"));
        Assert.Equal(5002u, observer.AidForFlow(Flow(50201)));
    }

    [Fact]
    public void Explicit_server_identity_packet_is_decoded()
    {
        var payload = new byte[] {0x83, 0x02, 0x39, 0x30, 0x00, 0x00};
        var scanner = new RoPacketStreamScanner();
        var observed = new List<RoSelfAidPacket>();
        scanner.SelfAidObserved += observed.Add;
        scanner.Process(payload[..2]);
        Assert.Empty(observed);
        scanner.Process(payload.AsSpan(2));
        Assert.Equal(12345u, Assert.Single(observed).Aid);
    }
}
