using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using RealizeJSON.CodeAnalysis;
using RealizeJSON.Core;
using RealizeJSON.Diagnostics;
using RealizeJSON.Serialization;

namespace RealizeJSON.UI;

/// <summary>Why a refresh was requested.</summary>
public enum RefreshReason
{
    Opened,
    TextChanged,
    CaretMoved,
    ActiveDocumentChanged,
    DependencyChanged,
    ProfileChanged,
    Manual,
}

/// <summary>A published preview with presentation state (stale/live) decided by the coordinator.</summary>
public sealed record PreviewPresentation(PreviewResult Result, PreviewState State, bool IsStale, string? StaleReason);

/// <summary>
/// Coordinates live refresh: debounces events, cancels superseded analyses, prevents stale results from overwriting
/// newer ones, retains the last valid preview on transient errors, and suspends while hidden. Thread-safe.
/// </summary>
public sealed class PreviewCoordinator : IDisposable
{
    private readonly PreviewEngine _engine;
    private readonly IEditorContext _editor;
    private readonly Func<UserSettings> _settings;
    private readonly object _gate = new();
    private CancellationTokenSource? _pending;
    private long _generation;
    private bool _visible;
    private bool _dirty = true;
    private bool _disposed;
    private Task _current = Task.CompletedTask;
    private PreviewResult? _lastGood;
    private PreviewResult? _lastPublished;
    private string? _lastDocumentPath;
    private int _lastTextVersion = -1;

    public PreviewCoordinator(PreviewEngine engine, IEditorContext editor, Func<UserSettings> settings)
    {
        _engine = engine;
        _editor = editor;
        _settings = settings;
    }

    /// <summary>Raised (on a background thread) when a new presentation is available.</summary>
    public event EventHandler<PreviewPresentation>? Published;

    /// <summary>Raised when an analysis starts (for the "Analyzing" indicator).</summary>
    public event EventHandler? AnalysisStarted;

    public string? PinnedTypeKey { get; private set; }

    public string? SelectedCandidateKey { get; private set; }

    public string? SelectedVariantKey { get; private set; }

    public int SelectedConfiguration { get; private set; }

    public bool IsVisible
    {
        get
        {
            lock (_gate)
            {
                return _visible;
            }
        }
    }

    /// <summary>Completes when the most recently scheduled analysis has finished (for tests and shutdown).</summary>
    public Task WhenIdleAsync()
    {
        lock (_gate)
        {
            return _current;
        }
    }

    /// <summary>The tool window became visible or hidden. Hidden windows do not analyze.</summary>
    public void SetVisible(bool visible)
    {
        bool refresh;
        lock (_gate)
        {
            _visible = visible;
            if (!visible)
            {
                _pending?.Cancel();
            }

            refresh = visible && _dirty;
        }

        if (refresh)
        {
            Schedule(RefreshReason.Opened, immediate: true);
        }
    }

    public void OnTextChanged() => Schedule(RefreshReason.TextChanged);

    public void OnActiveDocumentChanged() => Schedule(RefreshReason.ActiveDocumentChanged);

    /// <summary>
    /// Caret moved. When following the caret, the type at the caret is re-located (cheap, syntax/semantic lookup only);
    /// the contract is rebuilt only when the selected type changes.
    /// </summary>
    public void OnCaretMoved()
    {
        if (PinnedTypeKey is null)
        {
            Schedule(RefreshReason.CaretMoved);
        }
    }

    /// <summary>Workspace documents changed. Refreshes only when the preview depends on a changed file.</summary>
    public void OnWorkspaceChanged(IReadOnlyCollection<string> changedFiles, bool projectStructureChanged)
    {
        PreviewResult? last;
        lock (_gate)
        {
            last = _lastPublished;
        }

        if (projectStructureChanged || last is null ||
            changedFiles.Any(f => last.ContributingFiles.Contains(f, StringComparer.OrdinalIgnoreCase)))
        {
            Schedule(RefreshReason.DependencyChanged);
        }
    }

    public void OnProfileChanged() => Schedule(RefreshReason.ProfileChanged, immediate: true);

