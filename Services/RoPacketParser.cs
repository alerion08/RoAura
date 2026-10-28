using System.Buffers.Binary;
using System.Collections.Concurrent;

namespace RoAura.Services;

public readonly record struct RoStatusPacket(
    ushort PacketId,
    ushort StatusId,
    uint Aid,
    bool Active,
    uint RemainingMs,
    uint TotalMs);

public readonly record struct RoSkillPacket(
    ushort SkillId,
    uint SourceAid,
    uint TargetAid,
    uint DelayMs,
    ushort PacketId = 0x07FB);

public readonly record struct RoSkillCooldownPacket(ushort SkillId, uint DelayMs);

/// <summary>ZC_AID (0x0283) explicitly announces the client's own actor ID.</summary>
public readonly record struct RoSelfAidPacket(uint Aid);
public readonly record struct RoOptionPacket(ushort PacketId, uint Aid, uint OptionMask)
{
    public bool IsHidden => (OptionMask & 0x00000002U) != 0;
}

public readonly record struct RoEquipmentPacket(
    ushort PacketId,
    ushort InventoryIndex,
    int ItemId,
    uint WearLocation,
    bool Equipped,
    bool Success,
    byte RefineLevel = 0,
    byte Grade = 0,
    uint Card1 = 0,
    uint Card2 = 0,
    uint Card3 = 0,
    uint Card4 = 0,
    string OptionsSignature = "")
{
    public string VariantKey => ItemId <= 0
        ? ""
        : $"{ItemId}|{RefineLevel}|{Grade}|{Card1},{Card2},{Card3},{Card4}|{OptionsSignature}";
}

public readonly record struct RoItemAddedPacket(
    ushort InventoryIndex,
    int ItemId,
    int Count);

public readonly record struct RoExperiencePacket(
    uint Aid,
    long Amount,
    ushort ExperienceType,
    ushort SourceType);

public readonly record struct RoProgressPacket(ushort Parameter, ulong Value);
public readonly record struct RoZenyPacket(long Total);

/// <summary>
/// Finds the small set of server packets RoAura needs inside an RO TCP stream.
/// The scanner deliberately validates every candidate because a TCP segment can
/// contain several RO packets and can begin in the middle of one.
/// </summary>
public sealed class RoPacketStreamScanner
{
    private const int CarrySize = 65536;
    // RTM deployments exist with both the pre-grade 67-byte layout and the
    // newer 68-byte layout where refine/grade were moved to the record tail.
    private const int RtmEquipmentListHeaderSize = 5;
    private const int RtmEquipmentEntrySizeLegacy = 67;
    private const int RtmEquipmentEntrySizeCurrent = 68;
    private const int RtmItemAddedPacketSize = 70;
    private const int ExperiencePacketSize = 18;
    private const int ProgressPacketSize = 12;
    private const int ZenyPacketSize = 8;
    private const int EquipChangePacketSize = 7;
    private const int RtmEquipPacketSize = 11;
    private const int RtmUnequipPacketSize = 9;
    private const int TimedStatusPacketSize = 29;
    private const int LegacyTimedStatusPacketSize = 25;
    private const int SimpleStatusPacketSize = 9;
    private const int SelfAidPacketSize = 6;
    private const int OldOptionPacketSize = 13;
    private const int NewOptionPacketSize = 15;
    private const int SkillUsePacketSize = 25;
    private const int LegacySkillUsePacketSize = 15;
    private const int ModernSkillUsePacketSize = 17;
    private const int LegacyDamageSkillPacketSize = 31;
    private const int LegacySkillAckPacketSize = 24;
    private const int DamageSkillPacketSize = 33;
    private const int SkillCooldownPacketSize = 8;
    private const int MaximumEquipmentListLength = 8192;
    private byte[] _carry = [];
    private bool _knownBoundary = true;
    private readonly ConcurrentDictionary<ushort, RoEquipmentPacket> _inventoryEquipment;

    public RoPacketStreamScanner(
        ConcurrentDictionary<ushort, RoEquipmentPacket>? sharedEquipmentCache = null)
    {
        // Npcap can observe the same connection through several adapters
        // (especially with ExitLag/VPN/loopback). All scanners must resolve
        // equip acknowledgements against one inventory identity table.
        _inventoryEquipment = sharedEquipmentCache ?? new();
    }

    public event Action<RoStatusPacket>? StatusChanged;
    public event Action? StreamReset;
    public event Action<RoSelfAidPacket>? SelfAidObserved;
    public event Action<RoOptionPacket>? OptionChanged;
    // DEV diagnostics can report an option opcode whose candidate body was
    // rejected, without recording any packet bytes or actor identifiers.
    public event Action<ushort>? OptionCandidateRejected;
    public event Action<ushort, string>? OptionCandidateRejectedDetailed;
    public event Action<RoSkillPacket>? SkillUsed;
    public event Action<RoSkillCooldownPacket>? SkillCooldown;
    public event Action<RoEquipmentPacket>? EquipmentChanged;
    public event Action<RoItemAddedPacket>? ItemAdded;
    public event Action<RoExperiencePacket>? ExperienceChanged;
    public event Action<RoProgressPacket>? ProgressChanged;
    public event Action<RoZenyPacket>? ZenyChanged;

