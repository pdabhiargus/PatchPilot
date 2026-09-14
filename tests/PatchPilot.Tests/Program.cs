using PatchPilot;
using System.Text.Json;

// Dependency-free executable regression suite. A nonzero exit fails CI.
var tests = new List<(string Name, Action Run)>();
void Test(string name, Action run) => tests.Add((name, run));
void Reject(Action run)
{
    try { run(); } catch (InvalidDataException) { return; }
    throw new Exception("Expected rejection.");
}
var finding = new Finding("issue-1", "sonarqube", "java:S2095", "BUG", "MAJOR", "Close resource.", ["src/A.java"]);
var profile = new Profile
{
    Repository = "team/api", ContextFiles = ["src/A.java"], EditableFiles = ["src/A.java"],
    LowRiskRules = ["sonarqube:java:S2095"]
};
var proposal = new Proposal(Risk.Low, "Bounded replacement with relevant tests.", [],
    [new Edit("src/A.java", "before", "after")]);
Test("Explicitly approved source edit passes", () => Policy.Eligible(profile, finding, proposal));
Test("Unknown rule fails closed", () => Reject(() => Policy.Eligible(profile with { LowRiskRules = [] }, finding, proposal)));
Test("Model risk cannot override policy", () => Reject(() => Policy.Eligible(profile, finding, proposal with { Risk = Risk.High })));
Test("Uncertainty blocks automation", () => Reject(() => Policy.Eligible(profile, finding, proposal with { Uncertainties = ["unknown"] })));
Test("Sensitive path blocks automation", () => Reject(() => Policy.Eligible(profile with { SensitivePaths = ["src/"] }, finding, proposal)));
Test("Traversal blocked", () => Reject(() => Policy.SafeRelative("../secrets")));
Test("Absolute path blocked", () => Reject(() => Policy.SafeRelative("/etc/passwd")));
Test("Windows traversal blocked", () => Reject(() => Policy.SafeRelative("src\\..\\secret")));
Test("Pipeline path protected", () => Reject(() => Policy.SafeRelative(".github/workflows/ci.yml")));
Test("Secret file protected", () => Reject(() => Policy.SafeRelative(".env.production")));
Test("Duplicate edits blocked", () => Reject(() => Policy.Eligible(profile, finding, proposal with { Edits = [proposal.Edits[0], proposal.Edits[0]] })));
Test("Suppression blocked", () => Reject(() => Policy.Eligible(profile, finding, proposal with { Edits = [new("src/A.java", "before", "// NOSONAR")] })));
Test("Ambiguous replacement blocked", () => Reject(() => Policy.ReplaceOnce("before before", proposal.Edits[0])));
Test("Absent replacement blocked", () => Reject(() => Policy.ReplaceOnce("different", proposal.Edits[0])));
Test("Exact replacement preserves surroundings", () =>
{
    if (Policy.ReplaceOnce("x before y", proposal.Edits[0]) != "x after y") throw new Exception("Wrong replacement.");
});
var now = DateTimeOffset.UtcNow;
var before = new Scan(1, "base", "application:branch:rules-v1", ["sonarqube"], true, false, now.AddMinutes(-5), [finding]);
var after = new Scan(1, "candidate", before.Scope, before.Sources, true, true, now, []);
Test("Resolved target passes complete matching scan", () => Policy.CompareScans(before, after, finding, "candidate", now.AddMinutes(-1)));
Test("Stale scan blocked", () => Reject(() => Policy.CompareScans(before, after with { CompletedAt = now.AddHours(-1) }, finding, "candidate", now.AddMinutes(-1))));
Test("Wrong content scan blocked", () => Reject(() => Policy.CompareScans(before, after, finding, "wrong", now.AddMinutes(-1))));
Test("Unresolved finding blocked", () => Reject(() => Policy.CompareScans(before, after with { Findings = [finding] }, finding, "candidate", now.AddMinutes(-1))));
Test("New finding blocked", () => Reject(() => Policy.CompareScans(before, after with { Findings = [finding with { Id = "new" }] }, finding, "candidate", now.AddMinutes(-1))));
Test("Incomplete scan blocked", () => Reject(() => Policy.CompareScans(before, after with { Complete = false }, finding, "candidate", now.AddMinutes(-1))));
Test("Changed scan scope blocked", () => Reject(() => Policy.CompareScans(before, after with { Scope = "reduced" }, finding, "candidate", now.AddMinutes(-1))));
Test("Changed scanner set blocked", () => Reject(() => Policy.CompareScans(before, after with { Sources = [] }, finding, "candidate", now.AddMinutes(-1))));
Test("Mutable image tag blocked", () => Reject(() => Policy.Validate(profile with { Checks = [new("node:22", ["npm", "test"])] })));
Test("Unknown JSON field rejected", () =>
{
    try { JsonFiles.Parse<Edit>("""{"path":"a","oldText":"b","newText":"c","execute":"bad"}"""); }
    catch (JsonException) { return; }
    throw new Exception("Extra property accepted.");
});
Test("Signed artifact roundtrip and tamper detection", () =>
{
    Environment.SetEnvironmentVariable("PP_ATTESTATION_KEY", Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32)));
    var bundle = new Bundle(1, "team/api", "main", "abc", "tree", "policy", finding, Risk.Low,
        "reason", DateTimeOffset.UtcNow, "build", [], []);
    var signed = JsonFiles.Sign(bundle);
    if (JsonFiles.Authenticate(signed).Repository != "team/api") throw new Exception("Roundtrip failed.");
    Reject(() => JsonFiles.Authenticate(signed with { Payload = signed.Payload + " " }));
    Reject(() => JsonFiles.Authenticate(JsonFiles.Sign(bundle with { ValidatedAt = now.AddDays(-2) })));
});
Test("IQ vulnerability identity survives version change", () =>
{
    var raw = """{"components":[{"componentIdentifier":{"format":"maven","coordinates":{"groupId":"a","artifactId":"b","version":"1"}},"securityData":{"securityIssues":[{"reference":"CVE-EXAMPLE","severity":7}]}}]}""";
    using var first = JsonDocument.Parse(raw);
    using var second = JsonDocument.Parse(raw.Replace("\"version\":\"1\"", "\"version\":\"2\""));
    if (Scanners.FromSonatype(first.RootElement, [])[0].Id != Scanners.FromSonatype(second.RootElement, [])[0].Id)
        throw new Exception("Identity changed across versions.");
});
Test("Symlink workspace path blocked", () =>
{
    if (!OperatingSystem.IsLinux()) return;
    var temp = Path.Combine(Path.GetTempPath(), "patchpilot-test-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(temp);
    try
    {
        Directory.CreateSymbolicLink(Path.Combine(temp, "escape"), "/tmp");
        Reject(() => Policy.Resolve(temp, "escape/file"));
    }
    finally { Directory.Delete(temp, true); }
});
var failures = 0;
foreach (var test in tests)
{
    try { test.Run(); Console.WriteLine("PASS " + test.Name); }
    catch (Exception ex) { failures++; Console.Error.WriteLine("FAIL " + test.Name + ": " + ex.GetType().Name); }
}
Console.WriteLine($"{tests.Count - failures}/{tests.Count} passed.");
return failures == 0 ? 0 : 1;
