namespace PatchPilot;

// DTOs are deliberately plain records: artifacts are versioned JSON, not executable instructions.
public enum Risk { Low, Medium, High }
public sealed record Finding(string Id, string Source, string Rule, string Kind, string Severity,
    string Message, string[] Paths);
public sealed record Edit(string Path, string OldText, string NewText);
public sealed record Proposal(Risk Risk, string Rationale, string[] Uncertainties, Edit[] Edits);
public sealed record Plan(int Schema, string Repository, string BaseCommit, string BaseTree,
    Finding Finding, Proposal Proposal, string PolicyHash);
public sealed record Command(string Image, string[] Args, int TimeoutSeconds = 600,
    string Network = "none");
public sealed record Profile
{
    public string Repository { get; init; } = "";
    public string Destination { get; init; } = "main";
    public string[] ContextFiles { get; init; } = [];
    public string[] EditableFiles { get; init; } = [];
    public string[] LowRiskRules { get; init; } = [];
    public string[] SensitivePaths { get; init; } = [];
    public int MaxFiles { get; init; } = 3;
    public int MaxChangedCharacters { get; init; } = 8000;
    public Command[] Checks { get; init; } = [];
}
public sealed record Scan(int Schema, string TreeHash, string Scope, string[] Sources,
    bool Complete, bool PolicyGatePassed, DateTimeOffset CompletedAt, Finding[] Findings);
public sealed record CheckResult(string Image, string[] Args, int ExitCode);
public sealed record Baseline(string Repository, string BaseCommit, string TreeHash, string PolicyHash,
    Scan Scan, CheckResult[] Checks);
public sealed record PublishedFile(string Path, string Content, string Sha256);
public sealed record Bundle(int Schema, string Repository, string Destination, string BaseCommit,
    string CandidateTree, string PolicyHash, Finding Finding, Risk Risk, string Rationale,
    DateTimeOffset ValidatedAt, string BuildUrl, CheckResult[] Checks, PublishedFile[] Files);
public sealed record Envelope(string Payload, string Signature);