    public void ResetStream()
    {
        _carry = [];
        _knownBoundary = true;
        StreamReset?.Invoke();
    }

    public void Process(ReadOnlySpan<byte> payload)
    {
        if (payload.IsEmpty) return;

        var carryLength = _carry.Length;
        var combined = new byte[carryLength + payload.Length];
        _carry.CopyTo(combined, 0);
        payload.CopyTo(combined.AsSpan(carryLength));

        // Keep only incomplete frames. Re-scanning 64 KiB of already consumed
        // bytes let a split packet's contents masquerade as new packet headers.
        var offset = 0;
        for (; offset + 2 <= combined.Length; offset++)
        {
            var packetId = BinaryPrimitives.ReadUInt16LittleEndian(combined.AsSpan(offset, 2));
            var start = offset;
            var frameLength = KnownFrameLength(combined, offset, packetId);
            if (frameLength == -1 || (frameLength > 0 && offset + frameLength > combined.Length))
                break;
            switch (packetId)
            {
                case 0x0B41 when TryReadRtmItemAdded(combined, offset, out var length, out var itemAdded):
                    if (TryReadRtmAddedEquipment(combined, offset, out var addedEquipment))
                    {
                        _inventoryEquipment[addedEquipment.InventoryIndex] = addedEquipment;
                        DebugLog.Write($"PACKET EQUIPMENT CACHE: 0x0B41; index={addedEquipment.InventoryIndex}; " +
                                       $"item={addedEquipment.ItemId}; variant={addedEquipment.VariantKey}");
                    }
                    ItemAdded?.Invoke(itemAdded);
                    offset += length - 1;
                    break;

                case 0x0ACC when TryReadExperience(combined, offset, out var length, out var experience):
                    ExperienceChanged?.Invoke(experience);
                    offset += length - 1;
                    break;

                case 0x0ACB when TryReadProgress(combined, offset, out var length, out var progress):
                    ProgressChanged?.Invoke(progress);
                    offset += length - 1;
                    break;

                case 0x00B1 when TryReadZeny(combined, offset, out var length, out var zeny):
                    ZenyChanged?.Invoke(zeny);
                    offset += length - 1;
                    break;

                // Complete lists are consumed once; partial lists stay intact.
                case 0x00A4 when TryReadEquipmentListHeader(combined, offset, out var length):
                    {
                        foreach (var entry in ReadEquipmentListEntries(combined, offset, length))
                        {
                            _inventoryEquipment[entry.InventoryIndex] = entry;
                            if (entry.WearLocation != 0)
                                EquipmentChanged?.Invoke(entry);
                        }
                    }
                    offset += length - 1;
                    break;

                case 0x0B39 when TryReadRtmEquipmentListHeader(combined, offset, out var length,
                             out var rtmEntrySize):
                    {
                        var rtmEquipmentEntries = ReadRtmEquipmentListEntries(combined, offset,
                            length, rtmEntrySize);
                        DebugLog.Write($"PACKET INVENTORY EQUIPMENT: 0x0B39; " +
                                       $"entries={rtmEquipmentEntries.Count}; packetLength={length}");
                        foreach (var entry in rtmEquipmentEntries)
                        {
                            _inventoryEquipment[entry.InventoryIndex] = entry;
                            if (entry.WearLocation != 0)
                                EquipmentChanged?.Invoke(entry);
                        }
                    }
                    offset += length - 1;
                    break;

                case 0x00AA when TryReadEquipChange(combined, offset, true, out var length, out var equipped):
                    if (_inventoryEquipment.TryGetValue(equipped.InventoryIndex, out var equippedIdentity))
                        equipped = CopyIdentity(equipped, equippedIdentity);
                    if (IsPlausibleEquipmentEvent(equipped))
                        EquipmentChanged?.Invoke(equipped);
                    offset += length - 1;
                    break;

                case 0x00AC when TryReadEquipChange(combined, offset, false, out var length, out var unequipped):
                    if (_inventoryEquipment.TryGetValue(unequipped.InventoryIndex, out var unequippedIdentity))
                        unequipped = CopyIdentity(unequipped, unequippedIdentity);
                    if (IsPlausibleEquipmentEvent(unequipped))
                        EquipmentChanged?.Invoke(unequipped);
                    offset += length - 1;
                    break;

                case 0x0999 when TryReadRtmEquipChange(combined, offset, true, out var length, out var rtmEquipped):
                    if (_inventoryEquipment.TryGetValue(rtmEquipped.InventoryIndex, out var rtmEquippedIdentity))
                        rtmEquipped = CopyIdentity(rtmEquipped, rtmEquippedIdentity);
                    if (IsPlausibleEquipmentEvent(rtmEquipped))
                        EquipmentChanged?.Invoke(rtmEquipped);
                    offset += length - 1;
                    break;

                case 0x099A when TryReadRtmEquipChange(combined, offset, false, out var length, out var rtmUnequipped):
                    if (_inventoryEquipment.TryGetValue(rtmUnequipped.InventoryIndex, out var rtmUnequippedIdentity))
                        rtmUnequipped = CopyIdentity(rtmUnequipped, rtmUnequippedIdentity);
                    if (IsPlausibleEquipmentEvent(rtmUnequipped))
                        EquipmentChanged?.Invoke(rtmUnequipped);
                    offset += length - 1;
                    break;

                // TCP chunks are not protocol boundaries. A self-AID packet
                // may follow another complete packet in the same chunk.
                case 0x0283 when _knownBoundary &&
                        TryReadSelfAid(combined, offset, out var length, out var selfAid):
                    SelfAidObserved?.Invoke(selfAid);
                    offset += length - 1;
                    break;

                case 0x0119 when TryReadOptionState(combined, offset, OldOptionPacketSize,
                        out var length, out var oldOption):
                    OptionChanged?.Invoke(oldOption);
                    offset += length - 1;
                    break;

                case 0x0229 when TryReadOptionState(combined, offset, NewOptionPacketSize,
                        out var length, out var newOption):
                    OptionChanged?.Invoke(newOption);
                    offset += length - 1;
                    break;

                case 0x0119 when offset + OldOptionPacketSize <= combined.Length:
                    OptionCandidateRejected?.Invoke(packetId);
                    OptionCandidateRejectedDetailed?.Invoke(packetId,
                        OptionRejectReason(combined, offset, OldOptionPacketSize));
                    break;

                case 0x0229 when offset + NewOptionPacketSize <= combined.Length:
                    OptionCandidateRejected?.Invoke(packetId);
                    OptionCandidateRejectedDetailed?.Invoke(packetId,
                        OptionRejectReason(combined, offset, NewOptionPacketSize));
                    break;

                case 0x0983 when TryReadTimedStatus(combined, offset, TimedStatusPacketSize, packetId,
                             out var length, out var timedStatus):
                    StatusChanged?.Invoke(timedStatus);
                    offset += length - 1;
                    break;

                case 0x043F when TryReadTimedStatus(combined, offset, LegacyTimedStatusPacketSize, packetId,
                             out var length, out var legacyTimedStatus):
                    StatusChanged?.Invoke(legacyTimedStatus);
                    offset += length - 1;
                    break;

                case 0x0196 when TryReadSimpleStatus(combined, offset, out var length, out var simpleStatus):
                    StatusChanged?.Invoke(simpleStatus);
                    offset += length - 1;
                    break;

                case 0x0984 or 0x08FF when TryReadEnteredStatus(combined, offset, packetId,
                        out var length, out var enteredStatus):
                    StatusChanged?.Invoke(enteredStatus);
                    offset += length - 1;
                    break;

                case 0x07FB or 0x0B1A when TryReadSkillUse(combined, offset, packetId, out var length, out var skillUse):
                    SkillUsed?.Invoke(skillUse);
                    offset += length - 1;
                    break;

                case 0x0117 when TryReadGroundSkill(combined, offset, out var length, out var groundSkill):
                    SkillUsed?.Invoke(groundSkill);
                    offset += length - 1;
                    break;

                case 0x011A when TryReadLegacySkillUse(combined, offset, out var length, out var legacySkill):
                    SkillUsed?.Invoke(legacySkill);
                    offset += length - 1;
                    break;

                case 0x09CB when TryReadModernSkillUse(combined, offset, out var length, out var modernSkill):
                    SkillUsed?.Invoke(modernSkill);
                    offset += length - 1;
                    break;

                case 0x013E when TryReadLegacySkillAck(combined, offset, out var length, out var castAck):
                    SkillUsed?.Invoke(castAck);
                    offset += length - 1;
                    break;

                case 0x0114 when TryReadLegacyDamageSkill(combined, offset, out var length, out var oldDamageSkill):
                    SkillUsed?.Invoke(oldDamageSkill);
                    offset += length - 1;
                    break;

                case 0x01DE when TryReadDamageSkill(combined, offset, out var length, out var damageSkill):
                    SkillUsed?.Invoke(damageSkill);
                    offset += length - 1;
                    break;

                case 0x043D when TryReadSkillCooldown(combined, offset, out var length, out var cooldown):
                    SkillCooldown?.Invoke(cooldown);
                    offset += length - 1;
                    break;
            }
            // Even an unsuccessful skill result has a complete frame. Never
            // search its body (or a chat/list body) for status signatures.
            var consumed = offset != start;
            if (frameLength > 0 && (consumed || _knownBoundary))
            {
                offset = Math.Max(offset, start + frameLength - 1);
                _knownBoundary = true;
            }
            else _knownBoundary = consumed;
        }

        var keep = Math.Min(CarrySize, combined.Length - offset);
        _carry = combined.AsSpan(combined.Length - keep, keep).ToArray();
    }

