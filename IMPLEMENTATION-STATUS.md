# Implementation status

Last updated: 2026-11-27. Legend: **Done** = implemented and covered by automated tests; **Done (unvalidated)** =
implemented and compiled, requires the in-IDE validation in `docs/TESTING.md`; **Deferred** = intentionally postponed
with impact noted; **Blocked** = cannot be completed in the current environment.

## Environment and blockers

| Item | State |
|---|---|
| Development environment | Linux x64 container, .NET SDK 10.0.115, PowerShell 7.7, no Windows, no Visual Studio |
| GitHub | Resolved 2026-11-26: CI on push and pull requests; releases published from tags. |
| VSIX packaging | Done in CI (`windows-latest`, full MSBuild, VSSDK BuildTools 18.12.1606): `RealizeJSON-vsix` artifact built and verified by `build/inspect-vsix.ps1`. 0.1.1 installs in Visual Studio 2026 (18.0). |
| IDE validation | **Partial**, performed by the maintainer: 0.1.2 on Visual Studio 2026 (18.0) passed steps 1, 3, 4, 5, 6, 8, 10, 11, 12, 15. Other steps and other hosts not run. Results: `docs/TESTING.md` § IDE integration validation. |

## Compatibility matrix

| Host | Installable per manifest | Validated |
|---|---|---|
| Visual Studio 2022 17.8+ x64 | No from 1.0.0 (ADR-011) | **No** |
| Visual Studio 2022/2026 Arm64 | No from 1.0.0 (ADR-011) | **No** |
| Visual Studio 2026 x64 | Yes (`[18.0,)`, amd64) | **Partial**: 0.1.2 on 18.0 passed steps 1, 3, 4, 5, 6, 8, 10, 11, 12, 15 (0.1.1's tool window failed to open; fixed in 0.1.2) |
| Visual Studio 2019 and earlier, VS Code, VS for Mac | No | n/a |

Do not advertise any host as supported until it is validated.

## Milestones

| Milestone | State |
|---|---|
| M0 Feasibility | Done: host decision (ADR-001), solution, build scripts. SDK spike limited to compilation against VS SDK reference assemblies (no live IDE). |
| M1 Core engine | Done |
| M2 MVP | Done (unvalidated in IDE) |
| M3 Advanced (P1) | Done except FR-06 constant-initializer mode and Newtonsoft type-name polymorphism (deferred) |
| M4 Hardening | Differential, security, lifecycle, performance (test host) done; IDE accessibility/compatibility validation blocked |
| M5 Distribution | Docs, notices, listing draft, checklist, license, releases 0.1.0–1.0.1 done; 1.0.1 on the Visual Studio Marketplace (2026-11-27); IDE validation partial (VS 2026 18.0) |

## Requirements

| ID | Requirement | State | Evidence / notes |
|---|---|---|---|
| FR-01 | View menu command, editor context command, configurable shortcut | Done (unvalidated) | `RealizeJSONPackage.vsct`, `PreviewCommands.cs`; default Ctrl+Shift+Alt+J |
| FR-01 | Type at caret via semantic symbols; class/record/struct/interface/enum/collections; aliases, partial, nested, referenced | Done | `TypeLocatorTests` (13), metadata/cross-project integration tests |
| FR-01 | Type selector; FQN and project/TFM context; open generic diagnostic + constructed candidates | Done | `Open_generic_declaration_offers_constructed_usages`, `Target_framework_is_inferred…` |
| FR-01 | Follow caret; pin/unpin (P1) | Done | `Pinned_type_survives_caret_navigation_and_refreshes_on_edit` |
| FR-02 | Refresh on unsaved edits, document/caret changes, dependent types, project and profile changes | Done | Coordinator tests; workspace dependency filtering |
| FR-02 | Debounce (default 350 ms, configurable), cancellation, stale-result protection | Done | `Rapid_edits_are_debounced…`, `Superseded_results_never_overwrite…` |
| FR-02 | No refresh on irrelevant caret movement | Done | `Caret_moves_inside_the_same_type_do_not_republish` |
| FR-02 | Last valid preview on syntax errors marked stale | Done | `Syntax_errors_keep_last_valid_preview_marked_stale` |
| FR-02 | Suspend while hidden; solution/document close; disposal | Done | Coordinator tests; `PreviewSession.Dispose` (unvalidated in IDE) |
| FR-03 | STJ and Newtonsoft providers; naming, dictionary keys, enums, null/default omission, fields, read-only, indentation | Done | Differential matrix |
| FR-03 | Library defaults / ASP.NET Core web defaults / Custom; no silent camelCase | Done | `Library_defaults_do_not_assume_camel_case` |
| FR-03 | Only meaningful settings per serializer, with explanations | Done | `ProfileSettings.For/ExplainUnsupported`; UI filters naming choices |
| FR-03 | Per-user defaults; optional solution-scoped profile without writing project files | Done | Settings and solution profile tests; file written only on explicit command |
| FR-03 | Precedence manual > detected > preset | Done | `Precedence_is_manual_over_detected_over_preset`, integration test |
| FR-03 | Static detection of ASP.NET Core configuration (P1), ambiguity selection | Done | `ConfigurationDetectorTests`, `Multiple_configurations…` |
| FR-04 | STJ attribute table (JsonPropertyName … JsonConstructor) | Done | Differential + rule tests; polymorphism P1 done |
| FR-04 | Newtonsoft attribute table (JsonProperty … JsonConstructor, DataContract precedence) | Done | Differential + rule tests |
| FR-04 | Fully qualified attribute identity; version-dependent semantics; unknown customizations reported | Done | `SymbolExtensions.FindAttribute`; version gate for `JsonInclude` non-public, Half/Int128, DateOnly |
| FR-05 | Type coverage incl. special types, collections, dictionaries, generics, tuples, unsupported/unresolved types | Done | See `docs/SERIALIZATION-SUPPORT.md` |
| FR-05 | Type-level converters before traversal | Done | Recognized/unknown converters reported; unknown converters keep default shape with warning (documented choice) |
| FR-06 | Sample values / CLR defaults; deterministic; heuristics; counts 0–5; unique keys; enums; explicit nulls | Done | Determinism test over all fixtures; differential |
| FR-06 | Valid JSON via structured writer; omission on CLR values; decimal precision | Done | Writer tests; differential |
| FR-06 | Depth/size/node limits; cycle detection on traversal path; reference handling | Done | Limit tests; `Cycles_are_detected_on_the_traversal_path_only`; Preserve differential |
| FR-06 | Safe constant-initializer inference mode (P1) | **Deferred** | Impact: CLR-defaults mode shows `default(T)` rather than declared initializers; documented |
| FR-07 | Dockable themed tool window, toolbar, read-only highlighted JSON, wrap, formatted/compact, states | Done (unvalidated) | `PreviewControl.cs` |
| FR-07 | Copy only valid JSON; confirm incomplete; export (P1); tree view (P1); search (P1) | Done (view model tested; UI unvalidated) | `ViewModelTests` |
| FR-07 | No web server/WebView; native WPF | Done | Code-only WPF |
| FR-08 | Contract Inspector with excluded members, explanations, provenance, flags, navigation, sensitive heuristic (P1) | Done (navigation unvalidated in IDE) | `InspectorBuilder`, `Navigation_distinguishes_source_and_metadata` |
| FR-09 | STJ discriminators, variant selector, declared view, unknown variants reported (P1) | Done | Polymorphism differential/rule tests |
| FR-09 | Newtonsoft type metadata settings | **Deferred** | `TypeNameHandling` is reported as unknown configuration and never suggested; impact: no `$type` preview for Newtonsoft |
| NFR-01 | Offline, no transmission | Done | No network APIs referenced |
| NFR-02 | Never execute/build/load target binaries | Done | `SecurityTests` |
| NFR-03 | On-demand activation | Done (unvalidated) | No `ProvideAutoLoad` |
| NFR-04 | p95 < 500 ms (100-member graph) | Met in test host (p95 11.2 ms); **in-IDE measurement pending** | `docs/TESTING.md` |
| NFR-05 | Bounded output, recursion, caches, concurrency | Done | `docs/ARCHITECTURE.md` § Bounds; memory not measured |
| NFR-06 | Cancellation, fault isolation, last-good preview | Done | Coordinator tests |
| NFR-07 | Accessibility, keyboard, theme/high contrast, high DPI | Implemented (automation names, live regions, access keys, theme keys); Light/Dark themes validated on VS 2026 (step 15, high contrast not run); keyboard and DPI **not validated** | IDE validation steps 15–17 |
| NFR-08 | No telemetry; logs without source text | Done | ADR-009; ActivityLog only |
| NFR-09 | Minimal dependencies, notices, inventory, reproducible builds | Done | `THIRD-PARTY-NOTICES.md`, `docs/DECISIONS.md`; deterministic builds |
| NFR-10 | Nullable, analyzers, formatting config, documented interfaces | Done | Warnings as errors; `.editorconfig` |
| §2.2 | Brand identity, assets, palette, contrast | Done | `docs/BRAND.md`, `assets/branding/` |
| §11 | VSIX with manifest, identifier, icon, metadata | Done (installs on VS 2026 18.0) | `source.extension.vsixmanifest`, `build/inspect-vsix.ps1` |
| §11 | License | Done | Apache-2.0 (`LICENSE`), included in the VSIX |

## Known gaps to address next

2. Finish the IDE validation procedure: remaining steps on VS 2026; validate VS 2022 and Arm64 before widening the installation targets; capture screenshots.
3. FR-06 constant-initializer inference (P1) and Newtonsoft `TypeNameHandling` previews (P1, opt-in only).
4. Measure in-IDE latency and memory across solution switches.
