using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Threading;
using RealizeJSON.Core;
using RealizeJSON.Core.Json;
using RealizeJSON.Core.Naming;
using RealizeJSON.Diagnostics;

namespace RealizeJSON.UI;

/// <summary>A diagnostic row.</summary>
public sealed record DiagnosticItem(string Id, string Severity, string Message, string? Location, Classification Classification);

/// <summary>A node of the optional JSON tree view (P1).</summary>
public sealed class JsonTreeItem
{
    public JsonTreeItem(string label, string path, int? line)
    {
        Label = label;
        Path = path;
        Line = line;
    }

    public string Label { get; }

    public string Path { get; }

    public int? Line { get; }

    public IList<JsonTreeItem> Children { get; } = new List<JsonTreeItem>();
}

/// <summary>
/// Tool-window view model. All members must be accessed on the UI thread; background results are marshalled through
/// <see cref="IUiDispatcher"/>.
/// </summary>
public sealed class PreviewViewModel : ObservableObject, IDisposable
{
    private const int MaxTreeNodes = 2000;
    private readonly PreviewCoordinator _coordinator;
    private readonly IUiDispatcher _dispatcher;
    private readonly IHostServices _host;
    private readonly ISettingsStore _store;
    private UserSettings _settings;
    private PreviewPresentation? _presentation;
    private string _json = string.Empty;
    private string _stateText = "Empty";
    private PreviewState _state = PreviewState.Empty;
    private string _label = "Static preview";
    private string _selectedType = "No type selected";
    private string _context = string.Empty;
    private string _statusSummary = string.Empty;
    private string _searchText = string.Empty;
    private string? _configurationSummary;
    private bool _isAnalyzing;
    private CandidateInfo? _selectedCandidate;
    private VariantInfo? _selectedVariant;
    private bool _suppressSelection;

    public PreviewViewModel(PreviewCoordinator coordinator, IUiDispatcher dispatcher, IHostServices host, ISettingsStore store, UserSettings settings)
    {
        _coordinator = coordinator;
        _dispatcher = dispatcher;
        _host = host;
        _store = store;
        _settings = settings.Normalize();
        _coordinator.Published += OnPublished;
        _coordinator.AnalysisStarted += OnAnalysisStarted;

        RefreshCommand = new RelayCommand(_ => _coordinator.Refresh());
        CopyCommand = new RelayCommand(_ => Copy(), _ => _json.Length > 0);
        ExportCommand = new RelayCommand(_ => Export(), _ => _json.Length > 0);
        PinCommand = new RelayCommand(_ => TogglePin(), _ => _presentation?.Result.SelectedTypeKey is not null || IsPinned);
        SettingsCommand = new RelayCommand(_ => _host.OpenSettings());
        NavigateCommand = new RelayCommand(p => Navigate(p));
        ToggleFormatCommand = new RelayCommand(_ => IsFormatted = !IsFormatted);
    }

    public RelayCommand RefreshCommand { get; }

    public RelayCommand CopyCommand { get; }

    public RelayCommand ExportCommand { get; }

    public RelayCommand PinCommand { get; }

    public RelayCommand SettingsCommand { get; }

    public RelayCommand NavigateCommand { get; }

    public RelayCommand ToggleFormatCommand { get; }

    public UserSettings Settings => _settings;

    /// <summary>
    /// When false (a solution profile is active), toolbar changes apply to the session only and are not written to the
    /// per-user store, so solution-scoped values never leak into user defaults.
    /// </summary>
    public bool PersistChanges { get; set; } = true;

    // ---- Presentation ----------------------------------------------------------------------------------------

    public string Json { get => _json; private set => Set(ref _json, value); }

    public PreviewState State { get => _state; private set => Set(ref _state, value); }

    /// <summary>Text status shown next to the indicator (never color-only).</summary>
    public string StateText { get => _stateText; private set => Set(ref _stateText, value); }

    public string Label { get => _label; private set => Set(ref _label, value); }

    public string SelectedTypeDisplay { get => _selectedType; private set => Set(ref _selectedType, value); }

    public string ContextDisplay { get => _context; private set => Set(ref _context, value); }

    public string StatusSummary { get => _statusSummary; private set => Set(ref _statusSummary, value); }

    public string? ConfigurationSummary { get => _configurationSummary; private set => Set(ref _configurationSummary, value); }

    public bool IsAnalyzing { get => _isAnalyzing; private set => Set(ref _isAnalyzing, value); }

    public bool IsComplete => _presentation?.Result.IsComplete ?? true;

    public bool IsStale => _presentation?.IsStale ?? false;

    public bool IsPinned => _coordinator.PinnedTypeKey is not null;

    public IReadOnlyList<JsonLineMapping> LineMap => _presentation?.Result.LineMap ?? Array.Empty<JsonLineMapping>();