    // Only established layouts are framed here. Unknown/custom packet versions
    // still use the existing validated resynchronization scan; this is NOT a
    // claim that SpringRO's complete packet table has been identified.
    private int KnownFrameLength(byte[] data, int offset, ushort opcode)
    {
        var fixedLength = opcode switch
        {
            0x0B41 => 70, 0x0ACC => 18, 0x0ACB => 12, 0x00B1 => 8,
            0x00AA or 0x00AC => offset + 7 <= data.Length && data[offset + 6] > 1 ? 9 : 7,
            0x0999 => 11, 0x099A => 9,
            0x0283 when _knownBoundary => 6,
            0x0119 => 13, 0x0229 => 15,
            0x0983 => 29, 0x043F => 25, 0x0196 => 9,
            0x0984 => 28, 0x08FF => 24,
            0x07FB => 25, 0x0B1A => 29, 0x013E => 24,
            0x011A => 15, 0x09CB => 17, 0x0117 => 18,
            0x0114 => 31, 0x01DE => 33, 0x043D => 8,
            // Fixed common server frames whose payload is not interesting here.
            0x00B0 when _knownBoundary => 8,
            0x007F when _knownBoundary => 6,
            _ => 0
        };
        if (fixedLength > 0) return fixedLength;
        if (opcode is 0x00A4 or 0x0B39 ||
            (_knownBoundary && opcode is 0x008D or 0x008E or 0x009A or 0x0097))
        {
            if (offset + 4 > data.Length) return -1;
            var length = ReadUInt16(data, offset + 2);
            // Do not let an embedded list signature reserve an arbitrary
            // 64-KiB frame and starve the following real status packets.
            if (opcode == 0x00A4 && (length > MaximumEquipmentListLength || (length - 4) % 20 != 0))
                return 0;
            if (opcode == 0x0B39 && (length < 5 ||
                ((length - 5) % RtmEquipmentEntrySizeLegacy != 0 &&
                 (length - 5) % RtmEquipmentEntrySizeCurrent != 0))) return 0;
            var minimum = opcode == 0x0B39 ? 5 : opcode == 0x0097 ? 28 : opcode == 0x008D ? 8 : 4;
            if (length >= minimum) return length;
        }
        return 0;
    }

