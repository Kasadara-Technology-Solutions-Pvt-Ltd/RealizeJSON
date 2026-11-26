using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.CodeAnalysis;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Threading;
using RealizeJSON.Serialization;
using RealizeJSON.Serialization.NewtonsoftJson;
using RealizeJSON.Serialization.SystemTextJson;
using RealizeJSON.UI;

namespace RealizeJSON.VisualStudio;

/// <summary>
/// Owns the engine, coordinator and view model for one tool-window instance and wires IDE events to them. All event
/// subscriptions are removed on <see cref="Dispose"/>.
/// </summary>
internal sealed class PreviewSession : IDisposable
{
    private readonly ToolWindowServices _services;
    private readonly PreviewCoordinator _coordinator;
    private UserSettings _userSettings;
    private UserSettings _effectiveSettings;
    private string? _solutionPath;
    private bool _disposed;

    public PreviewSession(ToolWindowServices services)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        _services = services;
        _userSettings = services.Store.Load();
        _effectiveSettings = _userSettings;

        var engine = new PreviewEngine(new IRuleProvider[] { new SystemTextJsonRuleProvider(), new NewtonsoftJsonRuleProvider() });
        _coordinator = new PreviewCoordinator(engine, new VsEditorContext(services.JoinableTaskFactory, services.Tracker), () => _effectiveSettings);
        ViewModel = new PreviewViewModel(_coordinator, new VsUiDispatcher(services.JoinableTaskFactory), new VsHostServices(services.Package), services.Store, _effectiveSettings);
        ViewModel.PropertyChanged += (_, _) => _effectiveSettings = ViewModel.Settings;

        services.Tracker.ActiveViewChanged += OnActiveViewChanged;
        services.Tracker.CaretMoved += OnCaretMoved;
        services.Tracker.TextChanged += OnTextChanged;
        services.Workspace.WorkspaceChanged += OnWorkspaceChanged;
        OptionsPage.SettingsSaved += OnOptionsSaved;
        LoadSolutionProfile(services.Workspace.CurrentSolution.FilePath);
    }

    public PreviewViewModel ViewModel { get; }

    public void SetVisible(bool visible) => _coordinator.SetVisible(visible);

    /// <summary>"Preview as JSON": follow the caret again and refresh immediately.</summary>
    public void PreviewTypeAtCaret()
    {
        if (_coordinator.PinnedTypeKey is not null)
        {
            _coordinator.Unpin();
        }

        _coordinator.SelectCandidate(null);
    }

    /// <summary>Writes the current profile to .realizejson.json next to the solution (explicit user action only).</summary>
    public void SaveSolutionProfile()
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        var host = new VsHostServices(_services.Package);
        if (_solutionPath is null)
        {
            host.ShowMessage("Open a solution to save a solution profile.");
            return;
        }

        var target = Path.Combine(Path.GetDirectoryName(_solutionPath)!, SolutionProfile.FileName);
        if (!host.Confirm($"Write the current serializer profile to:\n{target}\n\nThis file can be committed to share preview settings. Project files are not modified."))
        {
            return;
        }

        var settings = ViewModel.Settings;
        host.WriteAllText(target, SolutionProfile.ToJson(settings.Serializer, settings.Preset, settings.Overrides));
    }

    private void LoadSolutionProfile(string? solutionPath)
    {
        _solutionPath = solutionPath;
        if (solutionPath is null)
        {
            return;
        }

        var file = Path.Combine(Path.GetDirectoryName(solutionPath) ?? string.Empty, SolutionProfile.FileName);
        string? json = null;
        try
        {
            if (File.Exists(file) && new FileInfo(file).Length < 64 * 1024)
            {
                json = File.ReadAllText(file);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            json = null;
        }

        var (applied, _) = SolutionProfile.Apply(_userSettings, json);
        _effectiveSettings = applied;
        var hasSolutionProfile = json is not null;
        _services.JoinableTaskFactory.RunAsync(async () =>
        {
            await _services.JoinableTaskFactory.SwitchToMainThreadAsync();
            if (!_disposed)
            {
                ViewModel.PersistChanges = false;
                ViewModel.ApplySettings(applied);
                ViewModel.PersistChanges = !hasSolutionProfile;
            }
        }).Task.Forget();
    }

    private void OnOptionsSaved(object? sender, UserSettings settings)
    {
        _userSettings = settings;
        ViewModel.ApplySettings(settings);
    }

    private void OnActiveViewChanged(object? sender, EventArgs e) => _coordinator.OnActiveDocumentChanged();

    private void OnCaretMoved(object? sender, EventArgs e) => _coordinator.OnCaretMoved();

    private void OnTextChanged(object? sender, EventArgs e) => _coordinator.OnTextChanged();

    /// <summary>Raised on background threads; only cheap filtering happens here.</summary>
    private void OnWorkspaceChanged(object? sender, WorkspaceChangeEventArgs e)
    {
        switch (e.Kind)
        {
            case WorkspaceChangeKind.SolutionCleared:
            case WorkspaceChangeKind.SolutionRemoved:
                _coordinator.OnSolutionClosed();
                break;
            case WorkspaceChangeKind.SolutionAdded:
            case WorkspaceChangeKind.SolutionReloaded:
                LoadSolutionProfile(e.NewSolution.FilePath);
                _coordinator.OnWorkspaceChanged(Array.Empty<string>(), projectStructureChanged: true);
                break;
            case WorkspaceChangeKind.ProjectAdded:
            case WorkspaceChangeKind.ProjectRemoved:
            case WorkspaceChangeKind.ProjectChanged:
            case WorkspaceChangeKind.ProjectReloaded:
                _coordinator.OnWorkspaceChanged(Array.Empty<string>(), projectStructureChanged: true);
                break;
            case WorkspaceChangeKind.DocumentAdded:
            case WorkspaceChangeKind.DocumentRemoved:
            case WorkspaceChangeKind.DocumentChanged:
            case WorkspaceChangeKind.DocumentReloaded:
                var path = (e.NewSolution.GetDocument(e.DocumentId) ?? e.OldSolution.GetDocument(e.DocumentId))?.FilePath;
                if (path is not null)
                {
                    _coordinator.OnWorkspaceChanged(new List<string> { path }, projectStructureChanged: false);
                }

                break;
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _services.Tracker.ActiveViewChanged -= OnActiveViewChanged;
        _services.Tracker.CaretMoved -= OnCaretMoved;
        _services.Tracker.TextChanged -= OnTextChanged;
        _services.Workspace.WorkspaceChanged -= OnWorkspaceChanged;
        OptionsPage.SettingsSaved -= OnOptionsSaved;
        ViewModel.Dispose();
        _coordinator.Dispose();
    }
}
