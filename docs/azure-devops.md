# Azure DevOps deployment

## 1. Build the agent from GitHub

Create an Azure DevOps pipeline connected to this private GitHub repository and select pipelines/azure-build-agent.yml. The build compiles PatchPilot, runs security regression tests and publishes the patchpilot artifact.

Use a reviewed commit/build ID. Do not download "latest successful" from an arbitrary branch for privileged remediation. Restrict who can edit the agent, templates, control profiles and validation images.

## 2. Connect the existing Bitbucket pipeline

Your application remains the pipeline's self repository in Bitbucket Cloud. Use its existing authenticated checkout/service connection with persisted credentials disabled. Do not depend on unsupported cross-provider multi-repository checkout assumptions.

Download the trusted PatchPilot artifact using Azure DevOps DownloadPipelineArtifact@2 with a specific approved pipeline/build ID. Download the reviewed profile and template from a protected control source. The example step templates must be imported/copied into that trusted pipeline definition; they are not autonomous pipelines.

Prepare:
- a fresh application clone with a clean tracked/untracked status;
- an artifacts/control directory outside the application clone;
- the exact target finding ID and normalized findings.json;
- complete baseline.scan.json from the scan that actually evaluated this checkout;
- .NET 10, trusted Git, Docker, and approved validation images on a dedicated Linux agent.

The template deliberately fails until rescanSteps is configured. It is an integration template, not a turnkey scanner installation.

## 3. Configure execution and scanner isolation

The model phase gets only AI credentials. Build/test commands execute inside restricted containers with no agent credentials. The workspace is their only host data mount and .git is mounted read-only.

Use an ephemeral dedicated agent, not a shared developer machine. The host has Docker control; the application container does not. Set ownership of the disposable clone for UID/GID 1000. Prepopulate approved dependency caches or use a controlled network allowing only approved package registries. Never mount the Docker socket, host home, cloud credentials or control artifacts into the validation container.

Your rescanSteps also execute repository build/scanner code. They must use equivalent isolation. Supply only scan-specific least-privilege credentials through your scanner's supported mechanism. Do not execute untrusted repository shell scripts directly on the host that holds the attestation secret.

The templates do not provision Docker images, registry credentials, private package access, test databases or your scanner tasks. These are deployment-specific inputs, intentionally not guessed.

## 4. Create the separate publisher job

Use a fresh protected job with checkout: none. Download:
- the same reviewed PatchPilot binary and profile;
- bundle.json ONLY from the successful validation job in this exact trusted run.

Invoke pipelines/azure-publish.steps.yml. This job alone receives Bitbucket write credentials, and it shares PP_ATTESTATION_KEY with verify. Require environment approval for the pilot. Do not authorize credentials for PRs/forks or user-supplied pipeline definitions.

Serialize remediation per repository using a protected Azure resource's exclusive lock or equivalent queue policy. Branch identities reduce duplicates, but this CLI has no distributed lock. Configure the Azure lock/check outside YAML; it is not provisioned by these templates.

## 5. Preserve independent PR validation

Configure existing Azure/Bitbucket integration to validate the published source commit and report its status. Require reviewers and successful checks before merge. Do not treat an old successful build or the model's risk statement as PR approval.

Automatic PR validation triggering and branch protections depend on your current Bitbucket/Azure configuration and must be verified. PatchPilot never changes those settings, waives findings, merges or deploys.

## Pilot acceptance

1. Run with no approved rules and confirm a plan is produced but apply rejects it.
2. Approve one narrowly understood rule on a non-production project.
3. Confirm the baseline tests pass.
4. Confirm a failed candidate test and a stale scan both block bundle generation.
5. Confirm the targeted issue disappears in a real full rescan.
6. Confirm signed-file tampering blocks publication.
7. Publish a PR and confirm your existing independent validation runs.
8. Test recovery when destination main advances before publish.