    private static bool TryReadSelfAid(byte[] data, int offset, out int length,
        out RoSelfAidPacket packet)
    {
        packet = default;
        length = SelfAidPacketSize;
        if (offset + length > data.Length) return false;
        var aid = ReadUInt32(data, offset + 2);
        if (aid == 0) return false;
        packet = new RoSelfAidPacket(aid);
        return true;
    }

    private static string OptionRejectReason(byte[] data, int offset, int length)
    {
        if (ReadUInt32(data, offset + 2) == 0) return "actor-zero-or-embedded-signature";
        return "unclassified";
    }

    private static bool TryReadOptionState(byte[] data, int offset, int packetLength,
        out int length, out RoOptionPacket packet)
    {
        length = packetLength;
        packet = default;
        if (offset + length > data.Length) return false;
        var aid = ReadUInt32(data, offset + 2);
        // bodyState and healthState are full 16-bit flag masks, not
        // magnitude-limited values. Values above 4096 are valid.
        var option = packetLength == NewOptionPacketSize
            ? ReadUInt32(data, offset + 10) : ReadUInt16(data, offset + 10);
        // rAthena assigns status.karma to this byte; despite its name it is
        // not restricted to a Boolean. It is unrelated to OPTION_HIDE.
        if (aid == 0)
            return false;
        packet = new RoOptionPacket(packetLength == NewOptionPacketSize
            ? (ushort)0x0229 : (ushort)0x0119, aid, option);
        return true;
    }

