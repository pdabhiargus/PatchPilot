# Threat model and security controls

## Assets and trust boundaries

Assets: private source, AI/scanner/Bitbucket credentials, attestation keys, build runners, repository integrity, and confidence in validation.

Trusted: reviewed PatchPilot binaries and profiles, protected pipeline definitions, digest-pinned validation images, secret store, authenticated scanner integration and publisher.

Untrusted: model output, source code/comments, scanner messages, package installation/build scripts, dependency contents and arbitrary PR artifacts.

## Controls implemented

- HTTPS-only clients; redirects disabled; bounded responses and request timeouts.
- No secrets in CLI arguments, model context or printed remote error bodies.
- Explicit source-upload opt-in; context is exact files, not the whole repository.
- No model-generated host commands; ProcessStartInfo.ArgumentList prevents shell interpolation.
- Allowlisted edits, protected paths, no traversal or symlinks, exact unique replacements.
- Explicit Low-risk rule approval and uncertainty rejection; size limits; common suppression markers rejected.
- Baseline/candidate content binding; checks may not change source; scan scope/source/freshness checks.
- Docker restrictions: non-root, read-only root, no added capabilities, no privilege escalation, PID/memory/CPU limits, no network by default, read-only Git metadata.
- Child environment cleared except required runtime values; no agent-token inheritance.
- Signed publication bundle, per-file hashes, policy hash and 24-hour expiry.
- Bitbucket write credential only in separate publisher; no force push, automatic merge or deployment.
- Immutable base commit check and deterministic remediation branch; no blind HTTP write retries.

## Controls the deployment MUST supply

- Separate protected publishing job and trusted artifact provenance.
- No execution of target code on the signing/publishing host. Container isolation assumes a patched kernel/runtime; use stronger VM isolation if your threat model requires it.
- CI templates and profiles must be outside untrusted branches and the application mount.
- Restrict Docker host access, host PATH/HOME, DNS and outbound network. Do not point DOCKER_HOST to an untrusted daemon.
- Remove checkout credentials from Git configuration. Do not persist credentials in the target mount.
- Pin and review validation images; prevent container root access and socket mounts.
- Audit source contents before approving upload. PatchPilot is not a secret-scanning/DLP product.
- Trusted scanner evidence: schema fields and a hash cannot independently prove that the scan happened.
- Required PR checks/reviewers and serialized repository remediation.
- Appropriate private artifact retention, access control and key rotation.

## Important limitations

Suppression-marker detection is a heuristic, not a semantic proof. Human review and narrowly approved rules are mandatory. Model-generated text can still introduce a logically incorrect fix; tests and scanners only cover what they check.

Artifacts contain source and model explanations. Protect them like repository contents. HMAC prevents unauthorized bundle modification but does not provide non-repudiation or replace pipeline authentication.

The content snapshot hashes tracked file bytes and paths, not Git metadata or ignored dependency caches. Approved images and dependency integrity checks must establish the environment. Source file additions, deletions, renames and symlinks are deliberately unsupported.

HTTP requests have no automatic retry. A write timeout may leave a branch or commit behind; inspect it before recovery. Publication checks the destination head immediately before writing but cannot create an atomic cross-request transaction with a concurrently advancing destination. Independent PR validation remains required.

Do not connect production repositories until the pilot acceptance checks and organization security review are complete.
