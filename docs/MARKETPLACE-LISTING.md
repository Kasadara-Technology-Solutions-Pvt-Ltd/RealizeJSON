# Visual Studio Marketplace listing (draft — not published)

> Publishing requires separate authorization, a confirmed Marketplace publisher ID, screenshots
> from a validated build and completion of the release checklist. Nothing here has been submitted.

## Metadata

| Field | Value |
|---|---|
| Display name | RealizeJSON - Live JSON Serialization Preview |
| Publisher ID | *To be created/confirmed by the copyright holder* |
| Categories | Coding; Other |
| Tags | json, serialization, System.Text.Json, Newtonsoft.Json, Roslyn, DTO, API, contract |
| Icon | `assets/branding/png/realizejson-128.png` |
| Supported products | Visual Studio 2022 17.8+ (x64, Arm64) — add Visual Studio 2026 only after validation |
| Price | Free |
| License | Apache-2.0 |
| Repository | https://github.com/Kasadara-Technology-Solutions-Pvt-Ltd/RealizeJSON |

## Overview (listing body)

**See your JSON before your code runs.**

RealizeJSON shows how your C# types serialize to JSON — live, as you type, without building or running your
application. Put the caret on a DTO, record or contract and get a deterministic example payload that follows the real
rules of **System.Text.Json** or **Newtonsoft.Json**: naming policies, ignore conditions, ordering, fields, enums,
dictionaries, polymorphism and more.

- Works on unsaved code — no build, no debugger, no runtime.
- System.Text.Json and Newtonsoft.Json profiles, including ASP.NET Core web defaults.
- Detects `AddJsonOptions` / `AddNewtonsoftJson` configuration statically.
- Contract Inspector explains why each member is included, renamed or excluded.
- Copy or export valid JSON; diagnostics stay outside the payload.
- Offline, private, free: no telemetry, no account, no cloud.

**Static preview, honestly labeled.** Values are synthetic samples. Anything that depends on runtime data, custom
converters or unknown configuration is clearly marked as Estimated, Conditional, Unsupported or Unresolved.

## Privacy statement

RealizeJSON runs entirely inside Visual Studio on your machine. It does not collect telemetry, does not send source
code, payloads or usage data anywhere, does not require an account and makes no network requests. It does not
execute, build or load your project's code. Settings are stored in the Visual Studio settings store and, only if you
choose *Save solution profile*, in a `.realizejson.json` file in your solution folder.

## Known limitations

- Previews are static models with sample values, not captured runtime output.
- Custom converters, contract resolvers, naming policies and `TypeInfoResolver` customizations are reported, not
  executed.
- Newtonsoft `TypeNameHandling` is not modeled.
- Configuration detection recognizes literal settings in common ASP.NET Core patterns only.
- Validated environments: see the compatibility matrix in `IMPLEMENTATION-STATUS.md`.

## Screenshots

None yet. Screenshots must come from a validated build running in Visual Studio (do not use mock-ups).