    public ObservableCollection<DiagnosticItem> Diagnostics { get; } = new();

    public ObservableCollection<InspectorEntry> Inspector { get; } = new();

    public ObservableCollection<CandidateInfo> Candidates { get; } = new();

    public ObservableCollection<VariantInfo> Variants { get; } = new();

    public ObservableCollection<JsonTreeItem> Tree { get; } = new();

    public CandidateInfo? SelectedCandidate
    {
        get => _selectedCandidate;
        set
        {
            if (Set(ref _selectedCandidate, value) && !_suppressSelection && value is not null)
            {
                _coordinator.SelectCandidate(value.Key);
            }
        }
    }

    public VariantInfo? SelectedVariant
    {
        get => _selectedVariant;
        set
        {
            if (Set(ref _selectedVariant, value) && !_suppressSelection && value is not null)
            {
                _coordinator.SelectVariant(value.Key);
            }
        }
    }

    /// <summary>Filters the inspector by member or JSON name (P1 search).</summary>
    public string SearchText
    {
        get => _searchText;
        set
        {
            if (Set(ref _searchText, value ?? string.Empty))
            {
                ApplyInspector();
            }
        }
    }

    // ---- Settings exposed on the toolbar --------------------------------------------------------------------

    public IReadOnlyList<Choice<SerializerKind>> SerializerChoices { get; } = new[]
    {
        new Choice<SerializerKind>(SerializerKind.SystemTextJson, "System.Text.Json"),
        new Choice<SerializerKind>(SerializerKind.NewtonsoftJson, "Newtonsoft.Json"),
    };

    public IReadOnlyList<Choice<ProfilePreset>> PresetChoices { get; } = new[]
    {
        new Choice<ProfilePreset>(ProfilePreset.LibraryDefaults, "Library defaults"),
        new Choice<ProfilePreset>(ProfilePreset.AspNetCoreWeb, "ASP.NET Core web defaults"),
        new Choice<ProfilePreset>(ProfilePreset.Custom, "Custom preview profile"),
    };

    public IReadOnlyList<Choice<SampleMode>> SampleModeChoices { get; } = new[]
    {
        new Choice<SampleMode>(SampleMode.SampleValues, "Sample values"),
        new Choice<SampleMode>(SampleMode.ClrDefaults, "CLR defaults"),
    };

    /// <summary>Naming policies meaningful for the selected serializer (null = preset/detected default).</summary>
    public IReadOnlyList<Choice<NamingPolicyKind?>> NamingChoices =>
        new[] { new Choice<NamingPolicyKind?>(null, "Profile default") }
            .Concat(((NamingPolicyKind[])Enum.GetValues(typeof(NamingPolicyKind)))
                .Where(k => NamingPolicies.IsSupported(k, _settings.Serializer))
                .Select(k => new Choice<NamingPolicyKind?>(k, NamingPolicies.Label(k))))
            .ToList();

    public Choice<SerializerKind> SelectedSerializer
    {
        get => SerializerChoices.First(c => c.Value == _settings.Serializer);
        set
        {
            if (value is not null && value.Value != _settings.Serializer)
            {
                // Drop overrides that the new serializer does not support.
                var overrides = _settings.Overrides;
                if (overrides.PropertyNaming is { } naming && !NamingPolicies.IsSupported(naming, value.Value))
                {
                    overrides = overrides with { PropertyNaming = null };
                }

                Update(_settings with { Serializer = value.Value, Overrides = overrides });
                OnPropertyChanged(nameof(NamingChoices));
                OnPropertyChanged(nameof(SelectedNaming));
            }
        }
    }

    public Choice<ProfilePreset> SelectedPreset
    {
        get => PresetChoices.First(c => c.Value == _settings.Preset);
        set
        {
            if (value is not null && value.Value != _settings.Preset)
            {
                Update(_settings with { Preset = value.Value });
            }
        }
    }

    public Choice<SampleMode> SelectedSampleMode
    {
        get => SampleModeChoices.First(c => c.Value == _settings.SampleMode);
        set
        {
            if (value is not null && value.Value != _settings.SampleMode)
            {
                Update(_settings with { SampleMode = value.Value });
            }
        }
    }

    public Choice<NamingPolicyKind?> SelectedNaming
    {
        get => NamingChoices.FirstOrDefault(c => c.Value == _settings.Overrides.PropertyNaming) ?? NamingChoices[0];
        set
        {
            if (value is not null && value.Value != _settings.Overrides.PropertyNaming)
            {
                Update(_settings with { Overrides = _settings.Overrides with { PropertyNaming = value.Value } });
            }
        }
    }

    public bool EnumsAsStrings
    {
        get => _settings.Overrides.Enums == EnumRepresentation.Strings;
        set => Update(_settings with { Overrides = _settings.Overrides with { Enums = value ? EnumRepresentation.Strings : null } });
    }

