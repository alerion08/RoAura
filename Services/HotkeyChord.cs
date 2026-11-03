using System.Windows.Input;
namespace RoAura.Services;

public readonly record struct HotkeyChord(int VirtualKey, ModifierKeys Modifiers)
{
    public bool Matches(int key, ModifierKeys modifiers) => VirtualKey > 0 && key == VirtualKey && modifiers == Modifiers;
    public static bool IsModifier(int key) => key is 0x10 or 0x11 or 0x12 or 0x5B or 0x5C or 0xA0 or 0xA1 or 0xA2 or 0xA3 or 0xA4 or 0xA5;
    public static bool TryParse(string? text, out HotkeyChord chord)
    {
        chord = default;
        if (string.IsNullOrWhiteSpace(text) || text.Trim().Equals("None",StringComparison.OrdinalIgnoreCase)) return false;
        var parts=text.Split('+',StringSplitOptions.TrimEntries|StringSplitOptions.RemoveEmptyEntries);
        if(parts.Length==0)return false;
        var modifiers=ModifierKeys.None;
        for(var i=0;i<parts.Length-1;i++)
        {
            var modifier=parts[i].ToUpperInvariant() switch
            {
                "CTRL" or "CONTROL"=>ModifierKeys.Control, "ALT"=>ModifierKeys.Alt,
                "SHIFT"=>ModifierKeys.Shift, "WIN" or "WINDOWS"=>ModifierKeys.Windows, _=>(ModifierKeys)(-1)
            };
            if(modifier==(ModifierKeys)(-1))return false;
            modifiers|=modifier;
        }
        var name=parts[^1];
        if(name.Equals("Esc",StringComparison.OrdinalIgnoreCase))name="Escape";
        if(name.Equals("PrintScreen",StringComparison.OrdinalIgnoreCase))name="Snapshot";
        if(name.Length==1 && char.IsAsciiDigit(name[0]))name="D"+name;
        if(!Enum.TryParse<Key>(name,true,out var key) || !Enum.IsDefined(key) || key==Key.None)return false;
        var vk=KeyInterop.VirtualKeyFromKey(key);
        if(vk<=0 || IsModifier(vk))return false;
        chord=new(vk,modifiers);return true;
    }
    public static string Canonical(string? value) => TryParse(value,out var chord) ? chord.ToString().ToUpperInvariant() : "NONE";
    public override string ToString()
    {
        if(VirtualKey<=0)return "None";
        var prefix="";
        if(Modifiers.HasFlag(ModifierKeys.Control))prefix+="Ctrl+";
        if(Modifiers.HasFlag(ModifierKeys.Alt))prefix+="Alt+";
        if(Modifiers.HasFlag(ModifierKeys.Shift))prefix+="Shift+";
        if(Modifiers.HasFlag(ModifierKeys.Windows))prefix+="Win+";
        var key=KeyInterop.KeyFromVirtualKey(VirtualKey);
        return prefix+(key==Key.Snapshot?"PrintScreen":key.ToString());
    }
}

/// <summary>Prevents assigning a key from also invoking it before release.</summary>
public static class HotkeyCaptureGate
{
    public static volatile bool IsCapturing;
    public static volatile int AwaitingReleaseKey;
    public static bool Suppressed(Func<int,bool> isDown)
    {
        if(IsCapturing)return true;
        var key=AwaitingReleaseKey;
        if(key<=0)return false;
        if(isDown(key))return true;
        AwaitingReleaseKey=0;return false;
    }
}
