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
