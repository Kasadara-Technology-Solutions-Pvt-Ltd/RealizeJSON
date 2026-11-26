using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using RealizeJSON.Core;
using RealizeJSON.Tests.Shared;

namespace RealizeJSON.UI.Tests;

public sealed class CoordinatorTests : IDisposable
{
    private const string Source = """
        public class CustomerDTO
        {
            public int Id { get; set; }
            public string Name { get; set; } = "";
        }

        public class Other
        {
            public bool Flag { get; set; }
        }
        """;

    private readonly AdhocWorkspace _workspace;
    private readonly Document _document;
    private readonly FakeEditor _editor = new();
    private readonly ConcurrentQueue<PreviewPresentation> _published = new();
    private readonly PreviewCoordinator _coordinator;
    private UserSettings _settings = new() { DebounceMilliseconds = 30 };

    public CoordinatorTests()
    {
        (_workspace, var project) = WorkspaceFactory.CreateProject("Api", ("Types.cs", Source));
        _document = project.Documents.Single();
        _editor.Document = _document;
        _editor.Caret = WorkspaceFactory.Caret(_document, "CustomerDTO", 2);
        _coordinator = new PreviewCoordinator(WorkspaceFactory.CreateEngine(), _editor, () => _settings);
        _coordinator.Published += (_, p) => _published.Enqueue(p);
    }

    private async Task<PreviewPresentation> Settle()
    {
        for (var i = 0; i < 5; i++)
        {
            await _coordinator.WhenIdleAsync();
        }

        return _published.Last();
    }

