using System;
using System.Collections.Generic;
using System.Globalization;
using RealizeJSON.Core;

namespace RealizeJSON.Diagnostics;

/// <summary>Descriptor of a RealizeJSON diagnostic.</summary>
public sealed record DiagnosticDescriptor(string Id, string Title, DiagnosticSeverity Severity, Classification Classification, string MessageFormat);

/// <summary>Stable diagnostic identifiers. IDs are never reused.</summary>
public static class DiagnosticIds
{
    public const string NoTypeAtCaret = "RJ0001";
    public const string UnsupportedLanguage = "RJ0002";
    public const string UnresolvedSymbol = "RJ0003";
    public const string OpenGenericParameter = "RJ0004";
    public const string AnalysisFailed = "RJ0005";
    public const string SourceHasErrors = "RJ0006";
    public const string NoDocument = "RJ0007";
    public const string UnknownConverter = "RJ1001";
    public const string ConditionalMember = "RJ1002";
    public const string DuplicateJsonName = "RJ1003";
    public const string UnsupportedType = "RJ1004";
    public const string UnresolvedRuntimeType = "RJ1005";
    public const string ExtensionDataEstimated = "RJ1006";
    public const string InvalidExtensionData = "RJ1007";
    public const string PotentialCycle = "RJ1008";
    public const string DepthLimit = "RJ1009";
    public const string NodeLimit = "RJ1010";
    public const string OutputSizeLimit = "RJ1011";
    public const string UnsupportedDictionaryKey = "RJ1012";
    public const string InvalidIgnoreCondition = "RJ1013";
    public const string UnknownConfiguration = "RJ1014";
    public const string PolymorphicRuntimeType = "RJ1015";
    public const string UnsupportedMultiDimensionalArray = "RJ1016";
    public const string NonPublicJsonInclude = "RJ1017";
    public const string SensitiveMemberHeuristic = "RJ1018";
    public const string RequiredMemberNull = "RJ1019";
    public const string DiscriminatorConflict = "RJ1020";
    public const string ConfigurationAmbiguous = "RJ1021";
    public const string SerializationCallbacks = "RJ1022";
    public const string ConstructorMetadata = "RJ1023";
    public const string ValueTupleFields = "RJ1024";
    public const string UnsupportedSerializerOutput = "RJ1025";
    public const string PolymorphicVariantUnknown = "RJ1026";
}

/// <summary>Catalog of all diagnostics with message templates.</summary>
public static class DiagnosticCatalog
{
    private static readonly Dictionary<string, DiagnosticDescriptor> Descriptors = Build();

    public static IReadOnlyCollection<DiagnosticDescriptor> All => Descriptors.Values;

    public static DiagnosticDescriptor Get(string id) =>
        Descriptors.TryGetValue(id, out var d) ? d : throw new ArgumentOutOfRangeException(nameof(id), id, "Unknown diagnostic id");

    /// <summary>Creates a diagnostic from the catalog template.</summary>
    public static PreviewDiagnostic Create(string id, string? jsonPath, string? memberPath, params object[] args)
    {
        var d = Get(id);
        var message = args.Length == 0 ? d.MessageFormat : string.Format(CultureInfo.InvariantCulture, d.MessageFormat, args);
        return new PreviewDiagnostic(d.Id, d.Severity, message, jsonPath, memberPath, d.Classification);
    }

