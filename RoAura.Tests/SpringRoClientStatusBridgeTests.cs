using RoAura.Services;
using Xunit;

namespace RoAura.Tests;

public sealed class SpringRoClientStatusBridgeTests
{
    [Fact]
    public void Matched_decoded_start_and_removal_preserve_status_and_duration()
    {
        const uint self = 0x00023456;
        var raw = Convert.FromHexString("8309AABBCCDD02000120BF020020BF0200010000000000000000000000");
        var decoded = Convert.FromHexString("83090600563402000120BF020020BF0200010000000000000000000000");
        var start = SpringRoClientStatusBridge.DecodeMatchedFrame(raw, decoded, self);
        Assert.NotNull(start);
        Assert.Equal((ushort)6, start.Value.StatusId);
        Assert.Equal(180000u, start.Value.RemainingMs);
        Assert.Equal(180000u, start.Value.TotalMs);
        Assert.True(start.Value.Active);

        var removal = SpringRoClientStatusBridge.DecodeMatchedFrame(
            Convert.FromHexString("9601ABCD1234020000"),
            Convert.FromHexString("960105005634020000"), self);
        Assert.NotNull(removal);
        Assert.Equal((ushort)5, removal.Value.StatusId);
        Assert.False(removal.Value.Active);
    }

    [Fact]
    public void Stale_or_foreign_buffer_cannot_claim_wire_event()
    {
        const uint self = 0x00023456;
        var raw = Convert.FromHexString("8309AABBCCDD02000120BF020020BF0200010000000000000000000000");
        var decoded = Convert.FromHexString("83090600563402000120BF020020BF0200010000000000000000000000");
        decoded[12] ^= 1;
        Assert.Null(SpringRoClientStatusBridge.DecodeMatchedFrame(raw, decoded, self));
        decoded[12] ^= 1;
        Assert.Null(SpringRoClientStatusBridge.DecodeMatchedFrame(raw, decoded, 9000));
    }
}
