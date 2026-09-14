# PatchPilot

An AI-assisted agent that assesses, fixes, tests, and opens pull requests for security and code-quality findings.

PatchPilot is a **.NET 10 CLI that runs on demand in Azure DevOps**. Application repositories stay in **Bitbucket Cloud**; this GitHub repository contains the agent. React and Java/Gradle validation run in separate approved container images.

## Status and scope

This is an initial implementation for controlled pilot use, not a production-certified security product. There is no always-on service, database, dashboard, automatic merge, or deployment.

| Capability | v0.1 behavior |
|---|---|
| Source vulnerabilities, bugs and code smells | Propose edits to explicitly selected files; apply only approved low-risk rules and paths |
| SonarQube | Read unresolved issues with pagination; optional integration, not an assumption about your installed scanner |
| Sonatype IQ | Read vulnerabilities from an immutable raw component report; dependency changes are proposed for human review |
| Other scanners | Import SARIF findings or supply normalized findings JSON |
| Dependency/build/lockfile edits | Deliberately blocked from automatic application in this release; plans remain reviewable |
| Validation | Baseline and candidate container checks plus complete matching scanner evidence |
| Bitbucket Cloud | Upload signed validated file contents to a new branch and open a PR |
| Azure DevOps | Agent-build pipeline and remediation/publishing step templates |
| Risk | Model recommendation constrained by deterministic policy; only Low can auto-apply |
| Retries | One proposal per invocation; no autonomous repair retry loop yet |
| New files/deletions/renames | Not supported; edit existing UTF-8 files only |

Source-code fixes are not restricted to dependencies. High-impact authorization changes, framework migrations, broad refactors and uncertain fixes require human handling. Rule approval must be narrow and repository-specific: a scanner's severity does not establish implementation risk.

## Build and test

Prerequisites: .NET 10 SDK. Linux, Git and Docker are additionally required for application remediation. No third-party NuGet packages are needed.

```bash
dotnet build tests/PatchPilot.Tests/PatchPilot.Tests.csproj -c Release
dotnet run --project tests/PatchPilot.Tests -c Release --no-build
dotnet run --project src/PatchPilot -c Release --no-build -- help
dotnet publish src/PatchPilot -c Release -o artifacts/agent
```

The regression suite is an executable test harness; use the command above, **not** `dotnet test`. GitHub CI runs the build and suite automatically. Live Sonatype/Bitbucket/model tests require your private environment and are not part of this CI.

## How the workflow works

1. A trusted pipeline scans a clean target checkout and supplies normalized findings and baseline scan evidence.
2. PatchPilot runs baseline checks using a digest-pinned container image.
3. The model receives only the explicitly configured context files and one finding; it produces exact text replacements and a risk assessment.
4. The policy checks rule approval, paths, uncertainty, size, protected files and prohibited suppressions.
5. Eligible changes are applied to the disposable checkout.
6. Your trusted scanner integration scans the changed content and waits for the real evaluation to complete.
7. PatchPilot reruns checks, verifies content and scan evidence, and signs a publication bundle.
8. A fresh publisher job authenticates the bundle, checks the destination has not advanced, uploads the files, and creates a Bitbucket PR.
9. Independent PR validation and human review decide whether to merge.

The publisher never runs the target application's code. Build containers receive neither AI nor Bitbucket credentials.

## Configure a repository

Copy a profile from `profiles/` into a **protected control location outside the application checkout**.

- Replace the repository slug and destination.
- Select the exact source and existing test files needed for this finding. There is no unrestricted whole-repository upload.
- Add narrowly reviewed `source:rule` entries to `lowRiskRules`. The examples leave this empty, so automatic edits are blocked.
- Set sensitive path prefixes for authentication, authorization, payments and other critical areas.
- Replace placeholder image digests with approved validation images containing the correct JDK/Node, Gradle distribution and dependency caches.
- Adapt commands to the project's actual tests. React's `--run` example assumes a compatible test runner; it is not universal.
- Include meaningful security regression/integration tests in the configured checks. Passing a build alone is insufficient.
- Read [configuration](docs/configuration.md), [security](docs/security.md) and [Azure setup](docs/azure-devops.md).

## Credentials and environment variables

