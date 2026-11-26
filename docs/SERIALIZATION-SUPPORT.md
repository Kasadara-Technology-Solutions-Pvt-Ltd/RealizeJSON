# Serialization support matrix

Rules modeled: **System.Text.Json 8.0+** (reflection serializer semantics; version-gated features noted) and
**Newtonsoft.Json 13.0** (`DefaultContractResolver`). Differential evidence was produced against System.Text.Json
shipped with .NET 10.0.15 and Newtonsoft.Json 13.0.4 on Linux x64.

Status legend: **Diff** = verified byte-for-byte against the real serializer by `DifferentialTests`;
**Unit** = verified by rule/unit tests; **Diag** = reported through a diagnostic with a dedicated test;
**Doc** = documented only (no automated test).

## Members and attributes

| Scenario | System.Text.Json | Newtonsoft.Json | Status |
|---|---|---|---|
| Public instance properties with public getter | Included | Included | Diff |
| Static members, indexers, write-only properties | Excluded | Excluded | Diff, Unit |
| Non-public property/getter | Excluded unless `[JsonInclude]` (8.0+; RJ1017 below 8.0) | Excluded unless `[JsonProperty]`/`[JsonRequired]`/`[DataMember]` | Diff |
| Public fields | Excluded unless `IncludeFields` or `[JsonInclude]` | Included | Diff |
| Member order across hierarchy | Most-derived type first; per type properties before fields; `[JsonPropertyOrder]` (default 0), stable | Fields before properties; `Order` (default −1), stable | Diff |
| Overridden properties | Represented once at the override's position | Same | Diff |
| Hidden (`new`) members | Derived member wins; base excluded with explanation | Same | Diff |
| Duplicate JSON names | RJ1003 error (serializer throws); first member shown | Same | Diag |
| `[JsonPropertyName]` / `[JsonProperty("x")]` | Explicit name, naming policy not applied | Explicit name; transformed only with `OverrideSpecifiedNames` | Diff |
| `[JsonIgnore]` (Always / Never / WhenWritingNull / WhenWritingDefault / WhenWriting / WhenReading) | All conditions; `WhenWritingNull` on non-nullable value type → RJ1013 | `[JsonIgnore]`, `[IgnoreDataMember]` (opt-out), `[NonSerialized]` fields | Diff, Unit |
| Global ignore condition | `DefaultIgnoreCondition` (null-only applies to nullable members) | `NullValueHandling`/`DefaultValueHandling` | Diff |
| Per-member null/default handling | — | `JsonProperty.NullValueHandling`, `DefaultValueHandling`, `[DefaultValue]`, `DataMember.EmitDefaultValue` | Diff |
| Read-only members | Serialized; `IgnoreReadOnlyProperties`/`IgnoreReadOnlyFields` honored | Serialized | Diff |
| Computed getters (`=> …`) | Included; value marked *Estimated* (getter never invoked) | Same | Unit |
| `[JsonRequired]`, C# `required` | Deserialization metadata only | `[JsonRequired]`/`Required.Always` null at write → RJ1019 | Unit |
| `[JsonExtensionData]` | Entries unknown statically → not fabricated, RJ1006; invalid type → RJ1007 | Same | Diag |
| `[JsonNumberHandling(WriteAsString)]` / global `WriteAsString` | Numbers quoted (incl. collection elements) | n/a | Diff |
| `[JsonConstructor]` | Metadata only, never invoked (RJ1023) | Same | Unit |
| `[JsonObject(MemberSerialization.OptIn/OptOut/Fields)]` | n/a | Supported; Fields mode includes compiler-generated backing fields | Diff (OptIn), Doc (Fields) |
| `[DataContract]`/`[DataMember(Name, Order, EmitDefaultValue)]` | Ignored (STJ does not honor them) | Opt-in contract with correct precedence | Diff |
| `ShouldSerializeX()` / `XSpecified` | n/a | *Conditional*, shown as included, RJ1002 | Diag |
| Serialization callbacks (`IJsonOnSerializing`, `[OnSerializing]`) | *Conditional*, RJ1022 | *Conditional*, RJ1022 | Doc |
| Attribute recognition | Fully qualified symbol identity | Fully qualified symbol identity | Unit |

## Naming

| Policy | System.Text.Json | Newtonsoft.Json | Status |
|---|---|---|---|
| Unchanged (declared names) | Yes (library default) | Yes (library default) | Diff |
| camelCase | `JsonNamingPolicy.CamelCase` (web default) | `CamelCaseNamingStrategy` (`AddNewtonsoftJson` default) | Diff + 28 names vs. real algorithm |
| snake_case / kebab-case | `SnakeCaseLower`, `KebabCaseLower` (8.0+) | `SnakeCaseNamingStrategy`, `KebabCaseNamingStrategy` | Diff + algorithm tests |
| SNAKE_CASE / KEBAB-CASE | `SnakeCaseUpper`, `KebabCaseUpper` | Not available (hidden in UI) | Diff |
| Dictionary keys | `DictionaryKeyPolicy` (string and enum keys only) | `ProcessDictionaryKeys` (all keys) | Diff |
| Custom naming policies/strategies | Reported as unknown (RJ1014) | Same | Unit (detector) |

The two serializers' separator algorithms differ in edge cases (for example `AB1` → `ab1` vs. `a_b1`, `Prop_Name`
kebab → `prop_name` vs. `prop_-name`); both are ported separately.

