using System;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis.Text;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Settings;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio.Shell.Settings;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.TextManager.Interop;
using Microsoft.VisualStudio.Threading;
using RealizeJSON.Core;
using RealizeJSON.UI;

namespace RealizeJSON.VisualStudio;

/// <summary>Reads the active editor state. Caret access happens on the UI thread; document mapping in the background.</summary>
internal sealed class VsEditorContext : IEditorContext
{
    private readonly JoinableTaskFactory _jtf;
    private readonly TextViewTracker _tracker;

    public VsEditorContext(JoinableTaskFactory jtf, TextViewTracker tracker)
    {
        _jtf = jtf;
        _tracker = tracker;
    }

    public bool IsNonCSharpDocumentActive => _tracker.ActiveView is not null && !_tracker.IsActiveViewCSharp;

    public async Task<EditorSnapshot?> GetActiveAsync(CancellationToken cancellationToken)
    {
        await _jtf.SwitchToMainThreadAsync(cancellationToken);
        var view = _tracker.ActiveView;
        if (view is null || !_tracker.IsActiveViewCSharp)
        {
            return null;
        }

        var snapshot = view.TextSnapshot;
        var point = view.Caret.Position.Point.GetPoint(view.TextBuffer, PositionAffinity.Successor);
        var caret = point?.Position ?? 0;
        await TaskScheduler.Default;

        // Includes unsaved changes: the Roslyn document is forked from the live editor snapshot.
        var document = snapshot.GetOpenDocumentInCurrentContextWithChanges();
        return document is null ? null : new EditorSnapshot(document, caret, snapshot.Version.VersionNumber);
    }
}

/// <summary>Marshals view-model updates to the UI thread without blocking.</summary>
internal sealed class VsUiDispatcher : IUiDispatcher
{
    private readonly JoinableTaskFactory _jtf;

    public VsUiDispatcher(JoinableTaskFactory jtf) => _jtf = jtf;

    public Task InvokeAsync(Action action, CancellationToken cancellationToken) =>
        _jtf.RunAsync(async () =>
        {
            await _jtf.SwitchToMainThreadAsync(cancellationToken);
            action();
        }).Task;
}

/// <summary>Per-user settings persisted in the Visual Studio settings store (no files, no registry writes by us).</summary>
internal sealed class VsSettingsStore : ISettingsStore
{
    private const string Collection = "RealizeJSON";
    private readonly WritableSettingsStore _store;

    public VsSettingsStore(IServiceProvider serviceProvider)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        _store = new ShellSettingsManager(serviceProvider).GetWritableSettingsStore(SettingsScope.UserSettings);
    }

    public string? Read(string key) => _store.PropertyExists(Collection, key) ? _store.GetString(Collection, key) : null;

    public void Write(string key, string value)
    {
        if (!_store.CollectionExists(Collection))
        {
            _store.CreateCollection(Collection);
        }

        _store.SetString(Collection, key, value);
    }

    public UserSettings Load()
    {
        var result = SettingsSerializer.FromJson(Read(SettingsSerializer.StoreKey));
        return result.Settings;
    }
}

/// <summary>Clipboard, dialogs and navigation. Must be called on the UI thread.</summary>
internal sealed class VsHostServices : IHostServices
{
    private readonly IServiceProvider _serviceProvider;
    private readonly AsyncPackage _package;

    public VsHostServices(AsyncPackage package)
    {
        _package = package;
        _serviceProvider = package;
    }

    public void CopyToClipboard(string text)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        try
        {
            System.Windows.Clipboard.SetText(text);
        }
        catch (COMException)
        {
            ShowMessage("The clipboard is in use by another application. Try again.");
        }
    }

    public string? PromptForExportPath(string suggestedFileName)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            FileName = suggestedFileName,
            DefaultExt = ".json",
            Filter = "JSON files (*.json)|*.json",
            AddExtension = true,
            OverwritePrompt = true,
        };
        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }

    public void WriteAllText(string path, string text)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        try
        {
            File.WriteAllText(path, text, new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ShowMessage("Export failed: " + ex.Message);
        }
    }

    public bool Confirm(string message)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        return VsShellUtilities.ShowMessageBox(_serviceProvider, message, "RealizeJSON", OLEMSGICON.OLEMSGICON_QUERY, OLEMSGBUTTON.OLEMSGBUTTON_OKCANCEL, OLEMSGDEFBUTTON.OLEMSGDEFBUTTON_SECOND) == (int)VSConstants.MessageBoxResult.IDOK;
    }

    public void NavigateTo(SourceLocation location)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        if (!location.CanNavigate || !File.Exists(location.FilePath))
        {
            ShowMessage("The declaration is not available as a source file.");
            return;
        }

        VsShellUtilities.OpenDocument(_serviceProvider, location.FilePath, VSConstants.LOGVIEWID.Code_guid, out _, out _, out _, out IVsTextView view);
        if (view is not null)
        {
            view.SetCaretPos(location.Line, location.Column);
            view.CenterLines(location.Line, 1);
        }
    }

    public void OpenSettings()
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        _package.ShowOptionPage(typeof(OptionsPage));
    }

    public void ShowMessage(string message)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        VsShellUtilities.ShowMessageBox(_serviceProvider, message, "RealizeJSON", OLEMSGICON.OLEMSGICON_INFO, OLEMSGBUTTON.OLEMSGBUTTON_OK, OLEMSGDEFBUTTON.OLEMSGDEFBUTTON_FIRST);
    }

    public static string FormatLocation(SourceLocation location) =>
        location.FilePath + "(" + (location.Line + 1).ToString(CultureInfo.InvariantCulture) + ")";
}