    [Fact]
    public async Task Opening_publishes_live_preview()
    {
        _coordinator.SetVisible(true);
        var p = await Settle();
        Assert.Equal(PreviewState.Live, p.State);
        Assert.Contains("\"id\": 101", p.Result.Json, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Rapid_edits_are_debounced_into_one_analysis()
    {
        _coordinator.SetVisible(true);
        await Settle();
        var before = _published.Count;
        var callsBefore = _editor.Calls;
        for (var i = 0; i < 10; i++)
        {
            _coordinator.OnTextChanged();
        }

        await Settle();
        Assert.Equal(before + 1, _published.Count);
        Assert.Equal(callsBefore + 1, _editor.Calls);
    }

    [Fact]
    public async Task Superseded_results_never_overwrite_newer_results()
    {
        _settings = _settings with { DebounceMilliseconds = 0 };
        _coordinator.SetVisible(true);
        await Settle();

        // First request blocks inside the editor callback; a second request with different text completes first.
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _editor.Gate = gate;
        _coordinator.OnTextChanged();
        await Task.Delay(50, TestContext.Current.CancellationToken);
        _editor.Gate = null;
        _editor.Document = _document.WithText(SourceText.From(Source.Replace("public bool Flag", "public bool Flag2")));
        _editor.Caret = WorkspaceFactory.Caret(_editor.Document, "Other", 2);
        _coordinator.OnTextChanged();
        await Settle();
        gate.SetResult();
        await Task.Delay(100, TestContext.Current.CancellationToken);

        Assert.Contains("flag2", _published.Last().Result.Json, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Syntax_errors_keep_last_valid_preview_marked_stale()
    {
        _coordinator.SetVisible(true);
        var good = await Settle();

        _editor.Document = _document.WithText(SourceText.From(Source.Replace("public string Name { get; set; } = \"\";", "public string Name { get; set")));
        _editor.Version = 1;
        _coordinator.OnTextChanged();
        var stale = await Settle();

        Assert.Equal(PreviewState.Stale, stale.State);
        Assert.True(stale.IsStale);
        Assert.Equal(good.Result.Json, stale.Result.Json);
        Assert.Contains(stale.Result.Diagnostics, d => d.Id == Diagnostics.DiagnosticIds.SourceHasErrors);
    }

    [Fact]
    public async Task Hidden_window_suspends_analysis_and_refreshes_on_reopen()
    {
        _coordinator.SetVisible(true);
        await Settle();
        _coordinator.SetVisible(false);
        var count = _published.Count;
        var calls = _editor.Calls;
        _coordinator.OnTextChanged();
        _coordinator.OnCaretMoved();
        await Task.Delay(100, TestContext.Current.CancellationToken);
        Assert.Equal(count, _published.Count);
        Assert.Equal(calls, _editor.Calls);

        _coordinator.SetVisible(true);
        await Settle();
        Assert.Equal(count + 1, _published.Count);
    }

    [Fact]
    public async Task Caret_moves_inside_the_same_type_do_not_republish()
    {
        _coordinator.SetVisible(true);
        await Settle();
        var count = _published.Count;
        _editor.Caret = WorkspaceFactory.Caret(_document, "public int Id", 3);
        _coordinator.OnCaretMoved();
        await Settle();
        Assert.Equal(count, _published.Count);

        _editor.Caret = WorkspaceFactory.Caret(_document, "class Other", 7);
        _coordinator.OnCaretMoved();
        var moved = await Settle();
        Assert.Equal(count + 1, _published.Count);
        Assert.Equal("Other", moved.Result.SelectedType!.FullName);
    }

    [Fact]
    public async Task Pinned_type_survives_caret_navigation_and_refreshes_on_edit()
    {
        _coordinator.SetVisible(true);
        var first = await Settle();
        _coordinator.Pin(first.Result.SelectedTypeKey!);
        await Settle();

        _editor.Caret = WorkspaceFactory.Caret(_document, "class Other", 7);
        _coordinator.OnCaretMoved();
        _coordinator.OnActiveDocumentChanged();
        var pinned = await Settle();
        Assert.Equal("CustomerDTO", pinned.Result.SelectedType!.FullName);

        _editor.Document = _document.WithText(SourceText.From(Source.Replace("public int Id", "public long Id")));
        _coordinator.OnTextChanged();
        var edited = await Settle();
        Assert.Equal("CustomerDTO", edited.Result.SelectedType!.FullName);
        Assert.Contains(edited.Result.Inspector, r => r.ClrMember == "Id" && r.ClrType == "long");

        _coordinator.Unpin();
        var unpinned = await Settle();
        Assert.Equal("Other", unpinned.Result.SelectedType!.FullName);
    }

    [Fact]
    public async Task Workspace_changes_refresh_only_for_dependencies()
    {
        _coordinator.SetVisible(true);
        await Settle();
        var count = _published.Count;
        _coordinator.OnWorkspaceChanged(new[] { "Unrelated.cs" }, projectStructureChanged: false);
        await Settle();
        Assert.Equal(count, _published.Count);

        _coordinator.OnWorkspaceChanged(new[] { "Types.cs" }, projectStructureChanged: false);
        await Settle();
        Assert.Equal(count + 1, _published.Count);
    }

    [Fact]
    public async Task Closing_documents_and_solution_is_handled()
    {
        _coordinator.SetVisible(true);
        await Settle();
        _editor.Document = null;
        _coordinator.OnActiveDocumentChanged();
        var closed = await Settle();
        Assert.Equal(PreviewState.Stale, closed.State);

        _coordinator.OnSolutionClosed();
        var empty = await Settle();
        Assert.Equal(PreviewState.Empty, empty.State);
        Assert.Equal(string.Empty, empty.Result.Json);
    }

    [Fact]
    public async Task Non_csharp_document_shows_unsupported_language()
    {
        _editor.Document = null;
        _editor.IsNonCSharpDocumentActive = true;
        _coordinator.SetVisible(true);
        var p = await Settle();
        Assert.Equal(PreviewState.Unsupported, p.State);
    }

    [Fact]
    public async Task Dispose_stops_publication()
    {
        _coordinator.SetVisible(true);
        await Settle();
        var count = _published.Count;
        _coordinator.Dispose();
        _coordinator.OnTextChanged();
        _coordinator.Refresh();
        await Task.Delay(100, TestContext.Current.CancellationToken);
        Assert.Equal(count, _published.Count);
    }

    public void Dispose()
    {
        _coordinator.Dispose();
        _workspace.Dispose();
    }
}
