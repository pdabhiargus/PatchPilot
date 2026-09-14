# Contributing

Keep changes focused and document behavior, configuration, validation and limitations.

1. Build using the SDK selected by global.json.
2. Run the executable tests in tests/PatchPilot.Tests.
3. Add adversarial regression tests for policy, path, API and artifact changes.
4. Never introduce arbitrary model shell execution, credential logging or silent scan-success defaults.
5. Do not weaken tests or scanning to make an agent-generated fix pass.
6. Document new environment variables in .env.example and docs/configuration.md.
7. Validate API changes against official version-specific documentation and mocked contract tests.
8. Request review before changing defaults that expand write scope, network access or automation eligibility.

The current test runner intentionally needs no third-party packages. Tests are executed by dotnet run, not dotnet test.
