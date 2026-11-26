using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using RealizeJSON.Core;

namespace RealizeJSON.UI;

/// <summary>Snapshot of the active C# editor: document including unsaved changes and caret position.</summary>
/// <param name="Document">Roslyn document for the current text snapshot (unsaved edits included).</param>
/// <param name="CaretPosition">Caret offset in the document.</param>
/// <param name="TextVersion">Monotonic version of the text buffer.</param>
public sealed record EditorSnapshot(Document Document, int CaretPosition, int TextVersion);

/// <summary>Provides the active editor state. Implemented by the IDE host.</summary>
public interface IEditorContext
{
    /// <summary>Returns the active C# editor snapshot, or null when no C# editor is active.</summary>
    Task<EditorSnapshot?> GetActiveAsync(CancellationToken cancellationToken);

    /// <summary>True when a non-C# document is active (used for the unsupported-language state).</summary>
    bool IsNonCSharpDocumentActive { get; }
}

/// <summary>Marshals work to the UI thread. Implemented with JoinableTaskFactory in Visual Studio.</summary>
public interface IUiDispatcher
{
    Task InvokeAsync(System.Action action, CancellationToken cancellationToken);
}

/// <summary>IDE services used by commands.</summary>
public interface IHostServices
{
    void CopyToClipboard(string text);

    /// <summary>Asks the user for a save location; returns null when cancelled.</summary>
    string? PromptForExportPath(string suggestedFileName);

    void WriteAllText(string path, string text);

    /// <summary>Asks for confirmation (e.g. before copying incomplete output).</summary>
    bool Confirm(string message);

    void NavigateTo(SourceLocation location);

    void OpenSettings();

    void ShowMessage(string message);
}

/// <summary>Key/value persistence for per-user settings.</summary>
public interface ISettingsStore
{
    string? Read(string key);

    void Write(string key, string value);
}
