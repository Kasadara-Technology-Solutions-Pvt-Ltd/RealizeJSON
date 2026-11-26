using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using RealizeJSON.Core;

namespace RealizeJSON.CodeAnalysis;

/// <summary>Result of project configuration detection.</summary>
public sealed class ConfigurationDetectionResult
{
    public ConfigurationDetectionResult(IReadOnlyList<DetectedConfiguration> configurations, bool referencesAspNetCore, bool referencesNewtonsoft)
    {
        Configurations = configurations;
        ReferencesAspNetCore = referencesAspNetCore;
        ReferencesNewtonsoft = referencesNewtonsoft;
    }

    public static ConfigurationDetectionResult None { get; } = new(Array.Empty<DetectedConfiguration>(), false, false);

    public IReadOnlyList<DetectedConfiguration> Configurations { get; }

    /// <summary>The project references ASP.NET Core assemblies (web defaults may be inferred).</summary>
    public bool ReferencesAspNetCore { get; }

    public bool ReferencesNewtonsoft { get; }
}

/// <summary>
/// Detects statically analyzable JSON configuration (AddJsonOptions, ConfigureHttpJsonOptions, Configure&lt;JsonOptions&gt;,
/// AddNewtonsoftJson, JsonConvert.DefaultSettings). Syntax only: Program.cs, Startup and options delegates are never
/// executed. Per-document results are cached by text version so only edited documents are rescanned.
/// </summary>
public sealed class JsonConfigurationDetector
{
    private const int MaxProjects = 16;

    private static readonly string[] Markers = { "AddJsonOptions", "ConfigureHttpJsonOptions", "JsonOptions", "AddNewtonsoftJson", "DefaultSettings" };

    private readonly ConcurrentDictionary<ProjectId, ProjectCache> _cache = new();

    public async Task<ConfigurationDetectionResult> DetectAsync(Project project, CancellationToken cancellationToken)
    {
        if (_cache.Count > MaxProjects)
        {
            _cache.Clear();
        }

        var cache = _cache.GetOrAdd(project.Id, _ => new ProjectCache());
        var all = new List<DetectedConfiguration>();
        var live = new HashSet<DocumentId>();
        foreach (var document in project.Documents)
        {
            cancellationToken.ThrowIfCancellationRequested();
            live.Add(document.Id);
            var version = document.TryGetTextVersion(out var v) ? v : await document.GetTextVersionAsync(cancellationToken).ConfigureAwait(false);
            if (cache.Documents.TryGetValue(document.Id, out var entry) && entry.Version == version)
            {
                all.AddRange(entry.Results);
                continue;
            }

            var results = await ScanDocumentAsync(document, cancellationToken).ConfigureAwait(false);
            cache.Documents[document.Id] = new DocumentEntry(version, results);
            all.AddRange(results);
        }

        foreach (var stale in cache.Documents.Keys.Where(k => !live.Contains(k)).ToList())
        {
            cache.Documents.TryRemove(stale, out _);
        }

        var referencesAspNetCore = project.MetadataReferences.Any(r => (r.Display ?? string.Empty).IndexOf("Microsoft.AspNetCore", StringComparison.OrdinalIgnoreCase) >= 0);
        var referencesNewtonsoft = project.MetadataReferences.Any(r => (r.Display ?? string.Empty).IndexOf("Newtonsoft.Json", StringComparison.OrdinalIgnoreCase) >= 0);
        return new ConfigurationDetectionResult(all, referencesAspNetCore, referencesNewtonsoft);
    }

    /// <summary>Clears cached results (solution close).</summary>
    public void Clear() => _cache.Clear();

    private static async Task<IReadOnlyList<DetectedConfiguration>> ScanDocumentAsync(Document document, CancellationToken cancellationToken)
    {
        var text = await document.GetTextAsync(cancellationToken).ConfigureAwait(false);
        var content = text.ToString();
        if (!Markers.Any(m => content.IndexOf(m, StringComparison.Ordinal) >= 0))
        {
            return Array.Empty<DetectedConfiguration>();
        }

        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        return root is null ? Array.Empty<DetectedConfiguration>() : Scan(root, document.FilePath ?? document.Name);
    }