## Types

| Type | System.Text.Json | Newtonsoft.Json | Status |
|---|---|---|---|
| bool, string, char, integral types, float, double, decimal | Yes (decimal precision preserved) | Yes (floating/decimal values always have a decimal place, e.g. `3.0`) | Diff |
| Half, Int128, UInt128 | Numbers (8.0+) | Half written as string; Int128/UInt128 unsupported (RJ1004) | Diff (Half), Doc (Int128) |
| BigInteger | Unsupported (RJ1004) | Number | Doc |
| DateTime, DateTimeOffset, DateOnly, TimeOnly, TimeSpan, Guid | ISO-8601 / `c` / `D` formats (written unescaped) | Same formats (IsoDateTimeConverter defaults) | Diff |
| Uri, Version, byte[] (Base64) | Strings | Strings | Diff |
| Enums (numbers / strings / flags / undefined values) | `JsonStringEnumConverter` (+ naming), `JsonStringEnumMemberName`, flags `"A, B"` | `StringEnumConverter` (+ naming strategy), `[EnumMember]` | Diff |
| Nullable value types, nullable reference annotations | Annotations are not runtime nulls; explicit null mode available | Same | Diff |
| Arrays, `List<T>`, `IEnumerable<T>`, `IReadOnlyList<T>`, sets | Yes; sets use unique samples | Yes | Diff |
| Multi-dimensional arrays | Unsupported (RJ1016) | Nested arrays | Diag, Diff |
| Dictionaries (`IDictionary<,>`, `IReadOnlyDictionary<,>`) | String, enum and primitive keys; complex keys → RJ1012 | Any key (complex keys estimated via ToString) | Diff, Diag |
| Records, record structs, structs, inheritance, abstract members, partial types | Yes | Yes | Diff |
| Generic, nested generic and constructed types (`CreateDTO<CaseDTO>`) | Yes; open parameters → null + RJ0004 | Same | Diff, Unit |
| ValueTuple | `{}` unless `IncludeFields` (RJ1024) | `Item1…` fields | Diff |
| `Tuple<…>`, `KeyValuePair<,>` | Properties | Properties | Diff |
| `object`, `dynamic` | *Unresolved*; `{}` placeholder, RJ1005 | Same | Diag |
| Interfaces / abstract declared types | Declared members (STJ serializes the declared type) | *Conditional*: runtime type decides, declared members as estimate | Unit |
| JsonElement, JsonNode, JToken, ExpandoObject | *Estimated* `{}` | Same | Unit |
| Pointers, ref structs, delegates, `Type`, `IntPtr`, `Task` | *Unsupported*, null placeholder, RJ1004 (no crash) | Same | Diag |
| Type-level `[JsonConverter]` | Built-in enum converters recognized; others RJ1001, default shape shown with warning | Same; `[TypeConverter]` reported | Diag |

## Polymorphism and references

| Scenario | System.Text.Json | Newtonsoft.Json | Status |
|---|---|---|---|
| `[JsonPolymorphic]`/`[JsonDerivedType]` discriminators | Variant selector; discriminator first; declared view by default | n/a | Diff |
| Discriminator conflicts / invalid derived types | RJ1020 | n/a | Unit |
| Nested polymorphic members | Declared view, RJ1015 lists variants | n/a | Diag |
| `TypeNameHandling` | n/a | **Not modeled**; reported as unknown configuration (never suggested) | Unit (detector) |
| `ReferenceHandler.Preserve` / `PreserveReferencesHandling.Objects` | `$id` on objects and non-array collections (`$values`) | `$id` on all objects including structs | Diff |
| Recursive types / potential cycles | Detected on the traversal path; truncated with RJ1008 explaining runtime behavior per setting; repeated sibling types are not cycles | Same | Unit |

## Samples and limits

| Scenario | Behavior | Status |
|---|---|---|
| Sample values | Deterministic; property-name heuristics; fixed dates/GUIDs; no PII | Unit (determinism over all fixtures) |
| CLR defaults | Synthetic `default(T)` values; constructors/initializers not simulated; root object materialized | Diff |
| Collection count 0–5 (default 1) | Unique set elements and dictionary keys | Diff |
| Depth (8), nodes (2,000), size (256 KiB) | Valid bounded JSON, *Incomplete* label, RJ1009–RJ1011 with paths | Unit |
| Omission rules | Applied to modeled CLR values (`WhenWritingNull`, `WhenWritingDefault`, `[DefaultValue]`) | Diff |
| String escaping | STJ default encoder (HTML-sensitive and non-ASCII escaped); Newtonsoft default escaping | Unit vs. real serializers |

## Known limitations

- Custom converters, contract resolvers, naming policies, `TypeInfoResolver` modifiers and source-generated contract
  customizations are not executed; they are reported, never approximated silently.
- Extension data entries, runtime-derived types (without polymorphism attributes) and computed getter values are
  unknown statically.
- Safe constant-initializer inference (FR-06, P1) is not implemented; CLR-defaults mode never simulates initializers.
- Newtonsoft `TypeNameHandling`, `ItemTypeNameHandling` and `ReferenceLoopHandling.Serialize` are not modeled.
- Version gating is coarse (System.Text.Json 7.0/8.0/9.0 features are recognized when the referenced assembly exposes
  them; the profile version defaults to 8.0).
- Configuration detection is syntactic: values from variables, configuration files or helper methods are reported as
  unknown.
