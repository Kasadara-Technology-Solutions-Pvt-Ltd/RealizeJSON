using System;
using System.Linq;
using System.Threading.Tasks;
using RealizeJSON.Core;
using RealizeJSON.Tests.Shared;

namespace RealizeJSON.UI.Tests;

public sealed class ViewModelTests
{
    private static PreviewResult Result(bool complete = true, SourceLocation? location = null) => new()
    {
        State = complete ? PreviewState.Live : PreviewState.Partial,
        Json = "{\"a\":1}",
        IsComplete = complete,
        Label = "Static preview · Sample values · System.Text.Json",
        PropertyCount = 1,
        SelectedTypeKey = "A@Lib",
        Inspector = new[]
        {
            new InspectorEntry("A", "T", "a", "int", "$.a", true, "Default rules", "", Classification.Resolved, location ?? SourceLocation.MetadataOnly, 0),
            new InspectorEntry("Secret", "T", null, "string", null, false, "[JsonIgnore]", "Excluded", Classification.Resolved, SourceLocation.MetadataOnly, 0),
        },
    };

    private static (PreviewViewModel Vm, FakeHost Host, MemoryStore Store) Create()
    {
        var (workspace, _) = WorkspaceFactory.CreateProject("Api");
        workspace.Dispose();
        var coordinator = new PreviewCoordinator(WorkspaceFactory.CreateEngine(), new FakeEditor(), () => new UserSettings());
        var host = new FakeHost();
        var store = new MemoryStore();
        return (new PreviewViewModel(coordinator, new ImmediateDispatcher(), host, store, new UserSettings()), host, store);
    }

    [Fact]
    public void Apply_updates_presentation_and_status_text()
    {
        var (vm, _, _) = Create();
        vm.Apply(new PreviewPresentation(Result(), PreviewState.Live, false, null));
        Assert.Equal("{\"a\":1}", vm.Json);
        Assert.Equal("Live", vm.StateText);
        Assert.Contains("1 properties", vm.StatusSummary, StringComparison.Ordinal);
        Assert.Contains("No diagnostics", vm.StatusSummary, StringComparison.Ordinal);
        Assert.Equal(2, vm.Inspector.Count);
        Assert.True(vm.CopyCommand.CanExecute(null));
    }

    [Fact]
    public void Copy_complete_json_without_prompt()
    {
        var (vm, host, _) = Create();
        vm.Apply(new PreviewPresentation(Result(), PreviewState.Live, false, null));
        vm.CopyCommand.Execute(null);
        Assert.Equal("{\"a\":1}", host.Clipboard);
        Assert.Empty(host.Confirmations);
    }

    [Fact]
    public void Copy_incomplete_json_requires_confirmation()
    {
        var (vm, host, _) = Create();
        vm.Apply(new PreviewPresentation(Result(complete: false), PreviewState.Partial, false, null));
        host.ConfirmResult = false;
        vm.CopyCommand.Execute(null);
        Assert.Null(host.Clipboard);
        Assert.Single(host.Confirmations);

        host.ConfirmResult = true;
        vm.ExportCommand.Execute(null);
        Assert.Equal("{\"a\":1}", host.Written);
    }

    [Fact]
    public void Empty_preview_cannot_be_copied()
    {
        var (vm, host, _) = Create();
        Assert.False(vm.CopyCommand.CanExecute(null));
        vm.CopyCommand.Execute(null);
        Assert.Null(host.Clipboard);
    }

    [Fact]
    public void Navigation_distinguishes_source_and_metadata()
    {
        var (vm, host, _) = Create();
        var source = new SourceLocation("/src/T.cs", 4, 8, SourceOrigin.Source);
        vm.Apply(new PreviewPresentation(Result(location: source), PreviewState.Live, false, null));
        vm.NavigateCommand.Execute(vm.Inspector[0]);
        Assert.Equal(source, host.Navigated);

        vm.NavigateCommand.Execute(vm.Inspector[1]);
        Assert.Single(host.Messages);
    }

    [Fact]
    public void Inspector_search_filters_rows()
    {
        var (vm, _, _) = Create();
        vm.Apply(new PreviewPresentation(Result(), PreviewState.Live, false, null));
        vm.SearchText = "secr";
        Assert.Single(vm.Inspector);
        vm.SearchText = string.Empty;
        Assert.Equal(2, vm.Inspector.Count);
    }

    [Fact]
    public void Settings_changes_are_persisted_and_serializer_specific_options_are_filtered()
    {
        var (vm, _, store) = Create();
        vm.SelectedNaming = vm.NamingChoices.First(c => c.Value == NamingPolicyKind.SnakeCaseUpper);
        vm.SelectedSerializer = vm.SerializerChoices.First(c => c.Value == SerializerKind.NewtonsoftJson);
        Assert.Null(vm.Settings.Overrides.PropertyNaming);
        Assert.DoesNotContain(vm.NamingChoices, c => c.Value == NamingPolicyKind.SnakeCaseUpper);
        var persisted = SettingsSerializer.FromJson(store.Values[SettingsSerializer.StoreKey]).Settings;
        Assert.Equal(SerializerKind.NewtonsoftJson, persisted.Serializer);
    }
}

public sealed class ViewModelPersistenceTests
{
    [Fact]
    public void Session_only_changes_are_not_persisted()
    {
        var store = new MemoryStore();
        var coordinator = new PreviewCoordinator(WorkspaceFactory.CreateEngine(), new FakeEditor(), () => new UserSettings());
        using var vm = new PreviewViewModel(coordinator, new ImmediateDispatcher(), new FakeHost(), store, new UserSettings()) { PersistChanges = false };
        vm.CollectionCount = 4;
        Assert.Empty(store.Values);
        Assert.Equal(4, vm.Settings.CollectionCount);
    }
}
