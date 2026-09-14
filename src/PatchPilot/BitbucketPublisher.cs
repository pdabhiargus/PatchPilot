using System.Text;

namespace PatchPilot;

/// <summary>Publish only signed, policy-bound files. Never execute target code or merge a PR.</summary>
public static class BitbucketPublisher
{
    public static async Task<string> Publish(Profile profile, Envelope envelope, CancellationToken ct)
    {
        if (Environment.GetEnvironmentVariable("PP_PUBLISH") != "true")
            throw new InvalidOperationException("Publishing requires PP_PUBLISH=true.");
        var bundle = JsonFiles.Authenticate(envelope);
        if (bundle.Repository != profile.Repository || bundle.Destination != profile.Destination ||
            bundle.PolicyHash != JsonFiles.Hash(JsonFiles.Serialize(profile)) || bundle.Risk != Risk.Low ||
            bundle.Checks.Length == 0 || bundle.Checks.Any(x => x.ExitCode != 0) ||
            bundle.Files.Length is 0 || bundle.Files.Length > profile.MaxFiles)
            throw new InvalidDataException("Bundle does not match approved policy.");
        foreach (var file in bundle.Files)
        {
            Policy.SafeRelative(file.Path);
            if (!profile.EditableFiles.Contains(file.Path) || JsonFiles.Hash(file.Content) != file.Sha256)
                throw new InvalidDataException("Invalid publication file.");
        }
        using var client = HttpApi.Client("https://api.bitbucket.org/2.0/",
            JsonFiles.Secret("PP_BITBUCKET_TOKEN"), Environment.GetEnvironmentVariable("PP_BITBUCKET_USER"));
        var repo = "repositories/" + profile.Repository + "/";
        var key = JsonFiles.Hash(bundle.Repository + bundle.Finding.Source + bundle.Finding.Id + bundle.BaseCommit)[..20];
        var branch = "patchpilot/" + key;
        var query = Uri.EscapeDataString($"source.branch.name = \"{branch}\"");
        using var existingRequest = new HttpRequestMessage(HttpMethod.Get, repo + "pullrequests?state=OPEN&q=" + query);
        var existing = await HttpApi.Send(client, existingRequest, ct);
        if (existing.GetProperty("values").GetArrayLength() > 0)
            return existing.GetProperty("values")[0].GetProperty("links").GetProperty("html").GetProperty("href").GetString()!;
        using var destinationRequest = new HttpRequestMessage(HttpMethod.Get,
            repo + "refs/branches/" + Uri.EscapeDataString(profile.Destination));
        var destination = await HttpApi.Send(client, destinationRequest, ct);
        if (destination.GetProperty("target").GetProperty("hash").GetString() != bundle.BaseCommit)
            throw new InvalidDataException("Destination advanced; rerun remediation on its new head.");

        // Do not overwrite an existing branch after a partial failure. Operator inspects/resumes explicitly.
        using var createBranch = HttpApi.Post(repo + "refs/branches",
            new { name = branch, target = new { hash = bundle.BaseCommit } });
        await HttpApi.Send(client, createBranch, ct);
        using var form = new MultipartFormDataContent();
        form.Add(new StringContent(branch), "branch");
        form.Add(new StringContent(bundle.BaseCommit), "parents");
        form.Add(new StringContent("PatchPilot: remediate " + bundle.Finding.Rule), "message");
        foreach (var file in bundle.Files)
            form.Add(new ByteArrayContent(Encoding.UTF8.GetBytes(file.Content)), "/" + file.Path, Path.GetFileName(file.Path));
        using var commit = new HttpRequestMessage(HttpMethod.Post, repo + "src") { Content = form };
        var committed = await HttpApi.Send(client, commit, ct);
        var commitHash = committed.GetProperty("hash").GetString()!;
        using var headRequest = new HttpRequestMessage(HttpMethod.Get, repo + "refs/branches/" + Uri.EscapeDataString(branch));
        var head = await HttpApi.Send(client, headRequest, ct);
        if (head.GetProperty("target").GetProperty("hash").GetString() != commitHash)
            throw new InvalidDataException("Source branch moved during publication.");
        var description = $"""
Automated remediation; human review and independent PR validation are required.

Finding: {bundle.Finding.Source} / {bundle.Finding.Rule} / {bundle.Finding.Id}
Fix risk: {bundle.Risk}
Reasoning (model-generated, untrusted): {bundle.Rationale}
Base commit: {bundle.BaseCommit}
Candidate content hash: {bundle.CandidateTree}
Published commit: {commitHash}
Validation build: {bundle.BuildUrl}
Validated at: {bundle.ValidatedAt:O}
Checks passed: {bundle.Checks.Length}
Target absent on complete rescan; no new findings; policy gate passed.

Rollback: revert this PR's merge commit through your normal reviewed process.
PatchPilot has not merged or deployed this change.
""";
        using var pr = HttpApi.Post(repo + "pullrequests", new
        {
            title = ("PatchPilot: " + bundle.Finding.Rule)[..Math.Min(120, ("PatchPilot: " + bundle.Finding.Rule).Length)],
            description,
            source = new { branch = new { name = branch } },
            destination = new { branch = new { name = profile.Destination } },
            close_source_branch = true
        });
        var result = await HttpApi.Send(client, pr, ct);
        return result.GetProperty("links").GetProperty("html").GetProperty("href").GetString()!;
    }
}