    public void Refresh() => Schedule(RefreshReason.Manual, immediate: true);

    public void Pin(string typeKey)
    {
        PinnedTypeKey = typeKey;
        Schedule(RefreshReason.Manual, immediate: true);
    }

    public void Unpin()
    {
        PinnedTypeKey = null;
        Schedule(RefreshReason.Manual, immediate: true);
    }

    public void SelectCandidate(string? key)
    {
        SelectedCandidateKey = key;
        if (PinnedTypeKey is not null && key is not null)
        {
            PinnedTypeKey = key;
        }

        Schedule(RefreshReason.Manual, immediate: true);
    }

    public void SelectVariant(string? key)
    {
        SelectedVariantKey = string.IsNullOrEmpty(key) ? null : key;
        Schedule(RefreshReason.Manual, immediate: true);
    }

    public void SelectConfiguration(int index)
    {
        SelectedConfiguration = Math.Max(0, index);
        Schedule(RefreshReason.ProfileChanged, immediate: true);
    }

    /// <summary>Solution closed: cancel work, drop cached state and caches.</summary>
    public void OnSolutionClosed()
    {
        lock (_gate)
        {
            _pending?.Cancel();
            _generation++;
            _lastGood = null;
            _lastPublished = null;
            _lastDocumentPath = null;
            _lastTextVersion = -1;
            PinnedTypeKey = null;
            SelectedCandidateKey = null;
            SelectedVariantKey = null;
            _dirty = true;
        }

        _engine.ClearCaches();
        Publish(new PreviewPresentation(PreviewResult.Empty(PreviewState.Empty, DiagnosticCatalog.Create(DiagnosticIds.NoDocument, null, null)), PreviewState.Empty, false, null));
    }

