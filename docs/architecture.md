# Architecture and code guide

## Deployment

PatchPilot is a short-lived process in Azure DevOps. Hosting an API is unnecessary for the pilot. Each job gets a fresh target checkout. Control files and artifacts are stored outside it. A separate publisher job has Bitbucket write credentials and does not execute application code.

GitHub hosts the agent source. Azure DevOps builds a pinned reviewed agent revision. Bitbucket hosts the target React and Java applications. The agent does not copy those private repositories to GitHub.

## Modules

| File | Responsibility and boundary |
|---|---|
| Program.cs | CLI phases, artifact placement, baseline/candidate checks and signed bundle creation |
| Models.cs | Versioned JSON records for policy, findings, plans, scans and publication |
| JsonFiles.cs | Strict JSON, bounded artifact reads, atomic writes, SHA-256 and HMAC verification |
| Policy.cs | Deterministic eligibility, safe paths, ambiguity checks and scan comparison |
| Workspace.cs | Clean Git checkout, content snapshot, exact replacements and changed-file checks |
| Processes.cs | Argument-list Git execution and restricted Docker validation |
| HttpApi.cs | HTTPS-only bounded clients; redirects disabled; response bodies excluded from errors |
| AiPlanner.cs | Selected context upload and one JSON-only model proposal |
| Scanners.cs | SonarQube, IQ raw report and SARIF finding adapters |
| BitbucketPublisher.cs | Signature/policy verification, destination head check, branch/file upload and PR creation |

There is no hidden autonomous shell tool. The model returns one replacement per existing file. All commands originate in the protected profile.

## State and identity

States are explicit artifacts: findings -> plan -> baseline -> modified workspace -> candidate scan -> signed bundle -> PR. Baseline can be generated before the plan. A failure yields exit code 1; cancellation yields 130. No later phase should run after a failure.

The plan binds repository, base commit, full tracked-content hash and serialized policy hash. Verification confirms the edits match the plan and the tests did not modify source. The bundle includes exact published bytes, content hashes, check identities, timestamp and build URL.

The HMAC key is held only by trusted verification/publishing hosts. It establishes that the bundle came from an authorized verifier; it does not prove the scanner or tests were configured correctly. Authentication of the originating pipeline remains essential.

## Boundaries and future extensions

v0.1 supports existing UTF-8 files only. Binary files, BOMs in edited files, symlinks, broad context discovery, parallel fixes and multi-repository transactions are out of scope. Context files are capped at 100 KB each and 250,000 characters collectively.

The current dependency policy routes manifests/lockfiles/build files to review. A future dependency executor should use the actual package manager, regenerate lockfiles, inspect resolved versions and introduce compatibility-specific gates rather than letting the model invent a lockfile.

Future work: tool-driven context expansion, bounded repair retries, model-provider adapters, native scan initiation/polling, durable job coordination, richer test evidence and coordinated API-contract testing. These are not represented as completed features.
