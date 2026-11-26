# Changelog

All notable changes to this project are documented here. The format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and the project uses [Semantic Versioning](https://semver.org/).

## [Unreleased]

## [0.1.0] - 2026-11-26

### Added

- Roslyn-based type selection at the caret (declarations, references, aliases, nested, partial, constructed and open
  generic types) with a candidate selector and pinning by stable type key.
- Serialization contract model with Resolved / Estimated / Conditional / Unsupported / Unresolved classifications.
- System.Text.Json and Newtonsoft.Json rule providers covering member selection, naming, ordering, ignore conditions,
  fields, read-only members, extension data, number handling, converters, enums, dictionaries, data contracts,
  polymorphism and reference preservation.
- Library defaults, ASP.NET Core web defaults and custom profiles with manual > detected > preset precedence.
- Static detection of `AddJsonOptions`, `ConfigureHttpJsonOptions`, `Configure<JsonOptions>`, `AddNewtonsoftJson` and
  `JsonConvert.DefaultSettings` configuration.
- Deterministic sample generator (sample values / CLR defaults, 0–5 collection elements, explicit null mode) with depth,
  node and output-size limits and traversal-path cycle detection.
- Structured JSON writer with serializer-specific escaping, budget truncation and line mapping.
- Live preview coordinator (debounce, cancellation, stale-result protection, last-good retention, suspension while
  hidden), view model, settings persistence with schema migration, and optional `.realizejson.json` solution profile.
- Visual Studio host: on-demand async package, *View › Other Windows › RealizeJSON*, editor context menu
  *Preview as JSON* (Ctrl+Shift+Alt+J), dockable themed tool window with JSON, tree, Contract Inspector and Diagnostics
  tabs, copy/export, navigation and options page.
- Brand assets, build/test/package scripts, CI workflow, documentation and 638 automated tests including differential
  tests against the real serializers.

### Known limitations

- Installation and in-IDE behavior (the VSIX is built and content-verified by CI, but has not been installed in
  Visual Studio yet). See `IMPLEMENTATION-STATUS.md`.

[Unreleased]: https://github.com/Kasadara-Technology-Solutions-Pvt-Ltd/RealizeJSON/compare/v0.1.0...HEAD
[0.1.0]: https://github.com/Kasadara-Technology-Solutions-Pvt-Ltd/RealizeJSON/releases/tag/v0.1.0