    /// <summary>Scans a syntax tree (public for tests).</summary>
    public static IReadOnlyList<DetectedConfiguration> Scan(SyntaxNode root, string fileName)
    {
        var results = new List<DetectedConfiguration>();
        var shortName = System.IO.Path.GetFileName(fileName);

        foreach (var invocation in root.DescendantNodes().OfType<InvocationExpressionSyntax>())
        {
            var name = GetInvokedName(invocation.Expression);
            var isGenericJsonOptions = invocation.Expression is MemberAccessExpressionSyntax { Name: GenericNameSyntax g } &&
                g.Identifier.ValueText == "Configure" && g.TypeArgumentList.Arguments.Any(a => a.ToString().EndsWith("JsonOptions", StringComparison.Ordinal));

            SerializerKind? serializer = name switch
            {
                "AddJsonOptions" or "ConfigureHttpJsonOptions" => SerializerKind.SystemTextJson,
                "AddNewtonsoftJson" => SerializerKind.NewtonsoftJson,
                _ => isGenericJsonOptions ? SerializerKind.SystemTextJson : null,
            };

            if (serializer is null)
            {
                continue;
            }

            var lambda = invocation.ArgumentList.Arguments.SelectMany(a => a.Expression.DescendantNodesAndSelf().OfType<LambdaExpressionSyntax>()).FirstOrDefault();
            var builder = new SettingsBuilder(serializer.Value);
            if (lambda is not null)
            {
                builder.Visit(lambda.Body);
            }

            results.Add(builder.ToConfiguration($"{shortName}: {(isGenericJsonOptions ? "Configure<JsonOptions>" : name)}", Locate(invocation, fileName), isWebHost: true));
        }

        // Newtonsoft global defaults: JsonConvert.DefaultSettings = () => new JsonSerializerSettings { ... };
        foreach (var assignment in root.DescendantNodes().OfType<AssignmentExpressionSyntax>())
        {
            if (assignment.Left.ToString().EndsWith("JsonConvert.DefaultSettings", StringComparison.Ordinal) && assignment.Right is LambdaExpressionSyntax lambda)
            {
                var builder = new SettingsBuilder(SerializerKind.NewtonsoftJson);
                builder.Visit(lambda.Body);
                results.Add(builder.ToConfiguration($"{shortName}: JsonConvert.DefaultSettings", Locate(assignment, fileName), isWebHost: false));
            }
        }

        return results;
    }

    private static SourceLocation Locate(SyntaxNode node, string fileName)
    {
        var line = node.GetLocation().GetLineSpan().StartLinePosition;
        return new SourceLocation(fileName, line.Line, line.Character, SourceOrigin.Source);
    }

    private static string? GetInvokedName(ExpressionSyntax expression) => expression switch
    {
        MemberAccessExpressionSyntax member => member.Name.Identifier.ValueText,
        IdentifierNameSyntax identifier => identifier.Identifier.ValueText,
        GenericNameSyntax generic => generic.Identifier.ValueText,
        _ => null,
    };

    private sealed class SettingsBuilder
    {
        private readonly SerializerKind _serializer;
        private readonly List<string> _unknown = new();
        private ProfileOverrides _settings = ProfileOverrides.None;

        public SettingsBuilder(SerializerKind serializer) => _serializer = serializer;

        public void Visit(SyntaxNode body)
        {
            foreach (var node in body.DescendantNodesAndSelf())
            {
                switch (node)
                {
                    case AssignmentExpressionSyntax assignment when assignment.IsKind(SyntaxKind.SimpleAssignmentExpression):
                        Assign(LastName(assignment.Left), assignment.Right);
                        break;
                    case InvocationExpressionSyntax invocation when invocation.Expression is MemberAccessExpressionSyntax { Name.Identifier.ValueText: "Add" } add &&
                                                                      LastName(add.Expression) == "Converters":
                        AddConverter(invocation.ArgumentList.Arguments.FirstOrDefault()?.Expression);
                        break;
                }
            }
        }