    private static bool TryReadTimedStatus(byte[] data, int offset, int packetLength,
        ushort packetId, out int length, out RoStatusPacket packet)
    {
        packet = default;
        length = packetLength;
        if (offset + length > data.Length) return false;

        var statusId = ReadUInt16(data, offset + 2);
        var aid = ReadUInt32(data, offset + 4);
        var state = data[offset + 8];
        // 0x0983 layout: Total at +9, Left at +13. 0x043F has only Left at +9.
        var totalMs = length >= TimedStatusPacketSize ? ReadUInt32(data, offset + 9) : 0;
        var remainingMs = ReadUInt32(data, offset + (length >= TimedStatusPacketSize ? 13 : 9));
        if (length < TimedStatusPacketSize) totalMs = remainingMs;
        if (statusId is 0 or > 20000 || aid == 0 || state > 1 ||
            !LooksLikeDuration(remainingMs) || !LooksLikeDuration(totalMs))
            return false;

        packet = new RoStatusPacket(packetId, statusId, aid, state == 1,
            remainingMs, totalMs);
        return true;
    }

    private static bool TryReadSimpleStatus(byte[] data, int offset, out int length,
        out RoStatusPacket packet)
    {
        packet = default;
        length = SimpleStatusPacketSize;
        if (offset + length > data.Length) return false;

        var statusId = ReadUInt16(data, offset + 2);
        var aid = ReadUInt32(data, offset + 4);
        var state = data[offset + 8];
        if (statusId is 0 or > 20000 || aid == 0 || state > 1) return false;

        packet = new RoStatusPacket(0x0196, statusId, aid, state == 1, 0, 0);
        return true;
    }

    private static bool TryReadEnteredStatus(byte[] data, int offset, ushort opcode,
        out int length, out RoStatusPacket packet)
    {
        packet = default;
        length = opcode == 0x0984 ? 28 : 24;
        if (offset + length > data.Length) return false;
        // SET_ENTER has AID before EFST, unlike MSG_STATE_CHANGE.
        var aid = ReadUInt32(data, offset + 2);
        var status = ReadUInt16(data, offset + 6);
        var total = ReadUInt32(data, offset + 8);
        var remaining = opcode == 0x0984 ? ReadUInt32(data, offset + 12) : total;
        if (aid == 0 || status == 0 || !LooksLikeDuration(total) || !LooksLikeDuration(remaining)) return false;
        packet = new(opcode, status, aid, true, remaining, total);
        return true;
    }

    private static bool TryReadGroundSkill(byte[] data, int offset, out int length, out RoSkillPacket packet)
    {
        packet = default;
        length = 18;
        if (offset + length > data.Length) return false;
        var skill = ReadUInt16(data, offset + 2);
        var source = ReadUInt32(data, offset + 4);
        if (skill == 0 || source == 0 || ReadUInt16(data, offset + 10) > 1000 ||
            ReadUInt16(data, offset + 12) > 1000) return false;
        packet = new(skill, source, 0, 0, 0x0117);
        return true;
    }

    private static bool TryReadSkillUse(byte[] data, int offset, ushort opcode, out int length,
        out RoSkillPacket packet)
    {
        packet = default;
        length = opcode == 0x0B1A ? 29 : SkillUsePacketSize;
        if (offset + length > data.Length) return false;

        var sourceAid = ReadUInt32(data, offset + 2);
        var targetAid = ReadUInt32(data, offset + 6);
        var x = ReadUInt16(data, offset + 10);
        var y = ReadUInt16(data, offset + 12);
        var skillId = ReadUInt16(data, offset + 14);
        var delayMs = ReadUInt32(data, offset + 20);
        var disposable = data[offset + 24];
        if (sourceAid == 0 || skillId is 0 or > 20000 ||
            x > 1000 || y > 1000 || disposable > 1)
            return false;

        packet = new RoSkillPacket(skillId, sourceAid, targetAid, delayMs, opcode);
        return true;
    }

    private static bool TryReadLegacySkillUse(byte[] data, int offset, out int length,
        out RoSkillPacket packet)
    {
        packet = default;
        length = LegacySkillUsePacketSize;
        if (offset + length > data.Length) return false;
        var skillId = ReadUInt16(data, offset + 2);
        var target = ReadUInt32(data, offset + 6);
        var source = ReadUInt32(data, offset + 10);
        var result = data[offset + 14];
        // This field is heal/amount, NOT a bounded skill level. Zero and
        // large values are legal. Failed casts must not start cooldowns.
        if (skillId is 0 or > 20000 || target == 0 || source == 0 || result != 1) return false;
        packet = new RoSkillPacket(skillId, source, target, 0, 0x011A);
        return true;
    }

    private static bool TryReadModernSkillUse(byte[] data, int offset, out int length,
        out RoSkillPacket packet)
    {
        packet = default;
        length = ModernSkillUsePacketSize;
        if (offset + length > data.Length) return false;
        var skillId = ReadUInt16(data, offset + 2);
        var target = ReadUInt32(data, offset + 8);
        var source = ReadUInt32(data, offset + 12);
        var result = data[offset + 16];
        if (skillId is 0 or > 20000 || target == 0 || source == 0 || result != 1) return false;
        packet = new RoSkillPacket(skillId, source, target, 0, 0x09CB);
        return true;
    }

