using RoAura.Models;

namespace RoAura.Services;

public static class EquipmentIdentityFormatter
{
    public static string Describe(RoEquipmentPacket packet,
        IReadOnlyDictionary<int, ItemEntry> catalog)
    {
        if (packet.ItemId <= 0) return "";

        var baseName = catalog.TryGetValue(packet.ItemId, out var item)
            ? item.Name
            : $"Item {packet.ItemId}";
        var refine = packet.RefineLevel > 0 ? $"+{packet.RefineLevel} " : "";
        var grade = packet.Grade > 0 ? $" · Grade {packet.Grade}" : "";

        var cards = new[] { packet.Card1, packet.Card2, packet.Card3, packet.Card4 }
            .Where(cardId => cardId > 0)
            .GroupBy(cardId => cardId)
            .Select(group =>
            {
                var cardName = group.Key <= int.MaxValue &&
                               catalog.TryGetValue((int)group.Key, out var card)
                    ? card.Name
                    : $"Card {group.Key}";
                return group.Count() > 1 ? $"{cardName} ×{group.Count()}" : cardName;
            });

        var cardText = string.Join(", ", cards);
        var optionsText = packet.OptionsSignature.Length > 0
            ? $" · Options: {packet.OptionsSignature.Length / 10}"
            : "";
        return $"{refine}{baseName}{grade}" +
               (cardText.Length > 0 ? $" · {cardText}" : "") +
               optionsText;
    }
}
