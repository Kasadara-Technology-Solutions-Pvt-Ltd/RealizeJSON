using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using RealizeJSON.CodeAnalysis;
using RealizeJSON.Core;
using RealizeJSON.Core.Contracts;
using RealizeJSON.Core.Json;
using RealizeJSON.Diagnostics;
using RealizeJSON.SampleData;

namespace RealizeJSON.Serialization;

/// <summary>Configuration detection mode.</summary>
public enum ConfigurationMode
{
    /// <summary>Detect statically analyzable project configuration.</summary>
    Detect,

    /// <summary>Use only the selected preset and manual overrides.</summary>
    Off,
}

/// <summary>Input of one preview analysis.</summary>
public sealed record PreviewRequest(Document Document, int Position)
{
    /// <summary>Pinned type; when set, the caret position is ignored.</summary>
    public TypeKey? PinnedType { get; init; }

    /// <summary>Pinned type as a <see cref="TypeKey"/> string (resolved by matching candidates or parsing).</summary>
    public string? PinnedTypeKeyText { get; init; }

    /// <summary>Explicitly selected candidate key (from the type selector).</summary>
    public string? SelectedCandidateKey { get; init; }

    public SerializerKind Serializer { get; init; } = SerializerKind.SystemTextJson;

    public ProfilePreset Preset { get; init; } = ProfilePreset.LibraryDefaults;

    public ProfileOverrides Overrides { get; init; } = ProfileOverrides.None;

    public ConfigurationMode ConfigurationMode { get; init; } = ConfigurationMode.Detect;

    /// <summary>Index of the selected detected configuration when several exist.</summary>
    public int SelectedConfiguration { get; init; }

    public PreviewOptions Options { get; init; } = new();

    /// <summary>Report member names that look sensitive (heuristic).</summary>
    public bool SensitiveNameWarnings { get; init; } = true;
}

/// <summary>
/// Orchestrates type selection, contract resolution, sample generation and JSON emission. Thread-safe; holds no
/// compilations or semantic models between calls.
/// </summary>
public sealed class PreviewEngine
{
    private readonly Dictionary<SerializerKind, IRuleProvider> _providers;
    private readonly JsonConfigurationDetector _detector = new();

    public PreviewEngine(IEnumerable<IRuleProvider> providers)
    {
        _providers = providers.ToDictionary(p => p.Serializer);
    }

    /// <summary>Clears caches (solution close).</summary>
    public void ClearCaches() => _detector.Clear();

