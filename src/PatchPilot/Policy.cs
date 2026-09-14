using System.Text.RegularExpressions;

namespace PatchPilot;

/// <summary>Deterministic gates. Model assessments cannot grant permission or change these rules.</summary>
public static class Policy
{
    public static void Validate(Profile p)
    {
        if (!Regex.IsMatch(p.Repository, @"^[a-zA-Z0-9_-]+/[a-zA-Z0-9_.-]+$") ||
            !Regex.IsMatch(p.Destination, @"^[a-zA-Z0-9][a-zA-Z0-9/_.-]*$") ||
            p.Destination.Contains("..", StringComparison.Ordinal))
            throw new InvalidDataException("Invalid repository or branch.");
        if (p.MaxFiles is < 1 or > 10 || p.MaxChangedCharacters is < 1 or > 50000)
            throw new InvalidDataException("Policy limits outside supported range.");
        foreach (var path in p.ContextFiles.Concat(p.EditableFiles)) SafeRelative(path);
        foreach (var c in p.Checks)
        {
            if (!Regex.IsMatch(c.Image, @"^[a-zA-Z0-9./:_-]+@sha256:[a-f0-9]{64}$") ||
                c.Args.Length == 0 || c.TimeoutSeconds is < 1 or > 1800 ||
                !Regex.IsMatch(c.Network, @"^[a-zA-Z0-9_-]+$"))
                throw new InvalidDataException("Checks require digest-pinned images, arguments and bounded timeouts.");
        }
    }
    public static void SafeRelative(string path, bool allowProtected = false)
    {
        if (string.IsNullOrWhiteSpace(path) || path.StartsWith('/') || path.Contains('\\') ||
            path.Split('/').Any(x => x is "" or "." or "..") ||
            !Regex.IsMatch(path, @"^[a-zA-Z0-9_./-]+$"))
            throw new InvalidDataException("Unsafe relative path.");
        var lower = path.ToLowerInvariant();
        if (!allowProtected && (lower.Split('/').Any(x => x is ".git" or ".github" or ".azuredevops" or "node_modules") ||
            lower.EndsWith(".pem", StringComparison.Ordinal) || lower.EndsWith(".key", StringComparison.Ordinal) ||
            lower.Split('/').Any(x => x.StartsWith(".env", StringComparison.Ordinal))))
            throw new InvalidDataException("Protected path.");
    }
    public static string Resolve(string root, string relative, bool allowProtected = false)
    {
        SafeRelative(relative, allowProtected);
        var current = Path.GetFullPath(root);
        if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
            throw new InvalidDataException("Workspace cannot be a link.");
        foreach (var part in relative.Split('/'))
        {
            current = Path.Combine(current, part);
            if ((File.Exists(current) || Directory.Exists(current)) &&
                (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("Symlinks are not supported.");
        }
        return current;
    }
    public static void Eligible(Profile p, Finding finding, Proposal proposal)
    {
        Validate(p);
        if (proposal.Risk != Risk.Low || proposal.Uncertainties.Length != 0 ||
            string.IsNullOrWhiteSpace(proposal.Rationale))
            throw new InvalidDataException("Human review required: risk or uncertainty.");
        if (!p.LowRiskRules.Contains(finding.Source + ":" + finding.Rule, StringComparer.Ordinal))
            throw new InvalidDataException("Human review required: rule is not approved.");
        if (proposal.Edits.Length == 0 || proposal.Edits.Length > p.MaxFiles ||
            proposal.Edits.Select(x => x.Path).Distinct(StringComparer.Ordinal).Count() != proposal.Edits.Length)
            throw new InvalidDataException("Invalid edit count or duplicate path.");
        long size = 0;
        foreach (var edit in proposal.Edits)
        {
            SafeRelative(edit.Path);
            if (!p.EditableFiles.Contains(edit.Path, StringComparer.Ordinal) ||
                !p.ContextFiles.Contains(edit.Path, StringComparer.Ordinal) ||
                p.SensitivePaths.Any(x => edit.Path.StartsWith(x, StringComparison.OrdinalIgnoreCase)))
                throw new InvalidDataException("Human review required: path outside approved scope.");
            var name = Path.GetFileName(edit.Path).ToLowerInvariant();
            if (name.Contains("gradlew", StringComparison.Ordinal) || name.Contains("pipeline", StringComparison.Ordinal) ||
                name is "dockerfile" or "settings.gradle" or "settings.gradle.kts" ||
                name.Contains("lock", StringComparison.Ordinal) || name.EndsWith(".toml", StringComparison.Ordinal) ||
                name is "package.json" or "build.gradle" or "build.gradle.kts" or "gradle.properties")
                throw new InvalidDataException("Dependency/build configuration requires human review in v0.1.");
            if (string.IsNullOrEmpty(edit.OldText) || edit.OldText == edit.NewText ||
                edit.NewText.Contains('\0'))
                throw new InvalidDataException("Invalid replacement.");
            // Suppression is never an acceptable automatic remediation.
            foreach (var marker in new[] { "NOSONAR", "SuppressWarnings", "eslint-disable", "ts-ignore", "@Disabled", ".skip(" })
                if (edit.NewText.Contains(marker, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("Suppression or skipped tests are prohibited.");
            size += edit.OldText.Length + edit.NewText.Length;
        }
        if (size > p.MaxChangedCharacters) throw new InvalidDataException("Patch exceeds policy.");
    }
    public static string ReplaceOnce(string content, Edit edit)
    {
        var first = content.IndexOf(edit.OldText, StringComparison.Ordinal);
        if (first < 0 || content.IndexOf(edit.OldText, first + edit.OldText.Length, StringComparison.Ordinal) >= 0)
            throw new InvalidDataException("Replacement must match exactly once.");
        return content[..first] + edit.NewText + content[(first + edit.OldText.Length)..];
    }
    public static void CompareScans(Scan before, Scan after, Finding target, string tree, DateTimeOffset started)
    {
        if (before.Schema != 1 || after.Schema != 1 || !before.Complete || !after.Complete ||
            !after.PolicyGatePassed || after.TreeHash != tree || string.IsNullOrWhiteSpace(after.Scope) ||
            after.Scope != before.Scope || !after.Sources.Order().SequenceEqual(before.Sources.Order()) ||
            !after.Sources.Contains(target.Source) || after.CompletedAt < started ||
            after.CompletedAt > DateTimeOffset.UtcNow.AddMinutes(1))
            throw new InvalidDataException("Incomplete, stale, mismatched or failed scan.");
        static string Key(Finding f) => f.Source + ":" + f.Id;
        if (!before.Findings.Any(f => Key(f) == Key(target)) ||
            after.Findings.Any(f => Key(f) == Key(target)))
            throw new InvalidDataException("Target was not present before or remains after.");
        var oldIds = before.Findings.Select(Key).ToHashSet(StringComparer.Ordinal);
        if (after.Findings.Any(f => !oldIds.Contains(Key(f))))
            throw new InvalidDataException("New findings require human review.");
    }
}
