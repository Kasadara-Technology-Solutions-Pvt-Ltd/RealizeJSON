namespace RealizeJSON.Core;

/// <summary>A diagnostic reported outside the JSON payload.</summary>
/// <param name="Id">Stable identifier, e.g. <c>RJ1001</c>.</param>
/// <param name="Severity">Severity.</param>
/// <param name="Message">Human-readable message (never contains source text beyond symbol names).</param>
/// <param name="JsonPath">JSON path affected, if any.</param>
/// <param name="MemberPath">CLR member path affected, if any.</param>
/// <param name="Classification">Classification implied by the diagnostic.</param>
public sealed record PreviewDiagnostic(
    string Id,
    DiagnosticSeverity Severity,
    string Message,
    string? JsonPath = null,
    string? MemberPath = null,
    Classification Classification = Classification.Resolved);
