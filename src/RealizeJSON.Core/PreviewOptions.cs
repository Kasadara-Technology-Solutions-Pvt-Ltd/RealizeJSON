using System;

namespace RealizeJSON.Core;

/// <summary>Sample generation and output limits.</summary>
public sealed record PreviewOptions
{
    public const int MaxCollectionCount = 5;

    public SampleMode Mode { get; init; } = SampleMode.SampleValues;

    /// <summary>Number of sample elements per collection (0..5).</summary>
    public int CollectionCount { get; init; } = 1;

    /// <summary>When true, nullable members are shown as null in sample mode (explicit null examples).</summary>
    public bool NullableMembersAsNull { get; init; }

    /// <summary>Use property-name heuristics for sample values.</summary>
    public bool UseNameHeuristics { get; init; } = true;

    public int MaxDepth { get; init; } = 8;

    public int MaxBytes { get; init; } = 256 * 1024;

    public int MaxNodes { get; init; } = 2000;

    /// <summary>Selected polymorphic variant for the root type (metadata name), or null for the declared-type view.</summary>
    public string? PolymorphicVariant { get; init; }

    /// <summary>Returns a copy with all values clamped into supported ranges.</summary>
    public PreviewOptions Normalize() => this with
    {
        CollectionCount = Math.Max(0, Math.Min(MaxCollectionCount, CollectionCount)),
        MaxDepth = Math.Max(1, Math.Min(64, MaxDepth)),
        MaxBytes = Math.Max(1024, Math.Min(16 * 1024 * 1024, MaxBytes)),
        MaxNodes = Math.Max(10, Math.Min(1_000_000, MaxNodes)),
    };
}
