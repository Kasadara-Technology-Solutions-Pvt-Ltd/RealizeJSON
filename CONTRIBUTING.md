# Contributing to RealizeJSON

Thank you for your interest. RealizeJSON is licensed under the [Apache License 2.0](LICENSE). Contributions are
accepted under the same license (Apache-2.0 section 5, inbound = outbound).

Every commit must carry a Developer Certificate of Origin sign-off (`git commit -s`), certifying that you have the
right to submit it under the project license (https://developercertificate.org/).

## Ground rules

- The preview must never execute, build or load user code, and must never send data over the network.
- Never present static output as runtime output: anything uncertain needs a classification and a diagnostic.
- Serializer behavior changes need a differential test (`tests/RealizeJSON.Serialization.Tests/DifferentialTests.cs`)
  or, when the behavior cannot be reproduced by materialized values, a rule test and an entry in
  `docs/SERIALIZATION-SUPPORT.md`.
- Keep core libraries free of Visual Studio dependencies; only `RealizeJSON.VisualStudio` may reference the VS SDK.

## Development

```bash
pwsh ./build/build.ps1
```

```bash
pwsh ./build/test.ps1 -NoBuild
```

The VSIX can only be built on Windows (`pwsh ./build/package.ps1`). The rest of the solution, including the VS host
assembly, builds on Linux and macOS.

- C# 12, nullable enabled, analyzers on, warnings as errors.
- Follow `.editorconfig`; keep changes small and focused.
- New diagnostics get a new `RJxxxx` ID in `DiagnosticCatalog` (IDs are never reused).
- Update `CHANGELOG.md` under *Unreleased*.

## Pull requests

Describe the behavior change, link the issue, list the tests added and state how the change was validated
(automated tests and, for UI changes, the IDE validation steps in `docs/TESTING.md`).