    public async Task<PreviewResult> GenerateAsync(PreviewRequest request, CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        var document = request.Document;
        if (!string.Equals(document.Project.Language, LanguageNames.CSharp, StringComparison.Ordinal))
        {
            return PreviewResult.Empty(PreviewState.Unsupported, DiagnosticCatalog.Create(DiagnosticIds.UnsupportedLanguage, null, null));
        }

        if (!_providers.TryGetValue(request.Serializer, out var provider))
        {
            throw new InvalidOperationException("No rule provider registered for " + request.Serializer);
        }

        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        var model = await document.GetSemanticModelAsync(cancellationToken).ConfigureAwait(false);
        if (root is null || model is null)
        {
            return PreviewResult.Empty(PreviewState.Unsupported, DiagnosticCatalog.Create(DiagnosticIds.UnsupportedLanguage, null, null));
        }

        var syntaxErrors = root.GetDiagnostics().Count(d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error);
        var compilation = model.Compilation;

        // 1. Type selection.
        var location = TypeLocator.Locate(model, root, request.Position, cancellationToken);
        ITypeSymbol? selected = null;
        if (request.PinnedType is not null)
        {
            selected = request.PinnedType.Resolve(compilation);
            if (selected is null)
            {
                return PreviewResult.Empty(PreviewState.Unsupported, DiagnosticCatalog.Create(DiagnosticIds.UnresolvedSymbol, null, null, request.PinnedType.MetadataName)) with
                {
                    SourceHasErrors = syntaxErrors > 0,
                };
            }
        }
        else if (request.PinnedTypeKeyText is not null)
        {
            selected = location.Candidates.FirstOrDefault(c => c.Key?.ToString() == request.PinnedTypeKeyText)?.Symbol
                ?? TypeKey.Parse(request.PinnedTypeKeyText)?.Resolve(compilation);
            if (selected is null)
            {
                return PreviewResult.Empty(PreviewState.Unsupported, DiagnosticCatalog.Create(DiagnosticIds.UnresolvedSymbol, null, null, request.PinnedTypeKeyText)) with
                {
                    SourceHasErrors = syntaxErrors > 0,
                    SelectedTypeKey = request.PinnedTypeKeyText,
                };
            }
        }
        else if (request.SelectedCandidateKey is not null)
        {
            selected = location.Candidates.FirstOrDefault(c => c.Key?.ToString() == request.SelectedCandidateKey)?.Symbol;
        }

        selected ??= location.Primary?.Symbol;
        var candidates = location.Candidates
            .Where(c => c.Key is not null)
            .Select(c => new CandidateInfo(c.Key!.ToString(), c.Display, c.Reason.ToString()))
            .ToList();

        if (selected is null)
        {
            var empty = PreviewResult.Empty(PreviewState.Empty, DiagnosticCatalog.Create(DiagnosticIds.NoTypeAtCaret, null, null));
            return Copy(empty, syntaxErrors, location, candidates, document);
        }

        // 2. Profile resolution (manual > detected > preset).
        DetectedConfiguration? detected = null;
        var diagnostics = new List<PreviewDiagnostic>();
        string? configurationSummary = null;
        if (request.ConfigurationMode == ConfigurationMode.Detect)
        {
            var detection = await _detector.DetectAsync(document.Project, cancellationToken).ConfigureAwait(false);
            var matching = detection.Configurations.Where(c => c.Serializer == request.Serializer).ToList();
            if (matching.Count > 0)
            {
                detected = matching[Math.Max(0, Math.Min(request.SelectedConfiguration, matching.Count - 1))];
                configurationSummary = "Detected: " + detected.Source;
                if (matching.Count > 1)
                {
                    diagnostics.Add(DiagnosticCatalog.Create(DiagnosticIds.ConfigurationAmbiguous, null, null, matching.Count, detected.Source));
                }

                foreach (var unknown in detected.UnknownSettings)
                {
                    diagnostics.Add(DiagnosticCatalog.Create(DiagnosticIds.UnknownConfiguration, null, null, unknown + " (" + detected.Source + ")"));
                }
            }
            else if (detection.ReferencesAspNetCore && request.Preset == ProfilePreset.LibraryDefaults)
            {
                configurationSummary = "Project references ASP.NET Core; consider the ASP.NET Core web defaults preset.";
            }
        }

        var profile = ProfileResolver.Resolve(request.Serializer, request.Preset, detected, request.Overrides);

        // 3. Contract, samples, JSON.
        var contract = provider.BuildContract(selected, compilation, profile, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        var options = request.Options.Normalize();
        var samples = SampleValueGenerator.Generate(contract, options);
        var tree = JsonEmitter.Emit(contract, samples.Root, out var memberCount, out var emitDiagnostics);
        var written = JsonTextWriter.Write(tree, profile.WriteIndented, contract.Formatter.Escaping, options.MaxBytes);

        diagnostics.AddRange(contract.Diagnostics);
        diagnostics.AddRange(samples.Diagnostics);
        diagnostics.AddRange(emitDiagnostics);
        if (written.Truncated)
        {
            diagnostics.Add(DiagnosticCatalog.Create(DiagnosticIds.OutputSizeLimit, written.TruncatedPaths.FirstOrDefault(), null, options.MaxBytes));
        }

        if (syntaxErrors > 0)
        {
            diagnostics.Add(DiagnosticCatalog.Create(DiagnosticIds.SourceHasErrors, null, null, syntaxErrors));
        }

        if (contract.Root is ObjectContract { ConstructorMetadata: { } ctor } rootObject)
        {
            diagnostics.Add(DiagnosticCatalog.Create(DiagnosticIds.ConstructorMetadata, null, ctor, rootObject.Type.DisplayName));
        }

        if (request.SensitiveNameWarnings)
        {
            foreach (var member in InspectorBuilder.AllIncludedMembers(contract.Root).Where(m => SensitiveNameHeuristic.IsPotentiallySensitive(m.ClrName)).Take(10))
            {
                diagnostics.Add(DiagnosticCatalog.Create(DiagnosticIds.SensitiveMemberHeuristic, null, member.ClrName, member.ClrName));
            }
        }

        var incomplete = samples.Incomplete || written.Truncated;
        var classification = diagnostics.Count == 0 ? Classification.Resolved : diagnostics.Max(d => d.Classification);
        if (contract.Root.Classification > classification)
        {
            classification = contract.Root.Classification;
        }

        var state = contract.Root is UnsupportedContract ? PreviewState.Unsupported
            : incomplete ? PreviewState.Partial
            : PreviewState.Live;

        var variants = new List<VariantInfo>();
        if (contract.Root is ObjectContract { Polymorphism: { } poly })
        {
            variants.Add(new VariantInfo(string.Empty, "Declared type: " + contract.Root.Type.DisplayName, poly.BaseDiscriminatorJson));
            variants.AddRange(poly.Variants.Select(v => new VariantInfo(v.VariantKey, v.Type.DisplayName, v.DiscriminatorJson)));
        }

        var label = "Static preview · " + (options.Mode == SampleMode.SampleValues ? "Sample values" : "CLR defaults") + " · " +
                    (request.Serializer == SerializerKind.SystemTextJson ? "System.Text.Json" : "Newtonsoft.Json") +
                    (incomplete ? " · Incomplete" : string.Empty);

        stopwatch.Stop();
        return new PreviewResult
        {
            State = state,
            Json = written.Text,
            IsComplete = !incomplete,
            Diagnostics = Deduplicate(diagnostics),
            Classification = classification,
            Label = label,
            PropertyCount = memberCount,
            SelectedType = selected.ToIdentity(),
            SelectedTypeKey = TypeKey.Create(selected)?.ToString(),
            ProjectContext = DescribeProject(document.Project),
            Profile = profile,
            Inspector = InspectorBuilder.Build(contract.Root),
            LineMap = written.LineMap,
            Tree = tree,
            Candidates = candidates,
            Variants = variants,
            ContributingFiles = contract.ContributingFiles.ToArray(),
            DeclarationSpanStart = location.EnclosingDeclarationSpan?.Start,
            DeclarationSpanEnd = location.EnclosingDeclarationSpan?.End,
            DocumentPath = document.FilePath,
            SourceHasErrors = syntaxErrors > 0,
            Duration = stopwatch.Elapsed,
            ConfigurationSummary = configurationSummary,
        };
    }

    /// <summary>Resolves the type key at a position without building a contract (cheap caret relevance check).</summary>
    public static async Task<string?> GetTypeKeyAtAsync(Document document, int position, CancellationToken cancellationToken)
    {
        var location = await TypeLocator.LocateAsync(document, position, cancellationToken).ConfigureAwait(false);
        return location.Primary?.Key?.ToString();
    }

    private static PreviewResult Copy(PreviewResult r, int errors, TypeLocation location, IReadOnlyList<CandidateInfo> candidates, Document document) => new()
    {
        State = r.State,
        Diagnostics = r.Diagnostics,
        Classification = r.Classification,
        Label = r.Label,
        Candidates = candidates,
        SourceHasErrors = errors > 0,
        DeclarationSpanStart = location.EnclosingDeclarationSpan?.Start,
        DeclarationSpanEnd = location.EnclosingDeclarationSpan?.End,
        DocumentPath = document.FilePath,
        ProjectContext = DescribeProject(document.Project),
    };

    /// <summary>Project and target framework context, e.g. "Api (net8.0)".</summary>
    public static string DescribeProject(Project project)
    {
        // Multi-targeted projects are already named "Api (net8.0)" by Visual Studio.
        var name = project.Name;
        if (name.IndexOf('(') >= 0)
        {
            return name;
        }

        var framework = InferTargetFramework(project.ParseOptions?.PreprocessorSymbolNames ?? Enumerable.Empty<string>());
        return framework is null ? name : name + " (" + framework + ")";
    }

    /// <summary>Infers the target framework moniker from compiler preprocessor symbols (NET8_0, NETSTANDARD2_0, NET472…).</summary>
    public static string? InferTargetFramework(IEnumerable<string> symbols)
    {
        var list = symbols.ToList();
        var modern = list.Where(s => System.Text.RegularExpressions.Regex.IsMatch(s, @"^NET\d+_\d+$"))
            .Select(s => s.Substring(3).Split('_'))
            .Select(p => (Major: int.Parse(p[0], System.Globalization.CultureInfo.InvariantCulture), Minor: int.Parse(p[1], System.Globalization.CultureInfo.InvariantCulture)))
            .Where(v => v.Major >= 5)
            .OrderByDescending(v => v.Major).ThenByDescending(v => v.Minor)
            .FirstOrDefault();
        if (modern.Major > 0)
        {
            return "net" + modern.Major.ToString(System.Globalization.CultureInfo.InvariantCulture) + "." + modern.Minor.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        var standard = list.Where(s => System.Text.RegularExpressions.Regex.IsMatch(s, @"^NETSTANDARD\d_\d$")).OrderByDescending(s => s, StringComparer.Ordinal).FirstOrDefault();
        if (standard is not null)
        {
            return "netstandard" + standard.Substring(11).Replace('_', '.');
        }

        var framework = list.Where(s => System.Text.RegularExpressions.Regex.IsMatch(s, @"^NET4\d{1,2}$")).OrderByDescending(s => s, StringComparer.Ordinal).FirstOrDefault();
        if (framework is not null)
        {
            return "net" + framework.Substring(3);
        }

        var core = list.Where(s => System.Text.RegularExpressions.Regex.IsMatch(s, @"^NETCOREAPP\d_\d$")).OrderByDescending(s => s, StringComparer.Ordinal).FirstOrDefault();
        return core is null ? null : "netcoreapp" + core.Substring(10).Replace('_', '.');
    }

    private static IReadOnlyList<PreviewDiagnostic> Deduplicate(IEnumerable<PreviewDiagnostic> diagnostics)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        return diagnostics
            .Where(d => seen.Add(d.Id + "|" + d.Message + "|" + d.JsonPath + "|" + d.MemberPath))
            .OrderByDescending(d => d.Severity)
            .ToList();
    }
}
