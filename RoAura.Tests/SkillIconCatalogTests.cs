using RoAura.Services;
using Xunit;

namespace RoAura.Tests;

public sealed class SkillIconCatalogTests
{
    [Fact]
    public void Bundled_catalog_loads_every_icon_and_searches_by_name_code_and_id()
    {
        var catalog = SkillIconCatalog.Instance;
        Assert.Equal(80, catalog.Jobs.Count);
        Assert.Equal(1126, catalog.Skills.Count);
        var skill = Assert.Single(catalog.Search("1"));
        Assert.Equal("Basic Skill", skill.Name);
        Assert.Same(skill, Assert.Single(catalog.Search("nv_basic")));
        Assert.Contains(skill, catalog.Search("basic skill"));
        Assert.All(catalog.Skills, row =>
            Assert.Equal("89504E470D0A1A0A", Convert.ToHexString(Convert.FromBase64String(row.IconBase64).AsSpan(0, 8))));
    }

    [Fact]
    public void Job_filter_includes_ancestors_and_excludes_unrelated_jobs()
    {
        var catalog = SkillIconCatalog.Instance;
        var swordman = Assert.Single(catalog.Jobs, job => job.Id == 1);
        Assert.Single(catalog.Search("2", swordman));
        Assert.Single(catalog.Search("1", swordman));
        Assert.Empty(catalog.Search("16", swordman));
        Assert.Equal(catalog.Search("", swordman).Select(skill => skill.Id).Distinct().Count(),
            catalog.Search("", swordman).Count());
    }
}