`.env.example` documents names only. PatchPilot does **not** load dotenv files. Inject secrets through protected Azure DevOps variables or your approved secret store; never pass secrets as CLI arguments.

| Variables | Used by |
|---|---|
| `PP_AI_ENDPOINT`, `PP_AI_MODEL`, `PP_AI_TOKEN` | `plan` |
| `PP_ALLOW_SOURCE_UPLOAD=true` | Explicitly enables sending configured private source to the approved model endpoint |
| `PP_SONAR_URL`, `PP_SONAR_PROJECT`, `PP_SONAR_BRANCH`, `PP_SONAR_TOKEN` | `collect-sonar` |
| `PP_SONATYPE_REPORT_URL`, `PP_SONATYPE_USER`, `PP_SONATYPE_TOKEN` | `collect-sonatype` |
| `PP_APPLY=true` | `apply` |
| `PP_ATTESTATION_KEY` | `verify` and `publish` only; base64 of at least 32 random bytes |
| `PP_BITBUCKET_TOKEN`, optional `PP_BITBUCKET_USER` | `publish` only |
| `PP_PUBLISH=true` | Explicitly enables publication |
| `PP_BUILD_URL` | Traceability link included in the PR |

The AI endpoint must implement a chat-completions-compatible request/response contract. It is a complete HTTPS URL; provider-specific authentication or response formats require an adapter. The current client uses Bearer authentication. Tokens are never supplied to the model.

## CLI walkthrough

Assume the trusted binary is `/opt/patchpilot/PatchPilot.dll`, the policy is `/control/profile.json`, the fresh target clone is `/work/target`, and artifacts are `/control/job`.

```bash
# Choose one source, or combine normalized findings in the trusted scan integration.
dotnet /opt/patchpilot/PatchPilot.dll collect-sonar /control/job/findings.json
dotnet /opt/patchpilot/PatchPilot.dll collect-sonatype /control/profile.json /control/job/findings.json
dotnet /opt/patchpilot/PatchPilot.dll import-sarif /control/job/results.sarif /control/job/findings.json

# Run after the trusted integration creates baseline.scan.json.
dotnet /opt/patchpilot/PatchPilot.dll baseline /control/profile.json /work/target /control/job/baseline.scan.json /control/job/baseline.json
dotnet /opt/patchpilot/PatchPilot.dll plan /control/profile.json /work/target /control/job/findings.json ISSUE_ID /control/job/plan.json
dotnet /opt/patchpilot/PatchPilot.dll apply /control/profile.json /work/target /control/job/plan.json

# HERE: run the real scanner on the candidate and create candidate.scan.json.
dotnet /opt/patchpilot/PatchPilot.dll verify /control/profile.json /work/target /control/job/plan.json /control/job/baseline.json /control/job/candidate.scan.json /control/job/bundle.json

# Separate fresh protected publisher job:
dotnet /opt/patchpilot/PatchPilot.dll publish /control/profile.json /control/job/bundle.json
```

Commands above are separate phases, not an unattended script. `collect-*` retrieves findings; it does not initiate a scan or establish scan provenance. **Never construct a successful scan by assuming disappearance from a stale report.** See the [scanner contract](docs/scanners.md).

## Documentation

- [Architecture and module guide](docs/architecture.md)
- [Configuration and risk policy](docs/configuration.md)
- [Scanner normalization and verification](docs/scanners.md)
- [Azure DevOps deployment](docs/azure-devops.md)
- [Threat model and security controls](docs/security.md)
- [Operations and failure recovery](docs/operations.md)
- [Contributing](CONTRIBUTING.md)
- [Security reporting](SECURITY.md)

## Initial onboarding checklist

- Run CI and review the implementation before granting production credentials.
- Validate API contracts against your SonarQube/IQ versions and Bitbucket token type.
- Connect one non-production repository with known reproducible tests.
- Keep publishing disabled until dry-run plans and scanner evidence are reviewed.
- Restrict pipeline editors, secret access and allowed target repositories.
- Configure required PR validation and reviewers in Bitbucket.
- Enable low-risk rules gradually; measure confirmed resolution and regressions.

No license grant is included. Choose an appropriate license before distributing the project.
