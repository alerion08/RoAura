using System.Buffers.Binary;

namespace RoAura.Services;

internal enum MonsterCombatEventKind { Visible, Spawn, Damage, Removed, Death, MapChanged }

internal readonly record struct MonsterCombatEvent(MonsterCombatEventKind Kind, uint Actor,
    uint Attacker = 0, ushort MonsterId = 0)
{
    public static int FrameLength(ReadOnlySpan<byte> data, ushort opcode, bool atBoundary)
    {
        var modernLength = opcode switch
        {
            0x09FD => 114, // Walking unit
            0x09FE => 107, // Spawned unit
            0x09FF => 108, // Idle unit
            _ => 0
        };
        if (modernLength > 0)
        {
            if (data.Length < 4) return -1;
            var length = BinaryPrimitives.ReadUInt16LittleEndian(data[2..]);
            // Client versions before the four-byte shield field use the shorter layout.
            return length == modernLength || length == modernLength - 4 ? length : 0;
        }

        if (!atBoundary) return 0;
        return opcode switch
        {
            0x0080 => 7,
            0x0091 => 22,
            0x0092 => 28,
            0x008A => 29,
            0x02E1 => 33,
            _ => 0
        };
    }

    public static MonsterCombatEvent? Read(ReadOnlySpan<byte> data, ushort opcode)
    {
        switch (opcode)
        {
            case 0x09FD or 0x09FE or 0x09FF:
                var monster = BinaryPrimitives.ReadUInt16LittleEndian(data[23..]);
                var actor = U32(data, 5);
                return data[4] == 5 && U32(data, 9) == 0 && actor != 0 && monster > 0
                    ? new(opcode == 0x09FE ? MonsterCombatEventKind.Spawn : MonsterCombatEventKind.Visible,
                        actor, MonsterId: monster) : null;
            case 0x0080:
                return new(data[6] == 1 ? MonsterCombatEventKind.Death : MonsterCombatEventKind.Removed,
                    U32(data, 2));
            case 0x0091 or 0x0092:
                return new(MonsterCombatEventKind.MapChanged, 0);
            case 0x008A when I16(data, 22) > 0 || I16(data, 27) > 0:
            case 0x02E1 when I32(data, 22) > 0 || I32(data, 29) > 0:
            case 0x08C8 when data[26] == 0 && (I32(data, 22) > 0 || I32(data, 30) > 0):
                return new(MonsterCombatEventKind.Damage, U32(data, 6), U32(data, 2));
            case 0x0114 when I16(data, 24) > 0:
            case 0x01DE when I32(data, 24) > 0:
                return new(MonsterCombatEventKind.Damage, U32(data, 8), U32(data, 4));
            default:
                return null;
        }
    }

    private static uint U32(ReadOnlySpan<byte> data, int offset) =>
        BinaryPrimitives.ReadUInt32LittleEndian(data[offset..]);

    private static short I16(ReadOnlySpan<byte> data, int offset) =>
        BinaryPrimitives.ReadInt16LittleEndian(data[offset..]);

    private static int I32(ReadOnlySpan<byte> data, int offset) =>
        BinaryPrimitives.ReadInt32LittleEndian(data[offset..]);
}

internal sealed class MonsterKillTracker
{
    private static readonly TimeSpan MaximumHitAge = TimeSpan.FromSeconds(3);
    private const int MaximumVisibleMonsters = 4096;
    private readonly Dictionary<uint, MonsterState> _monsters = [];

    private readonly record struct MonsterState(ushort MonsterId, uint Attacker, DateTime HitAtUtc);

    public ushort? Observe(MonsterCombatEvent packet, uint selfAid, DateTime nowUtc)
    {
        switch (packet.Kind)
        {
            case MonsterCombatEventKind.Visible or MonsterCombatEventKind.Spawn:
                if (_monsters.TryGetValue(packet.Actor, out var visible))
                {
                    if (visible.MonsterId != packet.MonsterId || packet.Kind == MonsterCombatEventKind.Spawn)
                        _monsters[packet.Actor] = new(packet.MonsterId, 0, default);
                }
                else if (_monsters.Count < MaximumVisibleMonsters)
                    _monsters.Add(packet.Actor, new(packet.MonsterId, 0, default));
                break;
            case MonsterCombatEventKind.Damage:
                if (_monsters.TryGetValue(packet.Actor, out var target))
                    _monsters[packet.Actor] = target with { Attacker = packet.Attacker, HitAtUtc = nowUtc };
                break;
            case MonsterCombatEventKind.Death:
                if (_monsters.Remove(packet.Actor, out var dead) && selfAid != 0 && dead.Attacker == selfAid &&
                    nowUtc >= dead.HitAtUtc && nowUtc - dead.HitAtUtc <= MaximumHitAge)
                    return dead.MonsterId;
                break;
            case MonsterCombatEventKind.Removed:
                _monsters.Remove(packet.Actor);
                break;
            case MonsterCombatEventKind.MapChanged:
                Reset();
                break;
        }
        return null;
    }

    public void Reset() => _monsters.Clear();
}