    private void Schedule(RefreshReason reason, bool immediate = false)
    {
        CancellationTokenSource cts;
        long generation;
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            if (!_visible)
            {
                _dirty = true;
                return;
            }

            // Selection changes reset the explicit candidate when following the caret.
            if (reason is RefreshReason.CaretMoved or RefreshReason.ActiveDocumentChanged && PinnedTypeKey is null)
            {
                SelectedCandidateKey = null;
            }

            _pending?.Cancel();
            _pending?.Dispose();
            _pending = cts = new CancellationTokenSource();
            generation = ++_generation;
            var delay = immediate ? 0 : _settings().DebounceMilliseconds;
            _current = Task.Run(() => RunAsync(reason, generation, delay, cts.Token));
        }
    }

    private async Task RunAsync(RefreshReason reason, long generation, int delay, CancellationToken cancellationToken)
    {
        try
        {
            if (delay > 0)
            {
                await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
            }

            var snapshot = await _editor.GetActiveAsync(cancellationToken).ConfigureAwait(false);
            if (snapshot is null)
            {
                PublishIfCurrent(generation, NoEditorPresentation());
                return;
            }

            // Cheap relevance check for caret moves: same document text and same type at caret -> nothing to do.
            if (reason == RefreshReason.CaretMoved && PinnedTypeKey is null)
            {
                PreviewResult? last;
                string? lastPath;
                int lastVersion;
                lock (_gate)
                {
                    last = _lastPublished;
                    lastPath = _lastDocumentPath;
                    lastVersion = _lastTextVersion;
                }

                if (last?.SelectedTypeKey is not null && lastPath == snapshot.Document.FilePath && lastVersion == snapshot.TextVersion)
                {
                    var key = await PreviewEngine.GetTypeKeyAtAsync(snapshot.Document, snapshot.CaretPosition, cancellationToken).ConfigureAwait(false);
                    if (key == last.SelectedTypeKey)
                    {
                        return;
                    }
                }
            }

            AnalysisStarted?.Invoke(this, EventArgs.Empty);
            var settings = _settings().Normalize();
            var request = new PreviewRequest(snapshot.Document, snapshot.CaretPosition)
            {
                PinnedTypeKeyText = PinnedTypeKey,
                SelectedCandidateKey = SelectedCandidateKey,
                Serializer = settings.Serializer,
                Preset = settings.Preset,
                Overrides = settings.Overrides,
                ConfigurationMode = settings.ConfigurationMode,
                SelectedConfiguration = SelectedConfiguration,
                Options = settings.ToOptions(SelectedVariantKey),
                SensitiveNameWarnings = settings.SensitiveNameWarnings,
            };

            var result = await _engine.GenerateAsync(request, cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            PublishIfCurrent(generation, Present(result), snapshot);
        }
        catch (OperationCanceledException)
        {
            // Superseded by a newer request or suspended.
        }
#pragma warning disable CA1031 // Fault isolation: analysis failures must never escape into the IDE.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            var failure = DiagnosticCatalog.Create(DiagnosticIds.AnalysisFailed, null, null, ex.GetType().Name);
            PreviewResult? lastGood;
            lock (_gate)
            {
                lastGood = _lastGood;
            }

            var presentation = lastGood is null
                ? new PreviewPresentation(PreviewResult.Empty(PreviewState.Error, failure), PreviewState.Error, false, null)
                : new PreviewPresentation(lastGood with { Diagnostics = new[] { failure }.Concat(lastGood.Diagnostics).ToList() }, PreviewState.Stale, true, "Analysis failed; showing the last valid preview.");
            PublishIfCurrent(generation, presentation);
        }
    }

    private PreviewPresentation Present(PreviewResult result)
    {
        PreviewResult? lastGood;
        lock (_gate)
        {
            lastGood = _lastGood;
        }

        // Transient syntax errors: keep the last valid preview of the same type and mark it stale.
        if (result.SourceHasErrors && lastGood is not null && lastGood.SelectedTypeKey == result.SelectedTypeKey && result.SelectedTypeKey is not null)
        {
            var diagnostics = result.Diagnostics.Where(d => d.Id == DiagnosticIds.SourceHasErrors).Concat(lastGood.Diagnostics).ToList();
            return new PreviewPresentation(lastGood with { Diagnostics = diagnostics }, PreviewState.Stale, true, "The source has syntax errors; showing the last valid preview.");
        }

        if (result.SourceHasErrors && result.State is PreviewState.Live)
        {
            return new PreviewPresentation(result, PreviewState.Partial, false, null);
        }

        return new PreviewPresentation(result, result.State, false, null);
    }

    private PreviewPresentation NoEditorPresentation()
    {
        PreviewResult? last;
        lock (_gate)
        {
            last = _lastPublished;
        }

        if (_editor.IsNonCSharpDocumentActive && last is null)
        {
            return new PreviewPresentation(PreviewResult.Empty(PreviewState.Unsupported, DiagnosticCatalog.Create(DiagnosticIds.UnsupportedLanguage, null, null)), PreviewState.Unsupported, false, null);
        }

        // Keep showing the last preview (e.g. after closing the document) but mark it stale.
        return last is null
            ? new PreviewPresentation(PreviewResult.Empty(PreviewState.Empty, DiagnosticCatalog.Create(DiagnosticIds.NoDocument, null, null)), PreviewState.Empty, false, null)
            : new PreviewPresentation(last, PreviewState.Stale, true, "No active C# editor; showing the last preview.");
    }

    private void PublishIfCurrent(long generation, PreviewPresentation presentation, EditorSnapshot? snapshot = null)
    {
        lock (_gate)
        {
            if (generation != _generation || _disposed)
            {
                return;
            }

            _dirty = false;
            if (!presentation.IsStale)
            {
                _lastPublished = presentation.Result;
                if (presentation.State is PreviewState.Live or PreviewState.Partial && !presentation.Result.SourceHasErrors && presentation.Result.Json.Length > 0)
                {
                    _lastGood = presentation.Result;
                }
            }

            if (snapshot is not null)
            {
                _lastDocumentPath = snapshot.Document.FilePath;
                _lastTextVersion = snapshot.TextVersion;
            }
        }

        Publish(presentation);
    }

    private void Publish(PreviewPresentation presentation) => Published?.Invoke(this, presentation);

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _pending?.Cancel();
            _pending?.Dispose();
            _pending = null;
            _lastGood = null;
            _lastPublished = null;
        }

        Published = null;
        AnalysisStarted = null;
    }
}
