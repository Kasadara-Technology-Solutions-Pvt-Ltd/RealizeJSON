using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using RealizeJSON.Core;

namespace RealizeJSON.UI.Tests;

internal sealed class FakeEditor : IEditorContext
{
    public Document? Document { get; set; }

    public int Caret { get; set; }

    public int Version { get; set; }

    public int Calls;

    public TaskCompletionSource? Gate { get; set; }

    public bool IsNonCSharpDocumentActive { get; set; }

    public async Task<EditorSnapshot?> GetActiveAsync(CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref Calls);
        var gate = Gate;
        if (gate is not null)
        {
            await gate.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        }

        return Document is null ? null : new EditorSnapshot(Document, Caret, Version);
    }
}

internal sealed class ImmediateDispatcher : IUiDispatcher
{
    public Task InvokeAsync(Action action, CancellationToken cancellationToken)
    {
        action();
        return Task.CompletedTask;
    }
}

internal sealed class FakeHost : IHostServices
{
    public string? Clipboard;
    public bool ConfirmResult = true;
    public List<string> Confirmations { get; } = new();
    public List<string> Messages { get; } = new();
    public SourceLocation? Navigated;
    public string? ExportPath = "/tmp/out.json";
    public string? Written;

    public void CopyToClipboard(string text) => Clipboard = text;

    public string? PromptForExportPath(string suggestedFileName) => ExportPath;

    public void WriteAllText(string path, string text) => Written = text;

    public bool Confirm(string message)
    {
        Confirmations.Add(message);
        return ConfirmResult;
    }

    public void NavigateTo(SourceLocation location) => Navigated = location;

    public void OpenSettings()
    {
    }

    public void ShowMessage(string message) => Messages.Add(message);
}

internal sealed class MemoryStore : ISettingsStore
{
    public Dictionary<string, string> Values { get; } = new();

    public string? Read(string key) => Values.TryGetValue(key, out var v) ? v : null;

    public void Write(string key, string value) => Values[key] = value;
}