        public DetectedConfiguration ToConfiguration(string source, SourceLocation location, bool isWebHost) => new()
        {
            Serializer = _serializer,
            Source = source,
            Location = location,
            IsWebHost = isWebHost,
            Settings = _settings,
            UnknownSettings = _unknown.Distinct().ToArray(),
        };

        private static string LastName(ExpressionSyntax expression) => expression switch
        {
            MemberAccessExpressionSyntax m => m.Name.Identifier.ValueText,
            IdentifierNameSyntax i => i.Identifier.ValueText,
            _ => expression.ToString(),
        };

        private void Assign(string name, ExpressionSyntax value)
        {
            var text = value.ToString().Replace(" ", string.Empty);
            if (_serializer == SerializerKind.SystemTextJson)
            {
                switch (name)
                {
                    case "PropertyNamingPolicy":
                        Naming(text, p => _settings = _settings with { PropertyNaming = p }, name);
                        return;
                    case "DictionaryKeyPolicy":
                        Naming(text, p => _settings = _settings with { DictionaryKeyNaming = p }, name);
                        return;
                    case "DefaultIgnoreCondition":
                        if (text.EndsWith("WhenWritingNull", StringComparison.Ordinal))
                        {
                            _settings = _settings with { IgnoreCondition = DefaultIgnoreCondition.WhenWritingNull };
                        }
                        else if (text.EndsWith("WhenWritingDefault", StringComparison.Ordinal))
                        {
                            _settings = _settings with { IgnoreCondition = DefaultIgnoreCondition.WhenWritingDefault };
                        }
                        else if (text.EndsWith("Never", StringComparison.Ordinal))
                        {
                            _settings = _settings with { IgnoreCondition = DefaultIgnoreCondition.Never };
                        }
                        else
                        {
                            _unknown.Add(ProfileSettings.IgnoreCondition);
                        }

                        return;
                    case "IgnoreNullValues":
                        if (Bool(text, name) == true)
                        {
                            _settings = _settings with { IgnoreCondition = DefaultIgnoreCondition.WhenWritingNull };
                        }

                        return;
                    case "IncludeFields":
                        _settings = _settings with { IncludeFields = Bool(text, name) };
                        return;
                    case "IgnoreReadOnlyProperties":
                        _settings = _settings with { IgnoreReadOnlyProperties = Bool(text, name) };
                        return;
                    case "IgnoreReadOnlyFields":
                        _settings = _settings with { IgnoreReadOnlyFields = Bool(text, name) };
                        return;
                    case "WriteIndented":
                        _settings = _settings with { WriteIndented = Bool(text, name) };
                        return;
                    case "NumberHandling":
                        _settings = _settings with { WriteNumbersAsStrings = text.Contains("WriteAsString") };
                        return;
                    case "ReferenceHandler":
                        if (text.EndsWith("ReferenceHandler.Preserve", StringComparison.Ordinal))
                        {
                            _settings = _settings with { ReferenceHandling = ReferenceHandlingMode.Preserve };
                        }
                        else if (text.EndsWith("ReferenceHandler.IgnoreCycles", StringComparison.Ordinal))
                        {
                            _settings = _settings with { ReferenceHandling = ReferenceHandlingMode.IgnoreCycles };
                        }
                        else
                        {
                            _unknown.Add(ProfileSettings.ReferenceHandling);
                        }

                        return;
                    case "TypeInfoResolver":
                    case "TypeInfoResolverChain":
                        _unknown.Add("TypeInfoResolver (custom contract resolution)");
                        return;
                }

                return;
            }

            switch (name)
            {
                case "ContractResolver":
                    NewtonsoftResolver(value);
                    return;
                case "NamingStrategy":
                    NewtonsoftStrategy(text, overrideSpecified: null, processKeys: null);
                    return;
                case "NullValueHandling":
                    if (text.EndsWith("Ignore", StringComparison.Ordinal))
                    {
                        _settings = _settings with { IgnoreCondition = DefaultIgnoreCondition.WhenWritingNull };
                    }

                    return;
                case "DefaultValueHandling":
                    if (text.EndsWith("Ignore", StringComparison.Ordinal) || text.EndsWith("IgnoreAndPopulate", StringComparison.Ordinal))
                    {
                        _settings = _settings with { IgnoreCondition = DefaultIgnoreCondition.WhenWritingDefault };
                    }

                    return;
                case "ReferenceLoopHandling":
                    if (text.EndsWith("Ignore", StringComparison.Ordinal))
                    {
                        _settings = _settings with { ReferenceHandling = ReferenceHandlingMode.IgnoreCycles };
                    }

                    return;
                case "PreserveReferencesHandling":
                    if (text.EndsWith("Objects", StringComparison.Ordinal) || text.EndsWith("All", StringComparison.Ordinal))
                    {
                        _settings = _settings with { ReferenceHandling = ReferenceHandlingMode.Preserve };
                    }

                    return;
                case "Formatting":
                    _settings = _settings with { WriteIndented = text.EndsWith("Indented", StringComparison.Ordinal) };
                    return;
                case "TypeNameHandling":
                    _unknown.Add("TypeNameHandling (type metadata is not modeled)");
                    return;
                case "ProcessDictionaryKeys":
                    if (Bool(text, name) is { } process)
                    {
                        _settings = _settings with { DictionaryKeyNaming = process ? _settings.PropertyNaming ?? NamingPolicyKind.Unchanged : NamingPolicyKind.Unchanged };
                    }

                    return;
                case "OverrideSpecifiedNames":
                    _settings = _settings with { OverrideSpecifiedNames = Bool(text, name) };
                    return;
            }
        }

