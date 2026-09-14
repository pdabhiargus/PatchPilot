using System.Text;

namespace PatchPilot;

/// <summary>Bind proposals and validation to Git content; reject links, binaries and unexpected changes.</summary>
public static class Workspace
{
    public static async Task<string> Commit(string root, CancellationToken ct) =>
        (await Processes.Git(root, ct, "rev-parse", "--verify", "HEAD")).Trim();
    public static async Task Clean(string root, CancellationToken ct)
    {
        if ((await Processes.Git(root, ct, "status", "--porcelain", "--untracked-files=all")).Length != 0)
            throw new InvalidDataException("A clean checkout is required.");
    }
    public static async Task<string> Snapshot(string root, CancellationToken ct)
    {
        var paths = (await Processes.Git(root, ct, "ls-files", "-z")).Split('\0', StringSplitOptions.RemoveEmptyEntries);
        if (paths.Length is 0 or > 30000) throw new InvalidDataException("Unsupported workspace size.");
        var lines = new List<string>();
        long size = 0;
        foreach (var path in paths.Order(StringComparer.Ordinal))
        {
            // Validate tracked paths as well: target repositories with links/protected tracked secrets fail closed.
            var full = Policy.Resolve(root, path, allowProtected: true);
            var bytes = await File.ReadAllBytesAsync(full, ct);
            size += bytes.Length;
            if (size > 100_000_000) throw new InvalidDataException("Workspace exceeds 100 MB.");
            lines.Add(path + ":" + Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)));
        }
        return JsonFiles.Hash(string.Join("\n", lines));
    }
    public static async Task<string[]> Changed(string root, CancellationToken ct)
    {
        var extra = await Processes.Git(root, ct, "ls-files", "--others", "--exclude-standard", "-z");
        if (extra.Length != 0) throw new InvalidDataException("Untracked files are not supported in v0.1.");
        // Include staged and unstaged modifications relative to HEAD.
        return (await Processes.Git(root, ct, "diff", "--no-ext-diff", "--no-textconv", "--name-only", "-z", "HEAD", "--"))
            .Split('\0', StringSplitOptions.RemoveEmptyEntries).Order(StringComparer.Ordinal).ToArray();
    }
    public static async Task Apply(string root, Profile profile, Plan plan, CancellationToken ct)
    {
        Policy.Eligible(profile, plan.Finding, plan.Proposal);
        await Clean(root, ct);
        if (await Commit(root, ct) != plan.BaseCommit || await Snapshot(root, ct) != plan.BaseTree ||
            plan.PolicyHash != JsonFiles.Hash(JsonFiles.Serialize(profile)) || plan.Repository != profile.Repository)
            throw new InvalidDataException("Plan does not match checkout or policy.");
        var writes = new List<(string Path, string Value)>();
        foreach (var edit in plan.Proposal.Edits)
        {
            var path = Policy.Resolve(root, edit.Path);
            var content = ReadText(path);
            writes.Add((path, Policy.ReplaceOnce(content, edit)));
        }
        // Validate every replacement before mutating any file. Failed jobs use disposable checkouts.
        foreach (var write in writes) await File.WriteAllTextAsync(write.Path, write.Value, new UTF8Encoding(false), ct);
    }
    public static string ReadText(string path)
    {
        if (new FileInfo(path).Length > 100_000) throw new InvalidDataException("Context file exceeds 100 KB.");
        var bytes = File.ReadAllBytes(path);
        if (bytes.Contains((byte)0) || (bytes.Length >= 3 && bytes[0] == 239 && bytes[1] == 187 && bytes[2] == 191))
            throw new InvalidDataException("Only UTF-8 text without BOM is supported.");
        return new UTF8Encoding(false, true).GetString(bytes);
    }
}
