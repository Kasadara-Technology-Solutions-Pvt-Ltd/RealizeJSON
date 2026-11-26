![RealizeJSON](https://raw.githubusercontent.com/Kasadara-Technology-Solutions-Pvt-Ltd/RealizeJSON/main/assets/branding/png/realizejson-wordmark-light.png)

# RealizeJSON: Live JSON Serialization Preview

**See your JSON before your code runs.**

You write a DTO, add a `[JsonPropertyName]`, mark a secret `[JsonIgnore]`, and then you start the API, call the
endpoint and inspect the response to check what actually went over the wire. RealizeJSON shows you instead, in
Visual Studio, while you type.

Put the caret on a class or record and RealizeJSON renders the JSON that **System.Text.Json** or **Newtonsoft.Json**
would produce for it. It applies each serializer's own rules for naming, ignore conditions, ordering, enums, dictionaries
and polymorphism. You don't need to build, run or set a breakpoint.

---

## From C# to JSON as you type

```csharp
public class CustomerDTO
{
    public int Id { get; set; }

    [JsonPropertyName("name")]
    public string FullName { get; set; } = string.Empty;

    public bool IsActive { get; set; }

    [JsonIgnore]
    public string Password { get; set; } = string.Empty;
}
```

With **System.Text.Json** and **ASP.NET Core web defaults**, RealizeJSON shows:

```json
{
  "id": 101,
  "name": "Sample Customer",
  "isActive": true
}
```

`FullName` is renamed, `Password` is left out, and everything else is camel-cased. If you add a property, the preview
updates before you save the file.

---

## Why developers use it

- **Catch contract mistakes early.** Spot a wrong property name, a leaked field or a missing `[JsonIgnore]` while
  you're writing the class, before it reaches a client.
- **Compare serializers.** Switch between System.Text.Json and Newtonsoft.Json and see how the same type comes out in
  each, which is handy during a migration.
- **Make example payloads for docs and tests.** Copy or export clean, valid JSON for API docs, Postman collections,
  test fixtures or a teammate's question.
- **Understand each property.** The Contract Inspector tells you why each property is included, renamed or left out.

---

## Features

### Live preview at the caret
- Follows the caret across classes, records, structs, nested and partial types, and generic types such as
  `CreateDTO<CaseDTO>`.
- Uses the code in the editor, including unsaved changes. Nothing is built.
- **Pin** keeps one type in view while you work in other files.
- When there are syntax errors, the last good preview stays visible and is marked **Stale**.

### Two serializers, three presets
- **System.Text.Json** and **Newtonsoft.Json**.
- **Library defaults**, **ASP.NET Core web defaults** or **Custom**. Library defaults never quietly assume camelCase.
- Quick toggles for naming policy, enums as strings, indented or compact output, and sample collection size (0–5).

### Attributes behave as they do at runtime
**System.Text.Json:** `JsonPropertyName`, `JsonIgnore` (every condition), `JsonInclude`, `JsonPropertyOrder`,
`JsonRequired`, `JsonExtensionData`, `JsonNumberHandling`, `JsonConverter`, `JsonPolymorphic` / `JsonDerivedType`,
`JsonConstructor`.

**Newtonsoft.Json:** `JsonProperty`, `JsonIgnore`, `JsonObject` member modes, `DefaultValue`, `ShouldSerialize*`,
`EnumMember`.

**Data contracts:** `DataContract`, `DataMember`, `IgnoreDataMember`.

Attributes are recognized by their full .NET type (namespace included), not by name alone, so your own
`JsonIgnoreAttribute` is not mistaken for the real one.

### Reads your project's JSON settings without running them
Finds plain literal settings in `AddJsonOptions`, `ConfigureHttpJsonOptions`, `Configure<JsonOptions>`,
`AddNewtonsoftJson` and `JsonConvert.DefaultSettings`. Settings you choose in RealizeJSON take precedence, then
settings found in your code, then the preset.

### Contract Inspector
Lists every member, included or excluded, with its JSON name, JSON path, CLR type, where the rule came from and a
plain-language explanation. Double-click a row to go to its declaration. Members with names that look sensitive are
flagged.

### Polymorphism
Shows System.Text.Json type discriminators, with a selector to preview each derived type.

### Copy and export
**Copy** and **Export** always produce valid JSON. Warnings appear in the Diagnostics tab, never inside the JSON. If
a preview was cut short by a size limit, you're asked to confirm before copying it.

### Feels like part of Visual Studio
A dockable tool window that follows your Visual Studio theme and colors JSON with your editor's colors. Open it with
**View › Other Windows › RealizeJSON**, or right-click in the editor and choose **Preview as JSON** (**Ctrl+Shift+Alt+J**, rebindable).

---

## What the preview is, and isn't

RealizeJSON builds a **static preview with sample values** from your source code. It shows the shape your
serializer would produce, not data captured at runtime. Every preview carries a label such as
`Static preview · Sample values · System.Text.Json`. Anything it can't know for certain is marked with one of these
labels:

| Label | Meaning |
|---|---|
| **Estimated** | Modeled with a reasonable assumption |
| **Conditional** | Depends on runtime values (for example, ignore-when-default) |
| **Unsupported** | Not modeled yet, such as custom converters, contract resolvers or Newtonsoft `TypeNameHandling` |
| **Unresolved** | The type or setting couldn't be determined from source |

---

## Private by design

- **No telemetry.** No account, no cloud service, no network requests.
- **Your code stays local.** Source and payloads never leave your machine.
- **Your code is never run.** RealizeJSON doesn't build, load or execute your project. Constructors, property getters,
  converters and `Program.cs` are never invoked, and automated tests check this.

---

## Get started

1. Install RealizeJSON and open a C# project.
2. Open **View › Other Windows › RealizeJSON**, or right-click in a class and choose **Preview as JSON**.
3. Put the caret in any class or record. That's it.

**Requirements:** Visual Studio 2026 (version 18.0 or later) on Windows x64, with C# language support (included with any .NET workload). Visual Studio 2022 support
is planned once it has been validated.

Found a problem or have an idea? Open an issue on
[GitHub](https://github.com/Kasadara-Technology-Solutions-Pvt-Ltd/RealizeJSON/issues).

---

## Open source

RealizeJSON is free and open source under the **Apache License 2.0**, built by
**Kasadara Technology Solutions Pvt Ltd**. Source code, documentation and the issue tracker are on
[GitHub](https://github.com/Kasadara-Technology-Solutions-Pvt-Ltd/RealizeJSON). Contributions are welcome.
