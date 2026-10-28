using System.Buffers.Binary;
using RoAura.Services;
using Xunit;

namespace RoAura.Tests;

public sealed class SpringRoStatusSkillCorrelationDiagnosticsTests
{
    private const uint Self = 2004474;

    private static RoPacketLayoutProbe Probe(byte[] frame, long offset, bool boundary = true) =>
        Assert.IsType<RoPacketLayoutProbe>(RoPacketLayoutProbe.Create(frame, offset, boundary));

    private static byte[] TimedStatus(ushort randomStatus, uint randomActor, uint duration)
    {
        var b = new byte[29];
        BinaryPrimitives.WriteUInt16LittleEndian(b, 0x0983);
        BinaryPrimitives.WriteUInt16LittleEndian(b.AsSpan(2), randomStatus);
        BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(4), randomActor);
        b[8] = 1;
        BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(9), duration);
        BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(13), duration);
        BinaryPrimitives.WriteUInt16LittleEndian(b.AsSpan(17), 317);
        return b;
    }

    private static byte[] StatusEnd(ushort randomStatus, uint randomActor)
    {
        var b = new byte[9];
        BinaryPrimitives.WriteUInt16LittleEndian(b, 0x0196);
        BinaryPrimitives.WriteUInt16LittleEndian(b.AsSpan(2), randomStatus);
        BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(4), randomActor);
        return b;
    }

    private static byte[] Option()
    {
        var b = new byte[15];
        BinaryPrimitives.WriteUInt16LittleEndian(b, 0x0229);
        BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(2), Self);
        BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(10), 0xFFAABB02);
        return b;
    }

    private static byte[] Skill(ushort id, uint actor = Self)
    {
        var b = new byte[17];
        BinaryPrimitives.WriteUInt16LittleEndian(b, 0x09CB);
        BinaryPrimitives.WriteUInt16LittleEndian(b.AsSpan(2), id);
        BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(12), actor);
        b[^1] = 1;
        return b;
    }

    [Fact]
    public void EnchantPoison_repeated_180_second_frames_show_stable_unknown_offsets_not_a_guessed_status_id()
    {
        var diagnostics = new SpringRoStatusSkillCorrelationDiagnostics();
        string result = "";
        for (var i = 0; i < 5; i++)
        {
            var start = i * 100L;
            Assert.Null(diagnostics.Observe(Probe(TimedStatus((ushort)(40000 + i),
                (uint)(3000000 + i), 180000), start), Self));
            result = Assert.IsType<string>(diagnostics.Observe(Probe(Skill(138), start + 29), Self));
        }

        Assert.Contains("skill=138", result);
        Assert.Contains("kind=start-candidate", result);
        Assert.Contains("sample=5", result);
        Assert.Contains("stableU16Offsets=", result);
        Assert.Contains("17", result);
        Assert.Contains("selfAidU32Offsets=none", result);
        Assert.DoesNotContain("2004474", result);
        Assert.DoesNotContain("3000000", result);
    }

    [Fact]
    public void Hiding_start_and_end_correlate_without_reading_unreliable_option_hide_bit()
    {
        var diagnostics = new SpringRoStatusSkillCorrelationDiagnostics();
        var start = 1000L;
        Assert.Null(diagnostics.Observe(Probe(TimedStatus(55000, 3001234, 240000), start), Self));
        var appear = Assert.IsType<string>(diagnostics.Observe(Probe(Skill(51), start + 29), Self));
        Assert.Contains("kind=start-candidate", appear);

        var end = 2000L;
        Assert.Null(diagnostics.Observe(Probe(StatusEnd(42000, 3214321), end), Self));
        Assert.Null(diagnostics.Observe(Probe(Option(), end + 9), Self));
        var disappear = Assert.IsType<string>(diagnostics.Observe(Probe(Skill(51), end + 24), Self));
        Assert.Contains("kind=end-candidate", disappear);
        Assert.DoesNotContain("2004474", disappear);
    }

    [Fact]
    public void Rejects_unverified_or_foreign_skill_and_noncontiguous_or_unaligned_frames()
    {
        var diagnostics = new SpringRoStatusSkillCorrelationDiagnostics();
        var status = Probe(TimedStatus(55000, 3001234, 240000), 0);
        Assert.Null(diagnostics.Observe(status, 0));
        Assert.Null(diagnostics.Observe(Probe(Skill(51), 29), 0));
        Assert.Null(diagnostics.Observe(status, Self));
        Assert.Null(diagnostics.Observe(Probe(Skill(51, Self + 1), 29), Self));
        Assert.Null(diagnostics.Observe(status, Self));
        Assert.Null(diagnostics.Observe(Probe(Skill(51), 30), Self));
        Assert.Null(diagnostics.Observe(status, Self));
        Assert.Null(diagnostics.Observe(Probe(Skill(51), 29, boundary: false), Self));
    }

    [Fact]
    public void Stream_reset_forgets_prior_correlations()
    {
        var diagnostics = new SpringRoStatusSkillCorrelationDiagnostics();
        diagnostics.Observe(Probe(TimedStatus(100, 3001234, 180000), 0), Self);
        var first = diagnostics.Observe(Probe(Skill(138), 29), Self);
        Assert.Contains("sample=1", first);
        diagnostics.Reset();
        diagnostics.Observe(Probe(TimedStatus(101, 3001235, 180000), 100), Self);
        var again = diagnostics.Observe(Probe(Skill(138), 129), Self);
        Assert.Contains("sample=1", again);
    }
}
