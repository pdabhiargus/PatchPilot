# Configuration and risk policy

Profiles are trusted executable policy: they select Docker images, arguments, network, editable paths and approved rules. Store them in the reviewed agent/control repository or a protected pipeline artifact, never load them from an untrusted target branch.

## Profile fields

| Field | Meaning |
|---|---|
| repository | Exact Bitbucket workspace/repository slug |
| destination | Protected destination branch |
| contextFiles | Exact existing files allowed to leave the organization for model analysis |
| editableFiles | Exact existing files the agent may modify; must also be context files |
| lowRiskRules | Approved source:rule identifiers, e.g. sonarqube:java:S2095 after repository-specific review |
| sensitivePaths | Case-insensitive path prefixes that always require review |
| maxFiles | 1–10, default 3 |
| maxChangedCharacters | Sum of old/new replacement lengths, default 8,000 |
| checks | Nonempty validation commands required by baseline and verify |
| checks[].image | Approved image pinned by sha256 digest; tags alone rejected |
| checks[].args | Argument array; no host shell parsing |
| checks[].timeoutSeconds | 1–1,800 seconds |
| checks[].network | Default none; a named egress-restricted Docker network may be configured by administrators |

The image's entrypoint must be compatible with the arguments. Build images must be vetted independently, contain required tools/caches, and run as UID/GID 1000. Give that identity access to the disposable workspace without exposing parent directories. Git metadata is mounted read-only.

## Risk is not severity

The finding severity sets remediation priority. Fix risk governs automation. An AI recommendation is only one input; it cannot override a denied rule, sensitive path, unsupported file type, size limit or incomplete evidence.

Only Low, with no stated uncertainties and an approved rule/path combination, can apply. Medium/High plans are still written for inspection. Passing tests does not prove a semantic security change is safe. Approve narrow, understood rules and include relevant tests.

Examples do not approve any rules. Do not enable every security rule simply because it appears in a scanner report.

## Secret configuration

Use protected Azure DevOps variables or a secret-store-linked variable group. Scope each variable to the required step. The .env.example file is documentation, not a credential file.

Generate PP_ATTESTATION_KEY from at least 32 random bytes, base64 encoded. Rotate it according to your secret policy; old bundles will become invalid. Bundles expire after 24 hours. Do not log keys or place them in command arguments.

PP_BITBUCKET_USER selects Basic authentication when present. Leave it absent for a supported Bearer access token. Configure the exact token type and minimum repository read/write plus PR read/write capabilities using current Bitbucket documentation. Do not use a personal password.

URLs are operator-configured HTTPS endpoints. Redirects are rejected. Restrict DNS and outbound access on the host; URL validation is not an SSRF firewall. Only the approved model endpoint should receive source code. Provider retention and data-residency approval are an organization decision.
