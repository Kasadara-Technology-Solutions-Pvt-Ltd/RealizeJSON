using System;
using System.Collections.Generic;
using RealizeJSON.Core.Contracts;
using RealizeJSON.Core.Json;

namespace RealizeJSON.Core;

/// <summary>A row of the Serialization Contract Inspector.</summary>
public sealed record InspectorEntry(
    string ClrMember,
    string DeclaringType,
    string? JsonName,
    string ClrType,
    string? JsonPath,
    bool Included,
    string Source,
    string Explanation,
    Classification Classification,
    SourceLocation Location,
    int Depth);

/// <summary>A selectable type candidate.</summary>
public sealed record CandidateInfo(string Key, string Display, string Reason);

/// <summary>A selectable polymorphic variant (empty key = declared-type view).</summary>
public sealed record VariantInfo(string Key, string Display, string? Discriminator);

/// <summary>Immutable result of one preview analysis.</summary>
public sealed record PreviewResult
{
    public PreviewState State { get; init; } = PreviewState.Empty;

    /// <summary>Valid JSON text, or empty when no preview is available.</summary>
    public string Json { get; init; } = string.Empty;

    public bool IsComplete { get; init; } = true;

    public IReadOnlyList<PreviewDiagnostic> Diagnostics { get; init; } = Array.Empty<PreviewDiagnostic>();

    public Classification Classification { get; init; } = Classification.Resolved;

    /// <summary>Persistent preview label, e.g. "Static preview · Sample values · System.Text.Json".</summary>
    public string Label { get; init; } = string.Empty;

    public int PropertyCount { get; init; }

    public TypeIdentity? SelectedType { get; init; }

    /// <summary>Stable key of the selected type (for pinning).</summary>
    public string? SelectedTypeKey { get; init; }

    /// <summary>Project and target framework context, e.g. "Contoso.Api (net8.0)".</summary>
    public string? ProjectContext { get; init; }

    public SerializationProfile? Profile { get; init; }

    public IReadOnlyList<InspectorEntry> Inspector { get; init; } = Array.Empty<InspectorEntry>();

    public IReadOnlyList<JsonLineMapping> LineMap { get; init; } = Array.Empty<JsonLineMapping>();

    public JsonNodeModel? Tree { get; init; }

    public IReadOnlyList<CandidateInfo> Candidates { get; init; } = Array.Empty<CandidateInfo>();

    public IReadOnlyList<VariantInfo> Variants { get; init; } = Array.Empty<VariantInfo>();

    public IReadOnlyCollection<string> ContributingFiles { get; init; } = Array.Empty<string>();

    /// <summary>Span start/end of the enclosing declaration at the caret (for cheap caret relevance checks).</summary>
    public int? DeclarationSpanStart { get; init; }

    public int? DeclarationSpanEnd { get; init; }

    /// <summary>Document that was analyzed (file path), if any.</summary>
    public string? DocumentPath { get; init; }

    public bool SourceHasErrors { get; init; }

    public TimeSpan Duration { get; init; }

    /// <summary>Configuration detection summary, e.g. "Detected: Program.cs AddJsonOptions".</summary>
    public string? ConfigurationSummary { get; init; }

    public static PreviewResult Empty(PreviewState state, PreviewDiagnostic? diagnostic, string label = "") => new()
    {
        State = state,
        Label = label,
        Diagnostics = diagnostic is null ? Array.Empty<PreviewDiagnostic>() : new[] { diagnostic },
        Classification = diagnostic?.Classification ?? Classification.Resolved,
    };
}
