using PatchPilot;
using System.Text.Json;

// Positional CLI deliberately keeps secrets out of command-line arguments.
// All artifact paths must be outside the target checkout.
using var cancellation = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cancellation.Cancel(); };
try
{
    await App.Execute(args, cancellation.Token);
    return 0;
}
catch (OperationCanceledException)
{
    Console.Error.WriteLine("PatchPilot cancelled or timed out.");
    return 130;
}
catch (Exception ex)
{
    // Avoid raw exception messages: HTTP, model and repository text may contain credentials.
    Console.Error.WriteLine("PatchPilot failed closed (" + ex.GetType().Name +
        "). Check configuration, policy, scan provenance, and isolated validation results. No PR was approved by this failure.");
    return 1;
}

public static class App
{
    public static async Task Execute(string[] args, CancellationToken ct)
    {
        if (args.Length == 0 || args[0] == "help")
        {
            Console.WriteLine("""
PatchPilot v0.1 (.NET 10, Linux runner)
  collect-sonar OUTPUT
  collect-sonatype PROFILE OUTPUT
  import-sarif INPUT OUTPUT
  hash WORKSPACE
  plan PROFILE WORKSPACE FINDINGS FINDING_ID OUTPUT
  baseline PROFILE WORKSPACE SCAN OUTPUT
  apply PROFILE WORKSPACE PLAN
  verify PROFILE WORKSPACE PLAN BASELINE SCAN OUTPUT
  publish PROFILE BUNDLE
Read README.md and docs/security.md before enabling edits or publication.
""");
            return;
        }
        var command = args[0];
        var count = command switch
        {
            "collect-sonar" or "hash" => 2,
            "collect-sonatype" or "import-sarif" or "publish" => 3,
            "apply" => 4, "baseline" => 5, "plan" => 6, "verify" => 7,
            _ => throw new ArgumentException("Unknown command.")
        };
        if (args.Length != count) throw new ArgumentException("Wrong number of arguments.");
        if (command == "collect-sonar")
        {
            JsonFiles.Write(args[1], await Scanners.Sonar(ct)); return;
        }
        if (command == "import-sarif")
        {
            using var data = JsonDocument.Parse(File.ReadAllText(args[1]));
            JsonFiles.Write(args[2], Scanners.Sarif(data.RootElement)); return;
        }
        if (command == "hash") { Console.WriteLine(await Workspace.Snapshot(args[1], ct)); return; }
        var profile = JsonFiles.Read<Profile>(args[1]);
        Policy.Validate(profile);
        if (command == "collect-sonatype")
        {
            JsonFiles.Write(args[2], await Scanners.Sonatype(profile, ct)); return;
        }
        if (command == "publish")
        {
            Console.WriteLine(await BitbucketPublisher.Publish(profile, JsonFiles.Read<Envelope>(args[2]), ct)); return;
        }
        var root = Path.GetFullPath(args[2]);
        // Profile and artifacts live in a trusted directory that is not mounted in the build container.
        Outside(root, args[1]);
        foreach (var path in command switch
        {
            "plan" => new[] { args[3], args[5] },
            "baseline" => new[] { args[3], args[4] },
            "apply" => new[] { args[3] },
            "verify" => new[] { args[3], args[4], args[5], args[6] },
            _ => Array.Empty<string>()
        }) Outside(root, path);
        var policyHash = JsonFiles.Hash(JsonFiles.Serialize(profile));
        if (command == "plan")
        {
            await Workspace.Clean(root, ct);
            var finding = JsonFiles.Read<Finding[]>(args[3]).Single(f => f.Id == args[4]);
            var baseCommit = await Workspace.Commit(root, ct);
            var tree = await Workspace.Snapshot(root, ct);
            var proposal = await AiPlanner.Propose(profile, finding, root, ct);
            JsonFiles.Write(args[5], new Plan(1, profile.Repository, baseCommit, tree, finding, proposal, policyHash));
            Console.WriteLine("Proposal saved. Risk: " + proposal.Risk + ". No source files changed.");
            return;
        }
        if (command == "baseline")
        {
            await Workspace.Clean(root, ct);
            var tree = await Workspace.Snapshot(root, ct);
            var scan = JsonFiles.Read<Scan>(args[3]);
            if (scan.Schema != 1 || !scan.Complete || scan.TreeHash != tree ||
                scan.CompletedAt < DateTimeOffset.UtcNow.AddHours(-24) ||
                scan.CompletedAt > DateTimeOffset.UtcNow.AddMinutes(1))
                throw new InvalidDataException("Baseline scan is incomplete, stale or mismatched.");
            var checks = await Processes.Checks(profile, root, ct);
            if (await Workspace.Snapshot(root, ct) != tree) throw new InvalidDataException("Baseline checks modified source.");
            await Workspace.Clean(root, ct);
            JsonFiles.Write(args[4], new Baseline(profile.Repository, await Workspace.Commit(root, ct),
                tree, policyHash, scan, checks));
            return;
        }
        var plan = JsonFiles.Read<Plan>(args[3]);
        if (plan.Schema != 1) throw new InvalidDataException("Unsupported plan.");
        if (command == "apply")
        {
            if (Environment.GetEnvironmentVariable("PP_APPLY") != "true")
                throw new InvalidOperationException("Applying requires PP_APPLY=true.");
            await Workspace.Apply(root, profile, plan, ct);
            Console.WriteLine("Eligible changes applied to the disposable checkout. Not published.");
            return;
        }
        var baseline = JsonFiles.Read<Baseline>(args[4]);
        Policy.Eligible(profile, plan.Finding, plan.Proposal);
        if (baseline.Repository != profile.Repository || plan.Repository != profile.Repository ||
            baseline.PolicyHash != policyHash || plan.PolicyHash != policyHash ||
            baseline.BaseCommit != plan.BaseCommit || baseline.TreeHash != plan.BaseTree ||
            baseline.Scan.TreeHash != plan.BaseTree || baseline.Checks.Length != profile.Checks.Length ||
            baseline.Checks.Any(x => x.ExitCode != 0) ||
            await Workspace.Commit(root, ct) != plan.BaseCommit)
            throw new InvalidDataException("Baseline, plan and checkout mismatch.");
        var expected = plan.Proposal.Edits.Select(x => x.Path).Order(StringComparer.Ordinal).ToArray();
        if (!(await Workspace.Changed(root, ct)).SequenceEqual(expected))
            throw new InvalidDataException("Unexpected changed files.");
        foreach (var edit in plan.Proposal.Edits)
        {
            var original = await Processes.Git(root, ct, "show", "HEAD:" + edit.Path);
            if (Workspace.ReadText(Policy.Resolve(root, edit.Path)) != Policy.ReplaceOnce(original, edit))
                throw new InvalidDataException("Candidate differs from approved proposal.");
        }
        var candidate = await Workspace.Snapshot(root, ct);
        var results = await Processes.Checks(profile, root, ct);
        if (await Workspace.Snapshot(root, ct) != candidate ||
            !(await Workspace.Changed(root, ct)).SequenceEqual(expected))
            throw new InvalidDataException("Validation modified source.");
        var after = JsonFiles.Read<Scan>(args[5]);
        Policy.CompareScans(baseline.Scan, after, plan.Finding, candidate, baseline.Scan.CompletedAt.AddTicks(1));
        var files = expected.Select(path =>
        {
            var content = Workspace.ReadText(Policy.Resolve(root, path));
            return new PublishedFile(path, content, JsonFiles.Hash(content));
        }).ToArray();
        var bundle = new Bundle(1, profile.Repository, profile.Destination, plan.BaseCommit, candidate,
            policyHash, plan.Finding, plan.Proposal.Risk, plan.Proposal.Rationale, DateTimeOffset.UtcNow,
            Environment.GetEnvironmentVariable("PP_BUILD_URL") ?? "not supplied", results, files);
        JsonFiles.Write(args[6], JsonFiles.Sign(bundle));
        Console.WriteLine("Signed validation bundle created. Publishing is a separate command.");
    }
    private static void Outside(string root, string path)
    {
        var full = Path.GetFullPath(path);
        if (full == root || full.StartsWith(root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar,
            StringComparison.Ordinal))
            throw new InvalidDataException("Policy and artifacts must be outside target checkout.");
    }
}
