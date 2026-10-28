using System.Buffers.Binary;
using RoAura.Services;
using Xunit;

namespace RoAura.Tests;

public sealed class CharacterLoginProbeTests
{
    private static byte[] MapLogin(ushort packetId = 0x0436, uint cid = 12002, bool extended = false,
        byte sex = 1)
    {
        var bytes = new byte[extended ? 23 : 19];
        BinaryPrimitives.WriteUInt16LittleEndian(bytes, packetId);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(2), 5002); // synthetic account
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(6), cid);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(10), 123456); // synthetic auth, never logged
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(14), 567890);
        if (extended)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(18), 0xDEC0ADBE);
            bytes[22] = sex;
        }
        else bytes[18] = sex;
        return bytes;
    }

    [Theory]
    [InlineData((ushort)0x0436)]
    [InlineData((ushort)0x0072)]
    public void Reads_standard_map_login_once(ushort opcode)
    {
        var probe = new CharacterLoginStreamProbe();
        var packet = MapLogin(opcode);
        var found = probe.Process(800, packet);
        Assert.NotNull(found);
        Assert.Equal(opcode, found.PacketId);
        Assert.Equal((uint)12002, found.CharacterId);
        Assert.Equal(19, found.PacketLength);
        Assert.True(probe.Identified);
        Assert.Null(probe.Process(800, packet)); // duplicate adapter / retransmission
        Assert.Null(probe.Process(819, packet));
    }

    [Fact]
    public void Reassembles_fragmented_extended_map_login()
    {
        var probe = new CharacterLoginStreamProbe();
        var bytes = MapLogin(extended: true, cid: 34567);
        Assert.Null(probe.Process(100, bytes.AsSpan(0, 1)));
        Assert.Null(probe.Process(101, bytes.AsSpan(1, 8)));
        Assert.Null(probe.Process(109, bytes.AsSpan(9, 10)));
        var found = probe.Process(119, bytes.AsSpan(19));
        Assert.NotNull(found);
        Assert.Equal((uint)34567, found.CharacterId);
        Assert.Equal(23, found.PacketLength);
    }

    [Fact]
    public void Overlapping_tcp_retransmission_does_not_corrupt_candidate()
    {
        var probe = new CharacterLoginStreamProbe();
        var bytes = MapLogin();
        Assert.Null(probe.Process(400, bytes.AsSpan(0, 8)));
        Assert.Null(probe.Process(400, bytes.AsSpan(0, 8)));
        var result = probe.Process(405, bytes.AsSpan(5));
        Assert.NotNull(result);
        Assert.Equal((uint)12002, result.CharacterId);
    }

    [Fact]
    public void Gap_aborts_probe_without_attributing_later_data()
    {
        var probe = new CharacterLoginStreamProbe();
        var bytes = MapLogin();
        Assert.Null(probe.Process(100, bytes.AsSpan(0, 7)));
        Assert.Null(probe.Process(109, bytes.AsSpan(9)));
        Assert.True(probe.Finished);
        Assert.Null(probe.Process(200, bytes));
    }

    [Fact]
    public void Unknown_opcode_or_invalid_values_never_report_identity()
    {
        var unknown = MapLogin(0x1234);
        var unknownProbe = new CharacterLoginStreamProbe();
        Assert.Null(unknownProbe.Process(1, unknown));
        Assert.True(unknownProbe.Finished);
        Assert.False(unknownProbe.Identified);
        var noCid = MapLogin(cid: 0);
        Assert.Null(new CharacterLoginStreamProbe().Process(1, noCid));
        var invalidSex = MapLogin(sex: 4);
        Assert.Null(new CharacterLoginStreamProbe().Process(1, invalidSex));
        var tooShort = new CharacterLoginStreamProbe();
        Assert.Null(tooShort.Process(1, MapLogin().AsSpan(0, 12)));
    }

    [Fact]
    public void Capture_filter_enables_outgoing_only_for_development()
    {
        var method = typeof(NpcapPacketCapture).GetMethod("BuildFilter",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        Assert.NotNull(method);
        string Filter(string address, int port) =>
            (string)method.Invoke(null, new object[] { address, port })!;

        var ip = AppEnvironment.IsDevelopment
            ? "host 192.0.2.10 and tcp port 6121"
            : "src host 192.0.2.10 and tcp src port 6121";
        var anyIp = AppEnvironment.IsDevelopment
            ? "tcp port 6121"
            : "tcp src port 6121";
        Assert.Equal(ip, Filter("192.0.2.10", 6121));
        Assert.Equal(anyIp, Filter("", 6121));
    }

    [Fact]
    public void Distinct_flows_do_not_share_character_candidate()
    {
        var first = new CharacterLoginStreamProbe();
        var second = new CharacterLoginStreamProbe();
        Assert.Equal((uint)23, first.Process(10, MapLogin(cid: 23))?.CharacterId);
        Assert.Equal((uint)45, second.Process(10, MapLogin(cid: 45))?.CharacterId);
    }
}
