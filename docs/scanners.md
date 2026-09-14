# Scanner integration contract

## Implemented read adapters

- collect-sonar calls api/issues/search with a project, branch, unresolved filter and pagination. It uses issue keys as identities. Validate this API against your SonarQube release. Security hotspots require a distinct review workflow and are not collected by this adapter.
- collect-sonatype consumes an immutable IQ RAW component report URL. It reads components[].securityData.securityIssues. It does not interpret all license, quality or policy violations, nor initiate an IQ evaluation.
- import-sarif normalizes runs/results from SARIF. Same source/rule/message/path occurrences are grouped conservatively. SARIF execution success is not inferred from an empty results array.
- A trusted integration can supply Finding[] directly for another tool.

Raw data is not proof of scanner success. Native report retrieval does not establish branch, commit, completed evaluation or coverage automatically.

## Required scan artifact

Both baseline.scan.json and candidate.scan.json implement the Scan record. See examples/scan.json; its defaults intentionally FAIL validation.

A trusted adapter must:
1. Start from the correct isolated application checkout.
2. Calculate its hash using PatchPilot hash WORKSPACE immediately before scanning.
3. Run the organization's actual scan and wait for completion. For remote scanners, poll the submitted task/report ID and confirm the branch/revision/analysis identity.
4. Recalculate the content hash and reject changes during scanning.
5. Collect the entire issue set for the same scope, configured scanners, policy/profile and exclusions.
6. Write schema=1, the actual treeHash, stable scope, sources, complete, policyGatePassed, actual completedAt and normalized findings.
7. Retain native report IDs, task IDs, revisions, logs and test evidence in protected CI artifacts linked by the build URL.

Never set complete or policyGatePassed from model output. Never use a latest-report endpoint without confirming it is the evaluation you submitted. Do not mark a scan successful merely because an API returned HTTP 200.

The baseline can have an existing failed policy gate. The candidate must pass the configured policy gate. This conservative initial policy can block a fix where unrelated pre-existing issues keep the gate red; treat that as escalation, not permission to waive issues.

The candidate must have the same scope and scanner set, be newer than the baseline, contain no new normalized finding identities, and omit the target identity. Removed/waived/suppressed results must not be represented as resolved; your adapter must preserve these as unresolved or reject the evaluation.

## Identity and coverage limitations

Sonar keys are stable within their analysis context but branch-key behavior must be checked in your deployment. The scope should describe the logical application and pinned quality profile, while the trusted adapter separately verifies the temporary analysis branch/revision.

SARIF message/path grouping can miss distinctions between identical occurrences and is sensitive to message changes. Validate fingerprints against your scanner before enabling automation. A reduced scope or scanner failure must fail closed, never produce a successful empty scan.

IQ identities exclude dependency version so the same vulnerability remains recognizable across upgrades. Distinct transitive paths for the same component are grouped; all must disappear. IQ support currently covers security issues, not license/policy remediation.

## References

- Sonatype REST overview: https://help.sonatype.com/en/rest-apis.html
- IQ report contracts: https://help.sonatype.com/en/report-rest-api.html
- IQ remediation candidates (future executor): https://help.sonatype.com/en/component-remediation-rest-api.html
- SonarQube Web API: https://docs.sonarsource.com/sonarqube-server/extension-guide/web-api
- Bitbucket source upload: https://developer.atlassian.com/cloud/bitbucket/rest/api-group-source/
- Bitbucket PR API: https://developer.atlassian.com/cloud/bitbucket/rest/api-group-pullrequests/

Review the live version-specific API before connecting a production tenant.
