using System.Buffers.Binary;

namespace RoAura.Services;

internal readonly record struct CharacterRosterLevel(uint CharacterId, ushort BaseLevel, ushort JobLevel);

/// <summary>Standard character-server roster layouts. Read only the fields used by the EXP bars.</summary>
internal static class CharacterRosterLevels
{
    private readonly record struct Layout(int Size, int BaseOffset, int JobOffset);
    private static readonly Layout[] Layouts = [new(175, 92, 24), new(155, 72, 24), new(147, 64, 16)];

    public static int FrameLength(ReadOnlySpan<byte> data)
    {
        if (data.Length < 2) return 0;
        var opcode = BinaryPrimitives.ReadUInt16LittleEndian(data);
        var header = opcode switch { 0x006B => 27, 0x099D or 0x0B72 => 4, _ => 0 };
        if (header == 0) return 0;
        if (data.Length < 4) return -1;
        var length = BinaryPrimitives.ReadUInt16LittleEndian(data[2..]);
        var bodyLength = length - header;
        if (bodyLength == 0) return length;
        return Layouts.Count(layout => bodyLength >= layout.Size &&
            bodyLength <= layout.Size * 15 && bodyLength % layout.Size == 0) == 1 ? length : 0;
    }

    public static CharacterRosterLevel[] ReadPayload(ReadOnlySpan<byte> data)
    {
        var levels = ReadAfterSlotInfo(data);
        // The character server can echo a four-byte account ID before the
        // first framed reply. Never retain the echo or other roster fields.
        return levels.Length == 0 && data.Length > 4 ? ReadAfterSlotInfo(data[4..]) : levels;
    }

    private static CharacterRosterLevel[] ReadAfterSlotInfo(ReadOnlySpan<byte> data) =>
        data.Length >= 29 && BinaryPrimitives.ReadUInt16LittleEndian(data) == 0x082D &&
        BinaryPrimitives.ReadUInt16LittleEndian(data[2..]) == 29 ? Read(data[29..]) : Read(data);

    public static CharacterRosterLevel[] Read(ReadOnlySpan<byte> data)
    {
        var length = FrameLength(data);
        if (length <= 0 || data.Length < length) return [];
        var header = BinaryPrimitives.ReadUInt16LittleEndian(data) == 0x006B ? 27 : 4;
        var bodyLength = length - header;
        if (bodyLength == 0) return [];
        var layout = Layouts.Single(candidate => bodyLength >= candidate.Size &&
            bodyLength <= candidate.Size * 15 && bodyLength % candidate.Size == 0);
        var levels = new CharacterRosterLevel[bodyLength / layout.Size];
        for (var i = 0; i < levels.Length; i++)
        {
            var entry = data.Slice(header + i * layout.Size, layout.Size);
            var cid = BinaryPrimitives.ReadUInt32LittleEndian(entry);
            var baseLevel = BinaryPrimitives.ReadUInt16LittleEndian(entry[layout.BaseOffset..]);
            var jobLevel = BinaryPrimitives.ReadUInt32LittleEndian(entry[layout.JobOffset..]);
            if (cid == 0 || baseLevel is 0 or > 1000 || jobLevel is 0 or > 1000)
                return [];
            levels[i] = new(cid, baseLevel, (ushort)jobLevel);
        }
        return levels;
    }
}
