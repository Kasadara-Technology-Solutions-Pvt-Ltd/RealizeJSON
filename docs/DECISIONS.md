# Architecture decision records

## ADR-001 — Traditional VSSDK in-process host (not VisualStudio.Extensibility)

**Status:** Accepted (M0).

**Context.** The preview needs live access to unsaved Roslyn documents, caret position, focus and workspace events, and a
dockable WPF tool window.

**Decision.** Use the VSSDK `AsyncPackage` model with an async tool window, MEF `IWpfTextViewCreationListener`,
`VisualStudioWorkspace` and `GetOpenDocumentInCurrentContextWithChanges()`.

**Rationale.** These are supported in-process APIs that provide exactly the required data. The out-of-process
VisualStudio.Extensibility model runs in a separate process and communicates through brokered RPC services; we found no
documented brokered service exposing Roslyn `Document`/`Compilation` objects, so live semantic analysis would not be
possible without an in-process component. (Research note: Microsoft documents that features missing from the new SDK
require the VSSDK; the absence of a Roslyn workspace service in the out-of-process model is our reading of the docs and
was not verified with a prototype because no Windows/Visual Studio environment was available.)

**Rejected.** VisualStudio.Extensibility out-of-process (no verified Roslyn access); mixed in-proc/out-of-proc
(complexity without benefit).

## ADR-002 — Compile against VS 2022 17.8 / Roslyn 4.8; target `[17.8,)` (targets superseded by ADR-011)

**Decision.** Reference `Microsoft.VisualStudio.SDK 17.8.37222` and Roslyn `4.8.0` with `ExcludeAssets="runtime"`;
manifest `InstallationTarget Version="[17.8,)"` for amd64 and arm64.

**Rationale.** Visual Studio supplies Roslyn at runtime; compiling against the oldest supported version avoids
binding to APIs missing in older 17.x releases. Microsoft's guidance for Visual Studio 2026 states that compatibility
is determined by the lower bound of the installation target and that VS 2022 extensions install in VS 2026 without
changes. This makes the extension *installable* in VS 2026; it is **not** a claim of validation.

## ADR-003 — Host-independent netstandard2.0 engine

**Decision.** All logic except the VS adapter lives in netstandard2.0 libraries tested on .NET 10.

**Rationale.** Testable on any OS (the development environment is Linux), reusable for a future CLI (P2), and loadable
in the .NET Framework–based Visual Studio process.

## ADR-004 — No System.Text.Json / Newtonsoft dependency at runtime

**Decision.** Implement a small structured JSON writer/reader and port the serializers' naming/escaping algorithms.

**Rationale.** Visual Studio ships its own versions of these libraries; shipping another copy risks binding conflicts.
The real libraries are used in tests to verify the ports (escaping, naming policies, differential output).

## ADR-005 — Model CLR values, then emit JSON

**Decision.** Generate a modeled CLR value tree, apply omission rules to CLR values, then emit JSON.

**Rationale.** Required by FR-06, and it enables differential testing: the same modeled values are materialized via
reflection (tests only, without running constructors) and serialized by the real serializer for comparison.

## ADR-006 — Code-only WPF view with Visual Studio theme keys and editor classification colors

**Decision.** Build the tool window in C# without XAML; use `EnvironmentColors`/`VsResourceKeys` and the editor's
`IClassificationFormatMap` ("string", "number", "keyword", "identifier", "operator") for JSON highlighting.

**Rationale.** Theme, high-contrast and font settings follow the IDE automatically; brand colors never override IDE
tokens. Code-only WPF also compiles on non-Windows build agents.

## ADR-007 — Static configuration detection (syntax only)

**Decision.** Detect `AddJsonOptions`, `ConfigureHttpJsonOptions`, `Configure<JsonOptions>`, `AddNewtonsoftJson` and
`JsonConvert.DefaultSettings` by syntax; non-literal values are reported as unknown. Per-document results are cached
by text version.

**Rationale.** Never executes `Program.cs`, options delegates or DI code (FR-03). Only edited documents are rescanned.

## ADR-008 — Publisher display name and identity

The Visual Studio Marketplace publisher was created by the owner on 2026-11-26: ID `kasadara`, display name
"Kasadara Technology Solutions Pvt Ltd". From 0.1.1 the VSIX `Publisher` attribute uses that exact display name
(0.1.0 used "Kasadara Technology Solutions Private Limited"), so Marketplace uploads match the publisher.
`src/RealizeJSON.VisualStudio/publishManifest.json` references the publisher ID. The legal entity name stays in
`NOTICE`. Marketplace publishing is a manual step that needs the owner's credentials; no token is stored in the
repository or in CI.

## ADR-009 — No telemetry

No telemetry, including Visual Studio fault reporting helpers (`FileAndForget` is not used). Failures are written only
to the local Visual Studio ActivityLog without source text.

## ADR-010 — Apache License 2.0

The repository owner added the Apache License 2.0. It is referenced by the VSIX manifest and shipped in the VSIX.
Contributions are inbound = outbound under Apache-2.0 with a DCO sign-off (the repository requires sign-off for web
commits).

## ADR-011 — 1.0.0 installs only on validated hosts

**Status:** Accepted (1.0.0).

**Decision.** From 1.0.0 the manifest targets Visual Studio 2026 x64 only (`[18.0,)`, amd64), and the VSIX is no
longer marked as preview. The compile-time references stay at VS 2022 17.8 / Roslyn 4.8 (ADR-002).

**Rationale.** Only Visual Studio 2026 (18.0) x64 has been validated in a running IDE (`docs/TESTING.md`). A
production release should not install where it has never run. VS 2022 and Arm64 are re-added as installation targets
after they pass validation; no code change is expected for that. `build/inspect-vsix.ps1` rejects a manifest that
still targets 17.x, Arm64 or preview.

## Dependency versions

| Package | Version | Use | License |
|---|---|---|---|
| Microsoft.VisualStudio.SDK | 17.8.37222 | Compile-time VS APIs (not shipped) | Microsoft Visual Studio SDK license |
| Microsoft.VSSDK.BuildTools | 17.14.2142 | VSIX build tasks (Windows only, not shipped) | Microsoft license |
| Microsoft.CodeAnalysis.CSharp.Workspaces | 4.8.0 | Roslyn APIs (provided by VS at runtime) | MIT |
| Microsoft.CodeAnalysis.EditorFeatures.Text | 4.8.0 | Snapshot → Document mapping (provided by VS) | MIT |
| Microsoft.VisualStudio.LanguageServices | 4.8.0 | `VisualStudioWorkspace` (provided by VS) | MIT |
| Newtonsoft.Json | 13.0.4 | Tests only (differential reference) | MIT |
| xunit.v3 | 3.2.2 | Tests only | Apache-2.0 |
| xunit.runner.visualstudio | 3.1.5 | Tests only | Apache-2.0 |
| Microsoft.NET.Test.Sdk | 18.0.1 | Tests only | MIT |
| .NET SDK | 10.0.100+ (`global.json`, latestFeature) | Build | MIT |