    private static bool TryReadLegacySkillAck(byte[] data, int offset, out int length,
        out RoSkillPacket packet)
    {
        packet = default;
        length = LegacySkillAckPacketSize;
        if (offset + length > data.Length) return false;
        var source = ReadUInt32(data, offset + 2);
        var target = ReadUInt32(data, offset + 6);
        var x = ReadUInt16(data, offset + 10);
        var y = ReadUInt16(data, offset + 12);
        var skillId = ReadUInt16(data, offset + 14);
        var delay = ReadUInt32(data, offset + 20);
        if (source == 0 || skillId is 0 or > 20000 ||
            x > 1000 || y > 1000) return false;
        packet = new RoSkillPacket(skillId, source, target, delay, 0x013E);
        return true;
    }

    private static bool TryReadLegacyDamageSkill(byte[] data, int offset, out int length,
        out RoSkillPacket packet)
    {
        packet = default;
        length = LegacyDamageSkillPacketSize;
        if (offset + length > data.Length) return false;
        var skillId = ReadUInt16(data, offset + 2);
        var source = ReadUInt32(data, offset + 4);
        var target = ReadUInt32(data, offset + 8);
        var level = ReadUInt16(data, offset + 26);
        if (skillId is 0 or > 20000 || source == 0 || target == 0 ||
            level > 100) return false;
        packet = new RoSkillPacket(skillId, source, target, 0, 0x0114);
        return true;
    }

    private static bool TryReadDamageSkill(byte[] data, int offset, out int length,
        out RoSkillPacket packet)
    {
        packet = default;
        length = DamageSkillPacketSize;
        if (offset + length > data.Length) return false;
        var skillId = ReadUInt16(data, offset + 2);
        var source = ReadUInt32(data, offset + 4);
        var target = ReadUInt32(data, offset + 8);
        var level = ReadUInt16(data, offset + 28);
        var hitCount = ReadUInt16(data, offset + 30);
        if (skillId is 0 or > 20000 || source == 0 || target == 0 ||
            level > 100 || hitCount > 1000) return false;
        packet = new RoSkillPacket(skillId, source, target, 0, 0x01DE);
        return true;
    }

    private static bool TryReadSkillCooldown(byte[] data, int offset, out int length,
        out RoSkillCooldownPacket packet)
    {
        packet = default;
        length = SkillCooldownPacketSize;
        if (offset + length > data.Length) return false;

        var skillId = ReadUInt16(data, offset + 2);
        var delayMs = ReadUInt32(data, offset + 4);
        if (skillId is 0 or > 20000 || delayMs > 24U * 60 * 60 * 1000) return false;

        packet = new RoSkillCooldownPacket(skillId, delayMs);
        return true;
    }

    private static bool TryReadRtmItemAdded(byte[] data, int offset, out int length,
        out RoItemAddedPacket packet)
    {
        packet = default;
        length = RtmItemAddedPacketSize;
        if (offset + length > data.Length) return false;

        var inventoryIndex = ReadUInt16(data, offset + 2);
        var count = ReadUInt16(data, offset + 4);
        var rawItemId = ReadUInt32(data, offset + 6);
        var identified = data[offset + 10];
        var damaged = data[offset + 11];
        var result = data[offset + 33];
        if (inventoryIndex == 0 || count == 0 || rawItemId == 0 || rawItemId > int.MaxValue ||
            identified > 1 || damaged > 1 || result != 0)
            return false;

        packet = new RoItemAddedPacket(inventoryIndex, (int)rawItemId, count);
        return true;
    }

    private static bool TryReadExperience(byte[] data, int offset, out int length,
        out RoExperiencePacket packet)
    {
        packet = default;
        length = ExperiencePacketSize;
        if (offset + length > data.Length) return false;

        var aid = ReadUInt32(data, offset + 2);
        var amount = BinaryPrimitives.ReadInt64LittleEndian(data.AsSpan(offset + 6, 8));
        var experienceType = ReadUInt16(data, offset + 14);
        var sourceType = ReadUInt16(data, offset + 16);
        if (aid == 0 || experienceType is not (1 or 2) || sourceType > 1 || amount == 0)
            return false;

        packet = new RoExperiencePacket(aid, amount, experienceType, sourceType);
        return true;
    }

    private static bool TryReadProgress(byte[] data, int offset, out int length,
        out RoProgressPacket packet)
    {
        packet = default;
        length = ProgressPacketSize;
        if (offset + length > data.Length) return false;

        var parameter = ReadUInt16(data, offset + 2);
        if (parameter is not (1 or 2 or 22 or 23)) return false;
        var value = BinaryPrimitives.ReadUInt64LittleEndian(data.AsSpan(offset + 4, 8));
        packet = new RoProgressPacket(parameter, value);
        return true;
    }