        private void NewtonsoftResolver(ExpressionSyntax value)
        {
            var text = value.ToString().Replace(" ", string.Empty);
            if (text.StartsWith("newCamelCasePropertyNamesContractResolver", StringComparison.Ordinal))
            {
                _settings = _settings with { PropertyNaming = NamingPolicyKind.CamelCase, DictionaryKeyNaming = NamingPolicyKind.CamelCase, OverrideSpecifiedNames = true };
                return;
            }

            if (text.StartsWith("newDefaultContractResolver", StringComparison.Ordinal))
            {
                _settings = _settings with { PropertyNaming = NamingPolicyKind.Unchanged };
                if (value is ObjectCreationExpressionSyntax { Initializer: { } initializer })
                {
                    foreach (var expr in initializer.Expressions.OfType<AssignmentExpressionSyntax>())
                    {
                        if (LastName(expr.Left) == "NamingStrategy")
                        {
                            var strategy = expr.Right as ObjectCreationExpressionSyntax;
                            bool? processKeys = null;
                            bool? overrideSpecified = null;
                            if (strategy?.Initializer is { } strategyInit)
                            {
                                foreach (var s in strategyInit.Expressions.OfType<AssignmentExpressionSyntax>())
                                {
                                    var v = s.Right.ToString();
                                    if (LastName(s.Left) == "ProcessDictionaryKeys")
                                    {
                                        processKeys = v == "true";
                                    }
                                    else if (LastName(s.Left) == "OverrideSpecifiedNames")
                                    {
                                        overrideSpecified = v == "true";
                                    }
                                }
                            }

                            NewtonsoftStrategy(expr.Right.ToString().Replace(" ", string.Empty), overrideSpecified, processKeys);
                        }
                    }
                }

                return;
            }

            _unknown.Add("ContractResolver (custom contract resolver)");
        }

