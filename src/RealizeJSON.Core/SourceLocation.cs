namespace RealizeJSON.Core;

/// <summary>Kind of origin for a symbol declaration.</summary>
public enum SourceOrigin
{
    Source = 0,
    Generated = 1,
    Metadata = 2,
}

/// <summary>Location of a declaration for navigation. Lines and columns are zero-based.</summary>
public sealed record SourceLocation(string? FilePath, int Line, int Column, SourceOrigin Origin)
{
    public static SourceLocation MetadataOnly { get; } = new(null, 0, 0, SourceOrigin.Metadata);

    public bool CanNavigate => Origin != SourceOrigin.Metadata && !string.IsNullOrEmpty(FilePath);
}
