using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;

namespace RoAura.Models;

public sealed class CharacterProfile : INotifyPropertyChanged
{
    private string _name = "";
    private string _server = GameServerProfile.Rtm;
    public Guid Id { get; set; } = Guid.NewGuid();
    public uint CharacterId { get; set; }
    public string DetectedServer { get; set; } = "";
    public string Name { get => _name; set { _name = value ?? ""; Changed(); Changed(nameof(Caption)); } }
    public string ServerProfile { get => _server; set { _server = GameServerProfile.Normalize(value); Changed(); Changed(nameof(Caption)); } }
    [JsonIgnore] public string Caption => $"{(string.IsNullOrWhiteSpace(Name) ? "CID " + CharacterId : Name.Trim())} · {ServerProfile} · CID {CharacterId}";
    public event PropertyChangedEventHandler? PropertyChanged;
    private void Changed([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public sealed class CharacterScope : INotifyPropertyChanged
{
    public ObservableCollection<Guid> Characters { get; set; } = [];
    private bool _isAllowed = true;
    [JsonIgnore] public bool IsAllowed { get => _isAllowed; set { if (_isAllowed == value) return; _isAllowed = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsAllowed))); } }
    public event PropertyChangedEventHandler? PropertyChanged;
}

public sealed class NavigationPreference : INotifyPropertyChanged
{
    public AppFeature Feature { get; set; }
    private bool _visible = true;
    public bool Visible { get => _visible; set { if (_visible == value) return; _visible = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Visible))); } }
    public event PropertyChangedEventHandler? PropertyChanged;
}
