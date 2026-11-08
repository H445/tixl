using Newtonsoft.Json.Linq;

namespace Editor.IntegrationTests;

internal sealed class OwnedEditorPaths
{
    internal OwnedEditorPaths(string version, string runId, string settingsRoot, string cacheRoot, string projectsRoot, string tempRoot)
    {
        if (!Guid.TryParseExact(runId, "N", out _))
        {
            throw new ArgumentException("Run ID must be a fresh GUID.", nameof(runId));
        }
        if (!Version.TryParse(version, out var parsedVersion) || parsedVersion.Build != -1)
        {
            throw new ArgumentException("Profile version must be major.minor.", nameof(version));
        }
        RunId = runId;
        ProfileId = "contract-" + runId;
        var folder = "TiXL" + version + "-" + ProfileId;
        _paths = new Dictionary<string, string>
        {
            ["settings"] = Child(settingsRoot, folder),
            ["cache"] = Child(cacheRoot, folder),
            ["projects"] = Child(projectsRoot, folder),
            ["temp"] = Child(tempRoot, folder),
        };
    }

    internal string RunId { get; }
    internal string ProfileId { get; }
    internal string this[string name] => _paths[name];
    internal IReadOnlyCollection<string> Roots => _paths.Values;

    private static string Child(string parent, string folder)
    {
        if (!Path.IsPathFullyQualified(parent))
        {
            throw new InvalidOperationException("Owned-session parent folders must be absolute.");
        }
        return Path.GetFullPath(Path.Combine(parent, folder));
    }

    internal void Claim()
    {
        // Reject all collisions before writing anything; never adopt an existing profile.
        foreach (var root in Roots.Distinct(PathComparer))
        {
            if (Directory.Exists(root) || File.Exists(root))
            {
                throw new IOException("Owned session path already exists: " + root);
            }
        }
        foreach (var root in Roots.Distinct(PathComparer))
        {
            Directory.CreateDirectory(root);
            File.WriteAllText(Path.Combine(root, OwnerFile), RunId);
        }
        File.WriteAllText(Path.Combine(this["settings"], "userSettings.json"), new JObject
        {
            ["ProjectDirectories"] = new JArray(this["projects"]),
            ["ReopenLastProjectOnStartup"] = false,
        }.ToString());
    }

    internal void Verify(JObject reported)
    {
        foreach (var (key, expected) in _paths)
        {
            var actual = reported[key]?.Type == JTokenType.String ? reported[key]!.Value<string>() : null;
            if (actual == null || !Path.IsPathFullyQualified(actual) || !PathComparer.Equals(Path.GetFullPath(actual), expected))
            {
                throw new InvalidDataException($"Editor {key} path is not owned by this session. Refusing mutation.");
            }
        }
        var log = reported["log"]?.Value<string>();
        if (log == null || !IsWithin(log, this["settings"]))
        {
            throw new InvalidDataException("Editor log directory is outside the owned settings folder.");
        }
        foreach (var root in Roots.Distinct(PathComparer))
        {
            VerifyOwnership(root);
        }
    }

    internal void Delete()
    {
        // Verify every root before any recursive deletion, including symlinks inside each tree.
        foreach (var root in Roots.Distinct(PathComparer))
        {
            VerifyOwnership(root);
            RejectLinks(new DirectoryInfo(root));
        }
        foreach (var root in Roots.Distinct(PathComparer))
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private void VerifyOwnership(string root)
    {
        if (!Roots.Contains(Path.GetFullPath(root), PathComparer)
            || File.ReadAllText(Path.Combine(root, OwnerFile)) != RunId)
        {
            throw new IOException("Cannot verify session path ownership: " + root);
        }
        if ((File.GetAttributes(root) & FileAttributes.ReparsePoint) != 0)
        {
            throw new IOException("Refusing a linked session root: " + root);
        }
    }

    private static void RejectLinks(DirectoryInfo root)
    {
        foreach (var entry in root.EnumerateFileSystemInfos())
        {
            if ((entry.Attributes & FileAttributes.ReparsePoint) != 0)
            {
                throw new IOException("Refusing recursive cleanup of linked test data: " + entry.FullName);
            }
            if (entry is DirectoryInfo directory)
            {
                RejectLinks(directory);
            }
        }
    }

    internal static bool IsWithin(string child, string parent)
    {
        if (!Path.IsPathFullyQualified(child))
        {
            return false;
        }
        var relative = Path.GetRelativePath(parent, child);
        return !Path.IsPathRooted(relative) && relative != ".."
               && !relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal);
    }

    private static StringComparer PathComparer => OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
    private const string OwnerFile = ".debug-bridge-test-owner";
    private readonly Dictionary<string, string> _paths;
}
