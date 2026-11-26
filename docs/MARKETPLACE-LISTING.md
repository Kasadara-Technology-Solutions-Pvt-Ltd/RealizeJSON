# Visual Studio Marketplace listing (draft — not published)

> Publishing requires separate authorization, screenshots
> from a validated build and completion of the release checklist. Nothing here has been submitted.

## Metadata

| Field | Value |
|---|---|
| Display name | RealizeJSON - Live JSON Serialization Preview |
| Publisher | `kasadara`: Kasadara Technology Solutions Pvt Ltd |
| Categories | Coding; Other |
| Tags | json, serialization, System.Text.Json, Newtonsoft.Json, Roslyn, DTO, API, contract |
| Icon | `assets/branding/png/realizejson-128.png` |
| Supported products | Visual Studio 2026 (18.0+), x64. Add Visual Studio 2022 / Arm64 only after validation |
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

## Publisher profile

The publisher was created on 2026-11-26: ID `kasadara`, display name **Kasadara Technology Solutions Pvt Ltd**. The
text below is the suggested profile content; bracketed items are for the company to fill in or remove. Current
Marketplace field length limits have not been re-verified.

**Display name:** Kasadara Technology Solutions Pvt Ltd

**Short description:**

> Kasadara Technology Solutions builds practical, privacy-conscious developer tools for the .NET ecosystem, released
> as open source.

**Full description:**

> Kasadara Technology Solutions Private Limited builds developer tools that make everyday engineering work clearer and
> faster. Our extensions are designed to be lightweight, run offline and respect your privacy: no telemetry, no
> accounts, and your source code never leaves your machine.
>
> **RealizeJSON**: see your JSON before your code runs. RealizeJSON previews how your C# types serialize with
> System.Text.Json and Newtonsoft.Json, live as you type, without building or running your application.
>
> Our extensions are open source under the Apache License 2.0. Source code, issue tracking and release notes are on
> GitHub: https://github.com/Kasadara-Technology-Solutions-Pvt-Ltd
>
> [About Kasadara: one or two sentences on what the company does beyond these tools, if it should be public.]

| Field | Value |
|---|---|
| Publisher ID | `kasadara` (created) |
| Logo | Kasadara company logo (not the RealizeJSON icon). |
| Company website | [Company website, if public] |
| Support | https://github.com/Kasadara-Technology-Solutions-Pvt-Ltd/RealizeJSON/issues or a support email |
| Source code | https://github.com/Kasadara-Technology-Solutions-Pvt-Ltd |

Before publishing:

- Confirm every claim. "Builds developer tools" reflects RealizeJSON only; remove it if it doesn't describe the
  company's wider business.
- "No telemetry, no accounts" is true of RealizeJSON today and must stay true for every extension published under this
  publisher.
