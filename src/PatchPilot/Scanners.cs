using System.Text.Json;

namespace PatchPilot;

/// <summary>Read-only adapters. Findings do not assert that a scan corresponds to a particular checkout.</summary>
public static class Scanners
{
    public static async Task<Finding[]> Sonar(CancellationToken ct)
    {
        var baseUrl = JsonFiles.Secret("PP_SONAR_URL").TrimEnd('/') + "/";
        using var client = HttpApi.Client(baseUrl, JsonFiles.Secret("PP_SONAR_TOKEN"));
        var project = Uri.EscapeDataString(JsonFiles.Secret("PP_SONAR_PROJECT"));
        var branch = Uri.EscapeDataString(JsonFiles.Secret("PP_SONAR_BRANCH"));
        var findings = new List<Finding>();
        for (var page = 1; page <= 100; page++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get,
                $"api/issues/search?componentKeys={project}&branch={branch}&resolved=false&ps=100&p={page}");
            var data = await HttpApi.Send(client, request, ct);
            foreach (var item in data.GetProperty("issues").EnumerateArray())
            {
                var component = item.GetProperty("component").GetString()!;
                // Sonar component keys prefix paths with project/module keys. Operators must inspect multi-module mappings.
                var path = component[(component.LastIndexOf(':') + 1)..];
                findings.Add(new(item.GetProperty("key").GetString()!, "sonarqube",
                    item.GetProperty("rule").GetString()!, Text(item, "type", "UNKNOWN"),
                    Text(item, "severity", "UNKNOWN"), Text(item, "message", ""), [path]));
            }
            var total = data.GetProperty("paging").GetProperty("total").GetInt32();
            if (total > 10000) throw new InvalidDataException("Sonar result limit exceeded; narrow project scope.");
            if (page * 100 >= total) return findings.ToArray();
        }
        throw new InvalidDataException("Incomplete Sonar pagination.");
    }
    public static async Task<Finding[]> Sonatype(Profile profile, CancellationToken ct)
    {
        // Supply an immutable raw report URL obtained from the IQ Reports API, never latestReport.
        using var client = HttpApi.Client(JsonFiles.Secret("PP_SONATYPE_REPORT_URL"),
            JsonFiles.Secret("PP_SONATYPE_TOKEN"), JsonFiles.Secret("PP_SONATYPE_USER"));
        using var request = new HttpRequestMessage(HttpMethod.Get, "");
        return FromSonatype(await HttpApi.Send(client, request, ct), profile.ContextFiles);
    }
    public static Finding[] FromSonatype(JsonElement data, string[] paths)
    {
        var result = new List<Finding>();
        foreach (var component in data.GetProperty("components").EnumerateArray())
        {
            if (!component.TryGetProperty("securityData", out var security) ||
                !security.TryGetProperty("securityIssues", out var issues)) continue;
            var identifier = component.GetProperty("componentIdentifier");
            var coordinates = identifier.GetProperty("coordinates");
            // Excluding version gives vulnerability identity continuity across upgrades.
            var identity = string.Join(":", coordinates.EnumerateObject()
                .Where(x => x.Name != "version").OrderBy(x => x.Name).Select(x => x.Name + "=" + x.Value.ToString()));
            foreach (var issue in issues.EnumerateArray())
            {
                var reference = issue.GetProperty("reference").GetString()!;
                result.Add(new(JsonFiles.Hash(identity + ":" + reference), "sonatype", reference,
                    "DEPENDENCY", Text(issue, "severity", "UNKNOWN"),
                    "Dependency " + identifier.GetRawText() + ": " + reference, paths));
            }
        }
        return result.DistinctBy(x => x.Id).ToArray();
    }
    public static Finding[] Sarif(JsonElement data)
    {
        var findings = new List<Finding>();
        foreach (var run in data.GetProperty("runs").EnumerateArray())
        {
            var source = run.GetProperty("tool").GetProperty("driver").GetProperty("name").GetString()!;
            if (!run.TryGetProperty("results", out var results)) continue;
            foreach (var item in results.EnumerateArray())
            {
                var rule = Text(item, "ruleId", "unknown");
                var message = item.GetProperty("message").GetProperty("text").GetString()!;
                var paths = item.TryGetProperty("locations", out var locations)
                    ? locations.EnumerateArray().Select(x => x.GetProperty("physicalLocation")
                        .GetProperty("artifactLocation").GetProperty("uri").GetString()!).Distinct().ToArray() : [];
                // Group same-rule/message/path occurrences conservatively; all occurrences must disappear.
                var id = JsonFiles.Hash(source + rule + message + string.Join("|", paths.Order()));
                findings.Add(new(id, source, rule, "STATIC_ANALYSIS", Text(item, "level", "warning"), message, paths));
            }
        }
        return findings.DistinctBy(x => x.Source + ":" + x.Id).ToArray();
    }
    private static string Text(JsonElement item, string property, string fallback) =>
        item.TryGetProperty(property, out var value) ? value.ToString() : fallback;
}
