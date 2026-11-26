using System.IO.Compression;
using System.Text.Json;

namespace RoAura.Services;

internal sealed class SkillIconCatalog
{
    internal sealed record Skill(int Id, string Name, string Code, string IconBase64)
    {
        public string Identifier => $"{Code} · ID {Id}";
    }

    internal sealed record Job(int Id, string Name, int? PreviousJobId, int[] SkillIds);
    private sealed record Entry(int Id, string Name, string Code, string Icon);
    private sealed record Manifest(int SchemaVersion, Job[] Jobs, Entry[] Skills);

    private static readonly Lazy<SkillIconCatalog> Catalog = new(Load);
    internal static SkillIconCatalog Instance => Catalog.Value;

    internal IReadOnlyList<Job> Jobs { get; }
    internal IReadOnlyList<Skill> Skills { get; }
    private readonly Dictionary<int, Job> _jobsById;

    private SkillIconCatalog(Job[] jobs, Skill[] skills)
    {
        Jobs = jobs.OrderBy(job => job.Name, StringComparer.OrdinalIgnoreCase).ToArray();
        Skills = skills.OrderBy(skill => skill.Name, StringComparer.OrdinalIgnoreCase).ThenBy(skill => skill.Id).ToArray();
        _jobsById = jobs.ToDictionary(job => job.Id);
    }

    private static SkillIconCatalog Load()
    {
        using var stream = typeof(SkillIconCatalog).Assembly.GetManifestResourceStream("RoAura.SkillIcons.zip")
            ?? throw new InvalidDataException("Missing bundled skill icon catalog.");
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
        using var index = archive.GetEntry("skill-icons.json")!.Open();
        var manifest = JsonSerializer.Deserialize<Manifest>(index, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        if (manifest is not { SchemaVersion: 1 }) throw new InvalidDataException("Unknown skill icon catalog schema.");

        var skills = new List<Skill>(manifest.Skills.Length);
        foreach (var entry in manifest.Skills)
        {
            if (archive.GetEntry(entry.Icon) is not { } icon) continue;
            using var png = icon.Open();
            using var buffer = new MemoryStream();
            png.CopyTo(buffer);
            skills.Add(new Skill(entry.Id, entry.Name, entry.Code, Convert.ToBase64String(buffer.ToArray())));
        }
        return new SkillIconCatalog(manifest.Jobs, skills.ToArray());
    }

    internal IEnumerable<Job> FindJobs(string query, bool rebirth, bool renewal) =>
        Jobs.Where(job => IsAvailable(job, rebirth, renewal) &&
            job.Name.Contains(query.Trim(), StringComparison.OrdinalIgnoreCase));

    private static bool IsAvailable(Job job, bool rebirth, bool renewal) => job.Id switch
    {
        >= 4008 and <= 4021 => rebirth,
        >= 4054 and <= 4308 => renewal,
        _ => true
    };

    internal IEnumerable<Skill> Search(string query, Job? job = null, bool rebirth = true, bool renewal = true)
    {
        query = query.Trim();
        var isId = int.TryParse(query, out var id);
        var allowed = job is null
            ? Jobs.Where(row => IsAvailable(row, rebirth, renewal)).SelectMany(row => row.SkillIds).ToHashSet()
            : SkillsForJob(job.Id, rebirth, renewal);
        return Skills.Where(skill => allowed.Contains(skill.Id) &&
            (isId ? skill.Id == id :
                skill.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                skill.Code.Contains(query, StringComparison.OrdinalIgnoreCase)));
    }

    private HashSet<int> SkillsForJob(int jobId, bool rebirth, bool renewal)
    {
        var skills = new HashSet<int>();
        var visited = new HashSet<int>();
        int? current = jobId;
        while (current is int id && visited.Add(id) && _jobsById.TryGetValue(id, out var job))
        {
            if (IsAvailable(job, rebirth, renewal)) skills.UnionWith(job.SkillIds);
            current = job.PreviousJobId;
        }
        return skills;
    }
}