    public bool IsFormatted
    {
        get => _settings.Overrides.WriteIndented ?? true;
        set => Update(_settings with { Overrides = _settings.Overrides with { WriteIndented = value ? null : false } });
    }

    public int CollectionCount
    {
        get => _settings.CollectionCount;
        set => Update(_settings with { CollectionCount = value });
    }

    public bool WordWrap
    {
        get => _settings.WordWrap;
        set
        {
            _settings = _settings with { WordWrap = value };
            Persist();
            OnPropertyChanged();
        }
    }

    /// <summary>Applies new settings (e.g. from the options page) and refreshes.</summary>
    public void ApplySettings(UserSettings settings) => Update(settings);

    private void Update(UserSettings settings)
    {
        _settings = settings.Normalize();
        Persist();
        OnPropertyChanged(string.Empty);
        _coordinator.OnProfileChanged();
    }

    private void Persist()
    {
        if (!PersistChanges)
        {
            return;
        }

        try
        {
            _store.Write(SettingsSerializer.StoreKey, SettingsSerializer.ToJson(_settings));
        }
#pragma warning disable CA1031 // Settings persistence failures must not break the preview.
        catch (Exception)
#pragma warning restore CA1031
        {
            // Persisting is best-effort; the in-memory settings remain active.
        }
    }

    // ---- Commands -------------------------------------------------------------------------------------------

    private void Copy()
    {
        if (_json.Length == 0)
        {
            return;
        }

        if (!IsComplete && !_host.Confirm("The preview is incomplete (limits were reached). Copy the bounded JSON anyway?"))
        {
            return;
        }

        if (IsStale && !_host.Confirm("The preview is stale and may not reflect the current source. Copy anyway?"))
        {
            return;
        }

        _host.CopyToClipboard(_json);
    }

    private void Export()
    {
        if (_json.Length == 0)
        {
            return;
        }

        if (!IsComplete && !_host.Confirm("The preview is incomplete (limits were reached). Export the bounded JSON anyway?"))
        {
            return;
        }

        var name = (_presentation?.Result.SelectedType?.DisplayName ?? "preview").Split('<')[0] + ".json";
        var path = _host.PromptForExportPath(name);
        if (path is null)
        {
            return;
        }

        _host.WriteAllText(path, _json);
    }

    private void TogglePin()
    {
        if (IsPinned)
        {
            _coordinator.Unpin();
        }
        else if (_presentation?.Result.SelectedTypeKey is { } key)
        {
            _coordinator.Pin(key);
        }

        OnPropertyChanged(nameof(IsPinned));
    }

    /// <summary>Navigates to a source declaration. Accepts an <see cref="InspectorEntry"/>, a line number or a location.</summary>
    private void Navigate(object? parameter)
    {
        var location = parameter switch
        {
            InspectorEntry entry => entry.Location,
            SourceLocation l => l,
            int line => LineMap.Where(m => m.Line <= line).OrderByDescending(m => m.Line).FirstOrDefault()?.Member?.Location,
            JsonTreeItem item => item.Line is { } l2 ? LineMap.FirstOrDefault(m => m.Line == l2)?.Member?.Location : null,
            _ => null,
        };

        if (location is null)
        {
            return;
        }

        if (!location.CanNavigate)
        {
            _host.ShowMessage("This member is declared in metadata (a referenced assembly); no source is available.");
            return;
        }

        _host.NavigateTo(location);
    }

    // ---- Publication ----------------------------------------------------------------------------------------

    private void OnAnalysisStarted(object? sender, EventArgs e) =>
        _ = _dispatcher.InvokeAsync(() => IsAnalyzing = true, CancellationToken.None);

    private void OnPublished(object? sender, PreviewPresentation presentation) =>
        _ = _dispatcher.InvokeAsync(() => Apply(presentation), CancellationToken.None);

