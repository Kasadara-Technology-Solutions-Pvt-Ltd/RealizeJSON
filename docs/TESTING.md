# Testing

## Running the tests

```bash
pwsh ./build/build.ps1
```

```bash
pwsh ./build/test.ps1 -NoBuild
```

TRX results are written to `artifacts/test-results/`. Tests run on Microsoft.Testing.Platform (opted in through
`global.json`), so plain `dotnet test --solution RealizeJSON.sln` works as well.

## Suites

| Project | Scope | Count* |
|---|---|---|
| `RealizeJSON.Core.Tests` | Writer escaping vs. real STJ/Newtonsoft, indentation and line map, budget truncation, reader validity and bounds, naming algorithms vs. real STJ policies/Newtonsoft strategies, profile precedence, option clamping, diagnostic catalog consistency | 100 |
| `RealizeJSON.CodeAnalysis.Tests` | Type at caret (declarations, references, aliases, nested, partial, generic arguments, constructed/open generics, `var`, predefined types), `TypeKey` round trip and parsing, configuration detection | 28 |
| `RealizeJSON.Serialization.Tests` | Differential matrix vs. real serializers, rule/diagnostic tests, limits, determinism and validity over every fixture type, security (no code execution) | 469 |
| `RealizeJSON.Integration.Tests` | `PreviewEngine` over Roslyn workspaces: acceptance scenario, unsaved edits, cross-project and metadata-only types, detected configuration and precedence, ambiguity, pinning, Newtonsoft profile, invalid syntax, cancellation, performance, TFM inference | 15 |
| `RealizeJSON.UI.Tests` | Coordinator: debounce, stale-result races, last-good retention, suspension while hidden, caret relevance, pinning, dependency filtering, document/solution close, unsupported language, disposal; view model commands; settings persistence, malformed input, migration; solution profile | 26 |

\* Counts from the last local run (Linux x64, .NET SDK 10.0.115, 638 tests, all passing).

## Differential testing

`tests/Shared/DifferentialHarness.cs` compiles `tests/Fixtures/Contracts.cs` with Roslyn, loads it into a collectible
`AssemblyLoadContext` (tests only), materializes the preview's modeled CLR values with
`RuntimeHelpers.GetUninitializedObject` and reflection (no constructors, initializers or getters run), serializes with
the real serializer using equivalent settings, and compares compact JSON byte-for-byte. The matrix covers 24 fixture
types × 2 serializers × 2 presets × 2 sample modes, plus 13 settings variations (ignore conditions, enum strings and
naming, snake/kebab/upper naming, dictionary key naming, fields, read-only, numbers-as-strings, reference preservation,
indentation), collection counts 0–5, polymorphic variants and explicit null mode. User project binaries are never
used as fixtures.

Behaviors discovered by differential testing and now encoded: System.Text.Json writes dates, GUIDs and Base64 without
string escaping; STJ orders properties before fields within each type while Newtonsoft orders all fields before all
properties; STJ does not wrap arrays (`T[]`) with `$values` under `Preserve`; Newtonsoft assigns `$id` to structs;
Newtonsoft writes enum dictionary keys with declared names regardless of converter naming; Newtonsoft never treats
`'\0'` as the default `char` for `DefaultValueHandling.Ignore`.

## Security tests

`SecurityTests.Preview_does_not_execute_user_code` previews a fixture whose static constructor, constructor and
getter increment a counter and throw, both from source and from a metadata-only reference, then reads the counter in a
separately loaded copy of the assembly: it stays 0.

## Performance (NFR-04)

`Performance_100_member_graph_p95_is_below_target` warms the engine, then analyzes a 100-member DTO graph 50 times,
each with a new text snapshot (as a keystroke would produce).

| Environment | p50 | p95 | max |
|---|---|---|---|
| Linux x64, 4 cores, .NET 10.0.15, Roslyn 4.8.0, `AdhocWorkspace` | 7.2 ms | 11.2 ms | 21.9 ms |

This is measured in the test host, **not** inside Visual Studio (.NET Framework, `VisualStudioWorkspace`, larger
solutions). In-IDE measurement remains a manual validation step. Memory was not measured.

## IDE integration validation (manual)

Each advertised IDE version must pass all steps before release.

| Date | Build | Host | Passed | Not run | Notes |
|---|---|---|---|---|---|
| 2026-11-26 | 0.1.1 | Visual Studio 2026 (18.0) | 1 | All others | Step 3 failed: the tool window threw while being constructed (style `TargetType` mismatch on *Pin*); fixed in 0.1.2 |
| 2026-11-26 | 0.1.2 | Visual Studio 2026 (18.0) | 1, 3, 4, 5, 6, 8, 10, 11, 12, 15 | 2, 7, 9, 13, 14, 16–22 | Step 15 covered Light and Dark themes; high contrast not checked. Performed by the maintainer |


| # | Step | Expected |
|---|---|---|
| 1 | Install the VSIX via VSIXInstaller (VS 2022 17.8+, x64; VS 2022 Arm64; VS 2026) | Installs without warnings; appears in *Extensions › Manage Extensions* |
| 2 | Start VS; check *Help › About* and ActivityLog | Package not loaded until first use |
| 3 | *View › Other Windows › RealizeJSON* | Window opens docked next to Solution Explorer; caption and icon correct |
| 4 | Open the acceptance `CustomerDTO`; place caret in the class | Preview matches `README` example with Web defaults; Password excluded |
| 5 | Type a new property without saving | Preview refreshes within ~0.5 s; no build is triggered |
| 6 | Introduce a syntax error | Last preview kept, state *Stale*, RJ0006 shown |
| 7 | Move caret between two classes / within one class | Switches / does not flicker |
| 8 | Pin, navigate to another file, edit the pinned type | Pinned type stays and refreshes |
| 9 | Right-click › *Preview as JSON*; press Ctrl+Shift+Alt+J; rebind in Keyboard options | Command works; rebinding persists |
| 10 | Switch serializer/preset/naming/values/count/enum strings/formatted | Preview updates; settings persist after restart |
| 11 | Copy; copy with an incomplete preview (Max nodes = 10) | Clipboard holds valid JSON; confirmation shown for incomplete |
| 12 | Export… | Save dialog; file contains valid JSON |
| 13 | Contract Inspector: double-click a member; metadata member | Navigates to declaration; message for metadata |
| 14 | Dock/undock/float/auto-hide the window; hide and show it | No errors; hidden window does not analyze |
| 15 | Switch Light/Dark/Blue/High Contrast themes | All text readable; JSON colors follow editor theme |
| 16 | Keyboard only: Tab through toolbar, tabs, grids; access keys (Alt+letter); Enter on a JSON line | All controls reachable; screen reader announces names and state |
| 17 | 150%/200% DPI, narrow pane (250 px) | Toolbar wraps; no clipped controls |
| 18 | Close the document; close and reopen the solution | No exceptions; empty state after close |
| 19 | Open a non-C# file | *Unsupported* state |
| 20 | Add `.realizejson.json` to the solution folder; *Save solution profile…* | Profile applied; file written only after confirmation |
| 21 | Uninstall, restart, reinstall | Clean uninstall/reinstall; settings retained in user store |
| 22 | Measure refresh latency for a 100-member DTO in a real solution | Record p95 (target < 500 ms after debounce) |