        private void NewtonsoftStrategy(string text, bool? overrideSpecified, bool? processKeys)
        {
            NamingPolicyKind? policy = text.StartsWith("newCamelCaseNamingStrategy", StringComparison.Ordinal) ? NamingPolicyKind.CamelCase
                : text.StartsWith("newSnakeCaseNamingStrategy", StringComparison.Ordinal) ? NamingPolicyKind.SnakeCaseLower
                : text.StartsWith("newKebabCaseNamingStrategy", StringComparison.Ordinal) ? NamingPolicyKind.KebabCaseLower
                : text.StartsWith("newDefaultNamingStrategy", StringComparison.Ordinal) ? NamingPolicyKind.Unchanged
                : null;
            if (policy is null)
            {
                _unknown.Add("NamingStrategy (custom naming strategy)");
                return;
            }

            _settings = _settings with
            {
                PropertyNaming = policy,
                DictionaryKeyNaming = processKeys == true ? policy : NamingPolicyKind.Unchanged,
                OverrideSpecifiedNames = overrideSpecified ?? false,
            };
        }

        private void Naming(string text, Action<NamingPolicyKind> set, string setting)
        {
            NamingPolicyKind? policy = text switch
            {
                "null" => NamingPolicyKind.Unchanged,
                _ when text.EndsWith("JsonNamingPolicy.CamelCase", StringComparison.Ordinal) => NamingPolicyKind.CamelCase,
                _ when text.EndsWith("JsonNamingPolicy.SnakeCaseLower", StringComparison.Ordinal) => NamingPolicyKind.SnakeCaseLower,
                _ when text.EndsWith("JsonNamingPolicy.SnakeCaseUpper", StringComparison.Ordinal) => NamingPolicyKind.SnakeCaseUpper,
                _ when text.EndsWith("JsonNamingPolicy.KebabCaseLower", StringComparison.Ordinal) => NamingPolicyKind.KebabCaseLower,
                _ when text.EndsWith("JsonNamingPolicy.KebabCaseUpper", StringComparison.Ordinal) => NamingPolicyKind.KebabCaseUpper,
                _ => null,
            };
            if (policy is null)
            {
                _unknown.Add(setting + " (custom naming policy)");
            }
            else
            {
                set(policy.Value);
            }
        }

        private bool? Bool(string text, string setting)
        {
            if (text == "true")
            {
                return true;
            }

            if (text == "false")
            {
                return false;
            }

            _unknown.Add(setting);
            return null;
        }

        private void AddConverter(ExpressionSyntax? expression)
        {
            if (expression is not BaseObjectCreationExpressionSyntax creation)
            {
                _unknown.Add("Converters (non-literal converter registration)");
                return;
            }

            var typeName = creation is ObjectCreationExpressionSyntax oc ? oc.Type.ToString() : "?";
            var args = creation.ArgumentList?.Arguments.Select(a => a.ToString().Replace(" ", string.Empty)).ToList() ?? new List<string>();
            if (typeName.EndsWith("JsonStringEnumConverter", StringComparison.Ordinal) || typeName.EndsWith("StringEnumConverter", StringComparison.Ordinal))
            {
                var naming = NamingPolicyKind.Unchanged;
                if (args.Any(a => a.Contains("CamelCase") || a == "true"))
                {
                    naming = NamingPolicyKind.CamelCase;
                }
                else if (args.Any(a => a.Contains("SnakeCase")))
                {
                    naming = NamingPolicyKind.SnakeCaseLower;
                }
                else if (args.Any(a => a.Contains("KebabCase")))
                {
                    naming = NamingPolicyKind.KebabCaseLower;
                }

                _settings = _settings with { Enums = EnumRepresentation.Strings, EnumNaming = naming };
                return;
            }

            _unknown.Add($"Converters.Add({typeName})");
        }
    }

    private sealed class ProjectCache
    {
        public ConcurrentDictionary<DocumentId, DocumentEntry> Documents { get; } = new();
    }

    private sealed class DocumentEntry
    {
        public DocumentEntry(VersionStamp version, IReadOnlyList<DetectedConfiguration> results)
        {
            Version = version;
            Results = results;
        }

        public VersionStamp Version { get; }

        public IReadOnlyList<DetectedConfiguration> Results { get; }
    }
}