    /// <summary>Applies a presentation (UI thread).</summary>
    internal void Apply(PreviewPresentation presentation)
    {
        _presentation = presentation;
        var result = presentation.Result;
        IsAnalyzing = false;
        Json = result.Json;
        State = presentation.State;
        StateText = presentation.State switch
        {
            PreviewState.Live => "Live",
            PreviewState.Analyzing => "Analyzing…",
            PreviewState.Stale => "Stale",
            PreviewState.Partial => "Partial",
            PreviewState.Unsupported => "Unsupported",
            PreviewState.Error => "Analysis failed",
            PreviewState.Suspended => "Suspended",
            _ => "Empty",
        };
        Label = result.Label.Length > 0 ? result.Label : "Static preview";
        SelectedTypeDisplay = result.SelectedType?.FullName ?? "No type selected";
        ContextDisplay = result.ProjectContext ?? string.Empty;
        ConfigurationSummary = result.ConfigurationSummary;

        var errors = result.Diagnostics.Count(d => d.Severity == DiagnosticSeverity.Error);
        var warnings = result.Diagnostics.Count(d => d.Severity == DiagnosticSeverity.Warning);
        var diagnosticsText = result.Diagnostics.Count == 0 ? "No diagnostics" : string.Format(CultureInfo.InvariantCulture, "{0} error(s), {1} warning(s), {2} info", errors, warnings, result.Diagnostics.Count - errors - warnings);
        StatusSummary = string.Join(" | ", new[]
        {
            "Static preview",
            result.PropertyCount.ToString(CultureInfo.InvariantCulture) + " properties",
            _settings.SampleMode == SampleMode.SampleValues ? "Sample values" : "CLR defaults",
            result.IsComplete ? null : "Incomplete",
            presentation.StaleReason,
            diagnosticsText,
        }.Where(s => !string.IsNullOrEmpty(s)));

        Replace(Diagnostics, result.Diagnostics.Select(d => new DiagnosticItem(d.Id, d.Severity.ToString(), d.Message, d.JsonPath ?? d.MemberPath, d.Classification)));
        ApplyInspector();

        _suppressSelection = true;
        try
        {
            Replace(Candidates, result.Candidates);
            SelectedCandidate = Candidates.FirstOrDefault(c => c.Key == result.SelectedTypeKey);
            Replace(Variants, result.Variants);
            SelectedVariant = Variants.FirstOrDefault(v => v.Key == (_coordinator.SelectedVariantKey ?? string.Empty));
        }
        finally
        {
            _suppressSelection = false;
        }

        Replace(Tree, result.Tree is null ? Array.Empty<JsonTreeItem>() : new[] { BuildTree("root", result.Tree, result.LineMap) });
        OnPropertyChanged(nameof(IsComplete));
        OnPropertyChanged(nameof(IsStale));
        OnPropertyChanged(nameof(IsPinned));
        OnPropertyChanged(nameof(LineMap));
        CopyCommand.RaiseCanExecuteChanged();
        ExportCommand.RaiseCanExecuteChanged();
        PinCommand.RaiseCanExecuteChanged();
    }

    private void ApplyInspector()
    {
        var rows = _presentation?.Result.Inspector ?? Array.Empty<InspectorEntry>();
        if (_searchText.Length > 0)
        {
            rows = rows.Where(r => r.ClrMember.IndexOf(_searchText, StringComparison.OrdinalIgnoreCase) >= 0 ||
                                   (r.JsonName?.IndexOf(_searchText, StringComparison.OrdinalIgnoreCase) ?? -1) >= 0).ToList();
        }

        Replace(Inspector, rows);
    }

    private static JsonTreeItem BuildTree(string label, JsonNodeModel node, IReadOnlyList<JsonLineMapping> map)
    {
        var count = 0;
        var lines = map.GroupBy(m => m.JsonPath).ToDictionary(g => g.Key, g => g.First().Line, StringComparer.Ordinal);
        return Build(label, node);

        JsonTreeItem Build(string name, JsonNodeModel n)
        {
            count++;
            int? line = lines.TryGetValue(n.Path, out var l) ? l : null;
            var item = new JsonTreeItem(name + Describe(n), n.Path, line);
            if (count > MaxTreeNodes)
            {
                return item;
            }

            switch (n)
            {
                case JsonObjectNode o:
                    foreach (var p in o.Properties)
                    {
                        item.Children.Add(Build(p.Name, p.Value));
                    }

                    break;
                case JsonArrayNode a:
                    for (var i = 0; i < a.Items.Count; i++)
                    {
                        item.Children.Add(Build("[" + i.ToString(CultureInfo.InvariantCulture) + "]", a.Items[i]));
                    }

                    break;
            }

            return item;
        }

        static string Describe(JsonNodeModel n) => n switch
        {
            JsonObjectNode o => " {" + o.Properties.Count.ToString(CultureInfo.InvariantCulture) + "}",
            JsonArrayNode a => " [" + a.Items.Count.ToString(CultureInfo.InvariantCulture) + "]",
            JsonStringNode s => ": \"" + (s.Value.Length > 40 ? s.Value.Substring(0, 40) + "…" : s.Value) + "\"",
            JsonNumberNode num => ": " + num.Text,
            JsonBooleanNode b => b.Value ? ": true" : ": false",
            _ => ": null",
        };
    }

    private static void Replace<T>(ObservableCollection<T> target, IEnumerable<T> items)
    {
        target.Clear();
        foreach (var item in items)
        {
            target.Add(item);
        }
    }

    public void Dispose()
    {
        _coordinator.Published -= OnPublished;
        _coordinator.AnalysisStarted -= OnAnalysisStarted;
    }
}
