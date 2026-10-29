using RoAura.Models;
using RoAura.Services;
using Xunit;

namespace RoAura.Tests;

public sealed class CharacterScopeTests
{
    private static CharacterSessionSnapshot Session(string server, uint? cid, CharacterSessionPhase phase = CharacterSessionPhase.ActiveObserved)
        => new(server, cid, phase, 0, 0);

    [Fact]
    public void EmptyScopeRetainsLegacyBehaviorButTargetsRequireObservedIdentity()
    {
        var scope = new CharacterScope();
        var profile = new CharacterProfile { CharacterId = 42, ServerProfile = GameServerProfile.Toc };
        Assert.True(CharacterScopeService.Allows(scope, [profile], default));
        scope.Characters.Add(profile.Id);
        Assert.False(CharacterScopeService.Allows(scope, [profile], default));
        Assert.False(CharacterScopeService.Allows(scope, [profile], Session("ToC", 42, CharacterSessionPhase.Connecting)));
        Assert.False(CharacterScopeService.Allows(scope, [profile], Session("SpringRO", 42)));
        Assert.False(CharacterScopeService.Allows(scope, [profile], Session("ToC", 43)));
        Assert.True(CharacterScopeService.Allows(scope, [profile], Session("ToC", 42)));
        profile.Name = "My Knight";
        Assert.True(CharacterScopeService.Allows(scope, [profile], Session("ToC", 42)));
    }

    [Fact]
    public void DiscoveryIsStableAndDoesNotDuplicateEditedProfiles()
    {
        var settings = new AppSettings();
        var key = new CharacterStatisticsKey("ToC", 42);
        Assert.True(CharacterScopeService.Discover(settings, [key]));
        var id = settings.Characters.Single().Id;
        settings.Characters.Single().ServerProfile = "SpringRO";
        Assert.False(CharacterScopeService.Discover(settings, [key]));
        var other = new AppSettings();
        CharacterScopeService.Discover(other, [key]);
        Assert.Equal(id, other.Characters.Single().Id);
        CharacterScopeService.Discover(other, [new CharacterStatisticsKey("SpringRO", 42)]);
        Assert.Equal(2, other.Characters.Count);
        Assert.NotEqual(id, other.Characters.Last().Id);
    }

    [Fact]
    public void MultipleCharactersCanShareOneOverlay()
    {
        var a = new CharacterProfile { CharacterId = 1, ServerProfile = "ToC" };
        var b = new CharacterProfile { CharacterId = 2, ServerProfile = "ToC" };
        var scope = new CharacterScope();
        scope.Characters.Add(a.Id); scope.Characters.Add(b.Id);
        Assert.True(CharacterScopeService.Allows(scope, [a,b], Session("ToC", 1)));
        Assert.True(CharacterScopeService.Allows(scope, [a,b], Session("ToC", 2)));
        Assert.False(CharacterScopeService.Allows(scope, [a,b], Session("ToC", null)));
    }

    [Fact]
    public void PresetsRetainKnownAliasesAndScopeTargetsRoundtrip()
    {
        var current = new AppSettings();
        var character = new CharacterProfile { CharacterId = 42, DetectedServer = "ToC", ServerProfile = "ToC", Name = "My Knight" };
        current.Characters.Add(character);
        var preset = new AppSettings();
        preset.Characters.Add(new CharacterProfile { Id = character.Id, CharacterId = 42, DetectedServer = "ToC", Name = "Old name" });
        var aura = new SkillAura { X = 321, Y = 123 };
        aura.CharacterScope.Characters.Add(character.Id);
        preset.Skills.Add(aura);
        SettingsSnapshot.Apply(preset, current);
        Assert.Equal("My Knight", current.Characters.Single().Name);
        Assert.Equal(character.Id, current.Skills.Single().CharacterScope.Characters.Single());
        var json = System.Text.Json.JsonSerializer.Serialize(current);
        var restored = System.Text.Json.JsonSerializer.Deserialize<AppSettings>(json)!;
        Assert.Equal(character.Id, restored.Skills.Single().CharacterScope.Characters.Single());
        Assert.Equal(321, restored.Skills.Single().X);
        Assert.Equal(123, restored.Skills.Single().Y);
        Assert.DoesNotContain("IsAllowed", json);
    }

    [Fact]
    public void HomeAndSettingsRemainReachable()
    {
        var settings = new AppSettings();
        settings.EnsureNavigationSections();
        foreach (var entry in settings.NavigationSections) entry.Visible = false;
        Assert.True(settings.IsNavigationVisible(AppFeature.Home));
        Assert.True(settings.IsNavigationVisible(AppFeature.Settings));
        Assert.False(settings.IsNavigationVisible(AppFeature.Cooldowns));
    }
}
