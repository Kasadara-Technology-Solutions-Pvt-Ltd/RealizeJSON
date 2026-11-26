<p align="center">
  <img src="assets/branding/png/realizejson-wordmark-light.png" alt="RealizeJSON" width="420" />
</p>

# RealizeJSON — Live JSON Serialization Preview for Visual Studio

**See your JSON before your code runs.**

RealizeJSON is a lightweight, offline-first Visual Studio extension that shows how a C# type would serialize to JSON
— as you type, without building, running or debugging your application. It analyzes your live (including unsaved)
source with Roslyn and applies the real member, naming and attribute rules of **System.Text.Json** or
**Newtonsoft.Json** to produce a deterministic, serialization-aware example payload.

> RealizeJSON shows a **static preview with synthetic sample values**. It is a model of the serialization contract,
> not guaranteed runtime output. Every preview is labeled (for example
> `Static preview · Sample values · System.Text.Json`), and anything that depends on runtime values, custom
> converters or unknown configuration is reported as *Estimated*, *Conditional*, *Unsupported* or *Unresolved*.

## Status

| Area | State |
|---|---|
| Core engine (Roslyn analysis, contracts, both serializers, sample generation) | Implemented, 630+ automated tests |
| Differential accuracy vs. real serializers | 433 scenario tests match System.Text.Json (.NET 10) and Newtonsoft.Json 13.0.4 byte-for-byte |
| Visual Studio host (tool window, commands, live events) | Implemented; compiles against the VS 2022 17.8 SDK. **Not yet validated in a running IDE** |
| VSIX package | Build pipeline in place (Windows only); **not yet produced or installed** — see [IMPLEMENTATION-STATUS.md](IMPLEMENTATION-STATUS.md) |

See [IMPLEMENTATION-STATUS.md](IMPLEMENTATION-STATUS.md) for the requirement-by-requirement status and blockers.

## Features

- **Live preview** of the type at the caret (declarations, references, aliases, nested, partial and constructed
  generic types such as `CreateDTO<CaseDTO>`), refreshed on unsaved edits with a configurable debounce (default 350 ms).
- **Serializer profiles**: System.Text.Json and Newtonsoft.Json, each with *Library defaults*, *ASP.NET Core web
  defaults* and *Custom* presets. Standalone defaults never assume camelCase.
- **Real attribute semantics**: `JsonPropertyName`, `JsonIgnore` (all conditions), `JsonInclude`, `JsonPropertyOrder`,
  `JsonRequired`, `JsonExtensionData`, `JsonNumberHandling`, `JsonConverter`, `JsonPolymorphic`/`JsonDerivedType`,
  `JsonConstructor`; Newtonsoft `JsonProperty`, `JsonIgnore`, `JsonObject` member modes, `DefaultValue`,
  `DataContract`/`DataMember`/`IgnoreDataMember`, `ShouldSerialize*`, `EnumMember` and more — matched by fully
  qualified symbol identity, never by short name.
- **Project configuration detection** (static, never executed): `AddJsonOptions`, `ConfigureHttpJsonOptions`,
  `Configure<JsonOptions>`, `AddNewtonsoftJson`, `JsonConvert.DefaultSettings`. Precedence: manual overrides >
  detected settings > preset defaults.
- **Deterministic samples** (`Sample values` or `CLR defaults`), collection counts 0–5, bounded depth/size/node
  limits with valid, clearly marked incomplete output, and cycle detection on the traversal path.
- **Contract Inspector**: every member (included and excluded) with JSON name, path, CLR type, provenance and an
  explanation; navigation to source; heuristic warning for possibly sensitive members.
- **Polymorphism**: System.Text.Json discriminators and a variant selector.
- **Copy/export** valid JSON only (diagnostics stay outside the JSON); confirmation before copying incomplete output.

## Privacy and safety

- No telemetry, no network access, no account, no cloud service. Source and payloads never leave your machine.
- The preview never executes, builds or loads your code: constructors, getters, converters, contract resolvers,
  source generators and `Program.cs` are never invoked. This is covered by automated tests with fixtures whose
  constructors and getters have observable side effects.

## Requirements

| Item | Requirement |
|---|---|
| IDE | Visual Studio 2022 (17.8 or later) on Windows, x64 or Arm64. Visual Studio 2026 accepts VS 2022 extensions, but RealizeJSON has **not** been validated there yet. |
| Workload | C# language services (installed with any .NET workload) |
| Build (contributors) | .NET SDK 10.0.100+ (`global.json`); for the VSIX: Windows with Visual Studio 2022+ or Build Tools and the *Visual Studio extension development* workload |

## Getting started

1. Install the VSIX (see [docs/INSTALL.md](docs/INSTALL.md)).
2. Open a C# file and choose **View › Other Windows › RealizeJSON**, or right-click in the editor and choose
   **Preview as JSON** (default shortcut **Ctrl+Shift+Alt+J**, rebindable under *Tools › Options › Environment ›
   Keyboard*).
3. Move the caret between types; the preview follows. Use **Pin** to keep a type while you navigate.

## Building from source

```bash
pwsh ./build/build.ps1
```

```bash
pwsh ./build/test.ps1 -NoBuild
```

On Windows with Visual Studio 2022+ (or Build Tools) installed:

```bash
pwsh ./build/package.ps1
```

The VSIX is written to `artifacts/vsix/`. Debug in the experimental instance by setting `RealizeJSON.VisualStudio` as
the startup project in Visual Studio and pressing F5 (`devenv /rootsuffix Exp`).

## Documentation

| Document | Contents |
|---|---|
| [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md) | Modules, data flow, threading and lifecycle |
| [docs/SERIALIZATION-SUPPORT.md](docs/SERIALIZATION-SUPPORT.md) | Support matrix per serializer, known limitations |
| [docs/DECISIONS.md](docs/DECISIONS.md) | Architecture decision records and dependency versions |
| [docs/TESTING.md](docs/TESTING.md) | Test strategy, evidence, manual IDE validation steps |
| [docs/BRAND.md](docs/BRAND.md) | Brand identity, assets and accessibility |
| [docs/INSTALL.md](docs/INSTALL.md) | Install, uninstall, troubleshooting |
| [docs/MARKETPLACE-LISTING.md](docs/MARKETPLACE-LISTING.md) | Marketplace listing draft and privacy statement |
| [docs/RELEASE-CHECKLIST.md](docs/RELEASE-CHECKLIST.md) | Release steps and open release blockers |
| [CHANGELOG.md](CHANGELOG.md) | Release history |
| [CONTRIBUTING.md](CONTRIBUTING.md) | How to contribute |

## License

The project license has **not yet been finalized** by the copyright holder; see
[docs/LICENSE-RECOMMENDATION.md](docs/LICENSE-RECOMMENDATION.md). Until a `LICENSE` file is added, no license is
granted. Third-party components are listed in [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).

RealizeJSON is a contribution of Kasadara Technology Solutions Private Limited to the developer community.
