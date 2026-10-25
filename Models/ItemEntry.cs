namespace RoAura.Models;

public sealed class ItemEntry
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string Type { get; set; } = "Loot";

    // The SpringRO export currently omits slot suffixes for a few items whose
    // base names are identical. Keep the stored name intact and expose the
    // unambiguous in-app name separately.
    public string FullName => Id switch
    {
        1250 => "Jur [2]",
        1251 => "Jur [3]",
        _ => Name
    };
}