    private static bool TryReadZeny(byte[] data, int offset, out int length,
        out RoZenyPacket packet)
    {
        packet = default;
        length = ZenyPacketSize;
        if (offset + length > data.Length || ReadUInt16(data, offset + 2) != 20) return false;
        var total = ReadUInt32(data, offset + 4);
        if (total > 2_000_000_000) return false;
        packet = new RoZenyPacket(total);
        return true;
    }

    private static bool TryReadEquipmentListHeader(byte[] data, int offset, out int length)
    {
        length = 0;
        if (offset + 4 > data.Length) return false;
        length = ReadUInt16(data, offset + 2);
        return length >= 4 && length <= MaximumEquipmentListLength &&
               offset + length <= data.Length && (length - 4) % 20 == 0;
    }

    private static List<RoEquipmentPacket> ReadEquipmentListEntries(byte[] data, int offset,
        int length)
    {
        var entries = new List<RoEquipmentPacket>();
        for (var position = offset + 4; position + 20 <= offset + length; position += 20)
        {
            var inventoryIndex = ReadUInt16(data, position);
            var itemId = ReadUInt16(data, position + 2);
            var allowedLocation = ReadUInt16(data, position + 6);
            var wearState = ReadUInt16(data, position + 8);
            if (inventoryIndex == 0 || itemId == 0) continue;
            entries.Add(new RoEquipmentPacket(0x00A4, inventoryIndex, itemId,
                wearState != 0 ? wearState : allowedLocation, wearState != 0, true));
        }
        return entries;
    }

    private static bool TryReadRtmEquipmentListHeader(byte[] data, int offset, out int length,
        out int entrySize)
    {
        length = 0;
        entrySize = 0;
        if (offset + RtmEquipmentListHeaderSize > data.Length) return false;

        length = ReadUInt16(data, offset + 2);
        if (length < RtmEquipmentListHeaderSize || offset + length > data.Length)
            return false;

        // 0x0B39 is shared by inventory, cart and storage. rAthena uses
        // INVTYPE_INVENTORY = 0, CART = 1, STORAGE = 2. The old value 1 here
        // silently discarded the character inventory and left equip ACKs with
        // an index/slot but no Item ID or cards/refine data.
        if (data[offset + 4] != 0) return false;

        var payloadLength = length - RtmEquipmentListHeaderSize;
        entrySize = payloadLength % RtmEquipmentEntrySizeCurrent == 0
            ? RtmEquipmentEntrySizeCurrent
            : payloadLength % RtmEquipmentEntrySizeLegacy == 0
                ? RtmEquipmentEntrySizeLegacy
                : 0;
        return entrySize != 0;
    }

    private static List<RoEquipmentPacket> ReadRtmEquipmentListEntries(byte[] data, int offset,
        int length, int entrySize)
    {
        var entries = new List<RoEquipmentPacket>();
        for (var position = offset + RtmEquipmentListHeaderSize;
             position + entrySize <= offset + length;
             position += entrySize)
        {
            var inventoryIndex = ReadUInt16(data, position);
            var rawItemId = ReadUInt32(data, position + 2);
            var allowedLocation = ReadUInt32(data, position + 7);
            var wearState = ReadUInt32(data, position + 11);
            if (inventoryIndex == 0 || rawItemId == 0 || rawItemId > int.MaxValue) continue;

            var legacyLayout = entrySize == RtmEquipmentEntrySizeLegacy;
            var refineLevel = data[position + (legacyLayout ? 15 : 65)];
            var grade = legacyLayout ? (byte)0 : data[position + 66];
            var cardOffset = position + (legacyLayout ? 16 : 15);
            var optionCountOffset = position + (legacyLayout ? 40 : 39);
            var optionOffset = position + (legacyLayout ? 41 : 40);
            var optionCount = Math.Min(data[optionCountOffset], (byte)5);
            var optionsSignature = BuildOptionsSignature(data.AsSpan(optionOffset, 25), optionCount);

            entries.Add(new RoEquipmentPacket(0x0B39, inventoryIndex, (int)rawItemId,
                wearState != 0 ? wearState : allowedLocation, wearState != 0, true,
                refineLevel, grade,
                ReadUInt32(data, cardOffset),
                ReadUInt32(data, cardOffset + 4),
                ReadUInt32(data, cardOffset + 8),
                ReadUInt32(data, cardOffset + 12),
                optionsSignature));
        }

        return entries;
    }