    private static Dictionary<string, DiagnosticDescriptor> Build()
    {
        var list = new[]
        {
            D(DiagnosticIds.NoTypeAtCaret, "No type at caret", DiagnosticSeverity.Info, Classification.Unresolved, "Place the caret on a class, record, struct, interface or enum declaration or reference."),
            D(DiagnosticIds.UnsupportedLanguage, "Unsupported language", DiagnosticSeverity.Info, Classification.Unsupported, "RealizeJSON previews C# types only. The active document is not a C# document in a loaded project."),
            D(DiagnosticIds.UnresolvedSymbol, "Unresolved symbol", DiagnosticSeverity.Warning, Classification.Unresolved, "Type '{0}' could not be resolved. Check project references and restore state."),
            D(DiagnosticIds.OpenGenericParameter, "Open generic parameter", DiagnosticSeverity.Warning, Classification.Unresolved, "Type parameter '{0}' is not bound to a concrete type; its value is shown as null. Select a constructed type to preview it."),
            D(DiagnosticIds.AnalysisFailed, "Analysis failed", DiagnosticSeverity.Error, Classification.Unsupported, "Analysis failed: {0}. The last valid preview is retained."),
            D(DiagnosticIds.SourceHasErrors, "Source has errors", DiagnosticSeverity.Warning, Classification.Estimated, "The source currently has {0} syntax error(s); the preview may be stale or partial."),
            D(DiagnosticIds.NoDocument, "No active document", DiagnosticSeverity.Info, Classification.Unresolved, "Open a C# document to preview its types."),
            D(DiagnosticIds.UnknownConverter, "Unknown converter", DiagnosticSeverity.Warning, Classification.Unsupported, "Converter '{0}' cannot be modeled statically. The value is shown without the converter; actual output may differ."),
            D(DiagnosticIds.ConditionalMember, "Conditional member", DiagnosticSeverity.Info, Classification.Conditional, "Inclusion of '{0}' depends on runtime code ({1}); it is shown as included."),
            D(DiagnosticIds.DuplicateJsonName, "Duplicate JSON name", DiagnosticSeverity.Error, Classification.Unsupported, "JSON name '{0}' is produced by more than one member ({1}). The serializer throws at runtime; only the first member is shown."),
            D(DiagnosticIds.UnsupportedType, "Unsupported type", DiagnosticSeverity.Warning, Classification.Unsupported, "Type '{0}' is not supported by {1}: {2}. A null placeholder is shown."),
            D(DiagnosticIds.UnresolvedRuntimeType, "Runtime type unknown", DiagnosticSeverity.Info, Classification.Unresolved, "Declared type '{0}' does not determine the JSON shape; the runtime value decides it. {1}"),
            D(DiagnosticIds.ExtensionDataEstimated, "Extension data", DiagnosticSeverity.Info, Classification.Conditional, "Extension data member '{0}' is flattened into the parent object at runtime; its entries are unknown statically and are not shown."),
            D(DiagnosticIds.InvalidExtensionData, "Invalid extension data", DiagnosticSeverity.Warning, Classification.Unsupported, "Extension data member '{0}' has an unsupported type '{1}'; the serializer rejects it at runtime."),
            D(DiagnosticIds.PotentialCycle, "Potential cycle", DiagnosticSeverity.Warning, Classification.Conditional, "Type '{0}' references itself through the current path. {1}"),
            D(DiagnosticIds.DepthLimit, "Depth limit reached", DiagnosticSeverity.Warning, Classification.Estimated, "Maximum preview depth ({0}) reached; deeper values are omitted. The preview is incomplete."),
            D(DiagnosticIds.NodeLimit, "Node limit reached", DiagnosticSeverity.Warning, Classification.Estimated, "Maximum node count ({0}) reached; remaining values are omitted. The preview is incomplete."),
            D(DiagnosticIds.OutputSizeLimit, "Output size limit reached", DiagnosticSeverity.Warning, Classification.Estimated, "Maximum output size ({0} bytes) reached; remaining content is omitted. The preview is incomplete."),
            D(DiagnosticIds.UnsupportedDictionaryKey, "Unsupported dictionary key", DiagnosticSeverity.Warning, Classification.Unsupported, "Dictionary key type '{0}' is not supported by {1}; entries are not shown."),
            D(DiagnosticIds.InvalidIgnoreCondition, "Invalid ignore condition", DiagnosticSeverity.Warning, Classification.Unsupported, "'{0}' cannot be applied to value-type member '{1}'; System.Text.Json throws at runtime."),
            D(DiagnosticIds.UnknownConfiguration, "Unknown configuration", DiagnosticSeverity.Warning, Classification.Unresolved, "{0} cannot be evaluated statically; the preview ignores it."),
            D(DiagnosticIds.PolymorphicRuntimeType, "Polymorphic member", DiagnosticSeverity.Info, Classification.Conditional, "'{0}' is polymorphic; the declared-type view is shown. Known variants: {1}."),
            D(DiagnosticIds.UnsupportedMultiDimensionalArray, "Multi-dimensional array", DiagnosticSeverity.Warning, Classification.Unsupported, "Multi-dimensional array '{0}' is not supported by System.Text.Json; serialization throws at runtime."),
            D(DiagnosticIds.NonPublicJsonInclude, "Non-public [JsonInclude]", DiagnosticSeverity.Warning, Classification.Unsupported, "[JsonInclude] on non-public member '{0}' requires System.Text.Json 8.0 or later; earlier versions throw."),
            D(DiagnosticIds.SensitiveMemberHeuristic, "Possibly sensitive member (heuristic)", DiagnosticSeverity.Info, Classification.Resolved, "Member '{0}' is serialized and its name suggests sensitive data. Heuristic only; not a security assessment."),
            D(DiagnosticIds.RequiredMemberNull, "Required member is null", DiagnosticSeverity.Warning, Classification.Conditional, "'{0}' is marked required and the modeled value is null; Newtonsoft.Json throws on serialization."),
            D(DiagnosticIds.DiscriminatorConflict, "Discriminator conflict", DiagnosticSeverity.Error, Classification.Unsupported, "Discriminator configuration conflict on '{0}': {1}."),
            D(DiagnosticIds.ConfigurationAmbiguous, "Ambiguous configuration", DiagnosticSeverity.Warning, Classification.Unresolved, "{0} JSON configurations were detected in the project; select one in the profile settings. Using: {1}."),
            D(DiagnosticIds.SerializationCallbacks, "Serialization callbacks", DiagnosticSeverity.Info, Classification.Conditional, "Type '{0}' defines serialization callbacks ({1}); side effects on output are not modeled."),
            D(DiagnosticIds.ConstructorMetadata, "Constructor metadata", DiagnosticSeverity.Info, Classification.Resolved, "'{0}' declares a JSON constructor; it is deserialization metadata and is never invoked."),
            D(DiagnosticIds.ValueTupleFields, "ValueTuple fields", DiagnosticSeverity.Info, Classification.Resolved, "'{0}' is a ValueTuple whose elements are fields; System.Text.Json writes an empty object unless IncludeFields is enabled. Tuple element names are not serialized."),
            D(DiagnosticIds.UnsupportedSerializerOutput, "Not valid JSON", DiagnosticSeverity.Warning, Classification.Unsupported, "'{0}' produces non-standard output ({1}); a placeholder is shown."),
            D(DiagnosticIds.PolymorphicVariantUnknown, "Unknown variant", DiagnosticSeverity.Warning, Classification.Unresolved, "Polymorphic variant '{0}' is not configured for '{1}'; the declared-type view is shown."),
        };

        var map = new Dictionary<string, DiagnosticDescriptor>(StringComparer.Ordinal);
        foreach (var d in list)
        {
            map.Add(d.Id, d);
        }

        return map;
    }

    private static DiagnosticDescriptor D(string id, string title, DiagnosticSeverity severity, Classification classification, string format) =>
        new(id, title, severity, classification, format);
}
