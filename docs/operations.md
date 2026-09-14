# Operations and recovery

## Exit behavior

0 means that the requested phase succeeded, not that the whole remediation succeeded.
1 means fail-closed validation/configuration/API failure.
130 means cancellation or timeout.

Model and remote error bodies are intentionally not printed. Inspect the protected plan and your scanner/build logs. Do not add raw request/response logging to diagnose secrets.

## Common failures

| Symptom | Action |
|---|---|
| Apply fails | Inspect plan risk/uncertainties, approved rules, paths and file limits; do not lower policy merely to pass |
| Baseline fails | Fix the environment or pre-existing tests before automation |
| Docker cannot write | Prepare workspace ownership for UID/GID 1000; do not run the container as root |
| Gradle/npm cannot resolve packages | Prepare pinned tools/caches or approved registry-only network |
| Scan mismatch | Check actual scan revision, complete pagination, profile/exclusions, source set and content hash |
| Candidate policy gate stays red | Escalate remaining findings; the agent does not waive them |
| Destination advanced | Recreate the plan/validation against its new commit |
| Signature rejected | Check key rotation, bundle provenance, policy hash and 24-hour expiration |
| Branch exists but PR absent | Inspect partial publication; no automatic overwrite is performed |

## Partial publication

The publisher first checks for an open PR on the deterministic branch. If one exists it returns its URL without changing it. Otherwise it creates a new branch, uploads the signed files and opens a PR. A crash can leave a branch/commit with no PR.

Inspect that branch and the originating signed bundle. An authorized operator may open a PR after verifying exact contents, or remove the unused branch and rerun publication. Never force-update a branch that might contain human changes.

Duplicate prevention is best-effort. The orchestrating pipeline must serialize jobs per repository. The publisher does not query closed PRs; a reused deterministic branch causes a safe failure rather than overwriting history.

## Evidence and monitoring

Retain build ID, reviewed agent commit, profile hash, native scanner report IDs, target base commit, plan, baseline/candidate scan, test reports, signed bundle and PR URL under private access control.

Track confirmed finding resolution, PR acceptance, regressions/reverts, escalations, API/model cost and time-to-remediate. Do not optimize for raw PR count.

No retry loop exists in v0.1. After a validation failure, inspect the evidence and start a new reviewed attempt. This prevents unconstrained repeated edits.
