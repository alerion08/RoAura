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
        Assert.Equal((uint)5002, found.PlayerAid);
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
    public void Automatic_retarget_recovers_only_a_fresh_owned_login_on_the_selected_endpoint()
    {
        var flow = new TcpFlowKey(0xC0000201, 50001, 0xC000020A, 6121);
        var handoff = new MapLoginHandoff();
        bool Match(uint ip, ushort port) => ip == flow.DestinationAddress && port == flow.DestinationPort;

        handoff.Observe(flow, 801, 0x18, MapLogin(), _ => true, 100);
        Assert.Null(handoff.Take(Match, _ => true, 101)); // no SYN evidence
        handoff.Observe(flow, 800, 0x02, [], _ => true, 102);
        handoff.Observe(flow, 802, 0x18, MapLogin(), _ => true, 103);
        handoff.Observe(flow, 801, 0x18, MapLogin(), _ => false, 104);
        handoff.Observe(flow, 801, 0x18, MapLogin(cid: 31234), _ => true, 105);
        handoff.Observe(flow, 800, 0x02, [], _ => true, 106); // duplicate adapter
        Assert.Null(handoff.Take((_, _) => false, _ => true, 107));
        var retained = handoff.Take(Match, key => key == flow, 108);
        Assert.NotNull(retained);
        Assert.Equal(flow, retained.Value.Flow);
        Assert.Equal(31234u, retained.Value.Login.CharacterId);
        Assert.Null(handoff.Take(Match, _ => true, 109));

        handoff.Observe(flow, 1000, 0x02, [], _ => true, 110);
        handoff.Observe(flow, 1001, 0x18, MapLogin(), _ => true, 111);
        Assert.Null(handoff.Take(Match, _ => false, 112)); // socket has already closed
        handoff.Observe(flow, 1200, 0x02, [], _ => true, 113);
        handoff.Observe(flow, 1201, 0x18, MapLogin(), _ => true, 114);
        Assert.Null(handoff.Take(Match, _ => true, 20_000));
    }

    [Fact]
    public void Handoff_does_not_choose_between_two_live_clients_on_the_same_endpoint()
    {
        var first = new TcpFlowKey(1, 50001, 2, 6121);
        var second = first with { SourcePort = 50002 };
        var handoff = new MapLoginHandoff();
        foreach (var flow in new[] { first, second })
        {
            handoff.Observe(flow, 800, 0x02, [], _ => true, 100);
            handoff.Observe(flow, 801, 0x18, MapLogin(cid: flow.SourcePort), _ => true, 101);
        }
        Assert.Null(handoff.Take((_, _) => true, _ => true, 102));
        Assert.Null(handoff.Take((_, _) => true, _ => true, 103));
    }

    [Fact]
    public void Capture_filter_includes_outgoing_map_login_for_character_sessions()
    {
        var method = typeof(NpcapPacketCapture).GetMethod("BuildFilter",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        Assert.NotNull(method);
        string Filter(string address, int port) =>
            (string)method.Invoke(null, new object[] { address, port })!;

        var ip = AppEnvironment.CharacterSessionsEnabled
            ? "host 192.0.2.10 and tcp port 6121"
            : "src host 192.0.2.10 and tcp src port 6121";
        var anyIp = AppEnvironment.CharacterSessionsEnabled
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
