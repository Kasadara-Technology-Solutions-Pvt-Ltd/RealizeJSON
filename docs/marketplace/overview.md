![RealizeJSON](https://raw.githubusercontent.com/Kasadara-Technology-Solutions-Pvt-Ltd/RealizeJSON/main/assets/branding/png/realizejson-wordmark-light.png)

# See your JSON before your code runs.

RealizeJSON shows how your C# types serialize to JSON, live as you type, without building or running your
application. Put the caret on a DTO, record or contract and get a deterministic example payload that follows the real
rules of **System.Text.Json** or **Newtonsoft.Json**.

> **Preview release.** RealizeJSON is new and has not yet been validated in every Visual Studio version. Please report
> problems on [GitHub](https://github.com/Kasadara-Technology-Solutions-Pvt-Ltd/RealizeJSON/issues).

## Features

- **Live preview** of the type at the caret, including unsaved edits, nested and generic types such as
  `CreateDTO<CaseDTO>`.
- **Serializer profiles**: System.Text.Json and Newtonsoft.Json, each with library defaults, ASP.NET Core web defaults
  or a custom profile. Standalone defaults never assume camelCase.
- **Real attribute semantics**: `JsonPropertyName`, `JsonIgnore`, `JsonInclude`, `JsonPropertyOrder`,
  `JsonPolymorphic`/`JsonDerivedType`, Newtonsoft `JsonProperty`, `DataContract`/`DataMember`, `DefaultValue`,
  `ShouldSerialize*` and more.
- **Project configuration detection**: reads literal settings in `AddJsonOptions`, `ConfigureHttpJsonOptions` and
  `AddNewtonsoftJson` without running any code.
- **Contract Inspector**: every member, included or excluded, with its JSON name, path and the reason for each
  decision; double-click to go to the declaration.
- **Copy or export** valid JSON. Diagnostics never appear inside the payload.

## Getting started

1. Open a C# file.
2. Choose **View › Other Windows › RealizeJSON**, or right-click in the editor and choose **Preview as JSON**
   (Ctrl+Shift+Alt+J).
3. Move the caret between types; the preview follows. Use **Pin** to keep one type while you navigate.

## Static preview, honestly labeled

Values are synthetic samples, not captured runtime output. Anything that depends on runtime data, custom converters or
unknown configuration is marked as *Estimated*, *Conditional*, *Unsupported* or *Unresolved*.

## Privacy

RealizeJSON runs entirely inside Visual Studio. It collects no telemetry, makes no network requests, needs no account,
and never builds, loads or executes your project's code.

## Requirements

Visual Studio 2022 version 17.8 or later (x64 or Arm64) with C# language services.

## Open source

RealizeJSON is open source under the Apache License 2.0, by Kasadara Technology Solutions Pvt Ltd.
[Source code, documentation and issue tracker](https://github.com/Kasadara-Technology-Solutions-Pvt-Ltd/RealizeJSON).