    private static bool TryReadRtmAddedEquipment(byte[] data, int offset,
        out RoEquipmentPacket packet)
    {
        packet = default;
        if (offset + RtmItemAddedPacketSize > data.Length) return false;

        var inventoryIndex = ReadUInt16(data, offset + 2);
        var rawItemId = ReadUInt32(data, offset + 6);
        var location = ReadUInt32(data, offset + 28);
        var result = data[offset + 33];
        if (inventoryIndex == 0 || rawItemId == 0 || rawItemId > int.MaxValue ||
            location == 0 || result != 0)
            return false;

        packet = new RoEquipmentPacket(0x0B41, inventoryIndex, (int)rawItemId,
            location, false, true,
            data[offset + 68], data[offset + 69],
            ReadUInt32(data, offset + 12),
            ReadUInt32(data, offset + 16),
            ReadUInt32(data, offset + 20),
            ReadUInt32(data, offset + 24),
            BuildOptionsSignature(data.AsSpan(offset + 40, 25)));
        return true;
    }

    private static string BuildOptionsSignature(ReadOnlySpan<byte> optionBytes, int? count = null)
    {
        var signature = new List<byte>(25);
        var slots = Math.Min(count ?? 5, 5);
        for (var index = 0; index < slots; index++)
        {
            var option = optionBytes.Slice(index * 5, 5);
            var populated = false;
            for (var byteIndex = 0; byteIndex < option.Length; byteIndex++)
            {
                if (option[byteIndex] == 0) continue;
                populated = true;
                break;
            }
            if (!populated) continue;
            signature.AddRange(option.ToArray());
        }
        return signature.Count == 0 ? "" : Convert.ToHexString(signature.ToArray());
    }

    private static RoEquipmentPacket CopyIdentity(RoEquipmentPacket target,
        RoEquipmentPacket identity) => target with
    {
        ItemId = identity.ItemId,
        RefineLevel = identity.RefineLevel,
        Grade = identity.Grade,
        Card1 = identity.Card1,
        Card2 = identity.Card2,
        Card3 = identity.Card3,
        Card4 = identity.Card4,
        OptionsSignature = identity.OptionsSignature
    };

    private bool IsPlausibleEquipmentEvent(RoEquipmentPacket packet)
    {
        if (packet.ItemId > 0) return true;

        // Real inventory indexes on supported RO clients are compact. Very high
        // values (for example 44420 observed in the field) are signatures of a
        // packet ID byte pattern found inside another packet, not an equipment
        // acknowledgement. Keep unresolved normal indexes so the UI can explain
        // that a relog is required to populate the identity cache.
        if (packet.InventoryIndex <= 4096) return true;
        DebugLog.Write($"PACKET EQUIPMENT FALSE CANDIDATE: packet=0x{packet.PacketId:X4}; " +
                       $"index={packet.InventoryIndex}; location=0x{packet.WearLocation:X}");
        return false;
    }

    private static bool TryReadEquipChange(byte[] data, int offset, bool equipped,
        out int length, out RoEquipmentPacket packet)
    {
        packet = default;
        length = EquipChangePacketSize;
        if (offset + length > data.Length) return false;
        var inventoryIndex = ReadUInt16(data, offset + 2);
        var wearLocation = ReadUInt16(data, offset + 4);
        var resultOffset = offset + 6;
        // Clients from 2010-06-29 onward insert a two-byte view ID before result.
        if (offset + 9 <= data.Length && data[offset + 6] > 1 && data[offset + 8] <= 1)
        {
            resultOffset = offset + 8;
            length = 9;
        }
        var success = data[resultOffset] == 1;
        if (inventoryIndex == 0 || wearLocation == 0 || data[resultOffset] > 1) return false;
        packet = new RoEquipmentPacket(equipped ? (ushort)0x00AA : (ushort)0x00AC,
            inventoryIndex, 0, wearLocation, equipped && success, success);
        return true;
    }

    private static bool TryReadRtmEquipChange(byte[] data, int offset, bool equipped,
        out int length, out RoEquipmentPacket packet)
    {
        packet = default;
        length = equipped ? RtmEquipPacketSize : RtmUnequipPacketSize;
        if (offset + length > data.Length) return false;

        var inventoryIndex = ReadUInt16(data, offset + 2);
        var wearLocation = ReadUInt32(data, offset + 4);
        var result = data[offset + length - 1];
        // RTM/rAthena V5 acknowledgements use inverted result values:
        // 0 = success, 1 = failure, and equip additionally uses 2 = low level.
        if (inventoryIndex == 0 || wearLocation == 0 || result > (equipped ? 2 : 1)) return false;

        packet = new RoEquipmentPacket(equipped ? (ushort)0x0999 : (ushort)0x099A,
            inventoryIndex, 0, wearLocation, equipped && result == 0, result == 0);
        return true;
    }

    private static bool LooksLikeDuration(uint value) =>
        value == uint.MaxValue || value <= 7U * 24 * 60 * 60 * 1000;

    private static ushort ReadUInt16(byte[] data, int offset) =>
        BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(offset, 2));

    private static uint ReadUInt32(byte[] data, int offset) =>
        BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(offset, 4));
}
