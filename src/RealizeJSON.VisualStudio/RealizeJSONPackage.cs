using System;
using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.VisualStudio.ComponentModelHost;
using Microsoft.VisualStudio.LanguageServices;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio.Text.Classification;
using Task = System.Threading.Tasks.Task;

namespace RealizeJSON.VisualStudio;

/// <summary>
/// RealizeJSON package. Loads on demand (no auto-load): it is loaded when a RealizeJSON command is invoked or the tool
/// window is restored. All heavy work happens on background threads.
/// </summary>
[PackageRegistration(UseManagedResourcesOnly = true, AllowsBackgroundLoading = true)]
[ProvideBindingPath]
[InstalledProductRegistration("RealizeJSON", "See your JSON before your code runs.", "1.0.1")]
[ProvideMenuResource("Menus.ctmenu", 1)]
[ProvideToolWindow(typeof(PreviewToolWindow), Style = VsDockStyle.Tabbed, Window = "3ae79031-e1bc-11d0-8f78-00a0c9110057", Orientation = ToolWindowOrientation.Right)]
[ProvideOptionPage(typeof(OptionsPage), "RealizeJSON", "General", 0, 0, true)]
[Guid(PackageIds.PackageGuidString)]
public sealed class RealizeJSONPackage : AsyncPackage
{
    protected override async Task InitializeAsync(CancellationToken cancellationToken, IProgress<ServiceProgressData> progress)
    {
        await JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);
        await PreviewCommands.InitializeAsync(this);
    }

    public override IVsAsyncToolWindowFactory? GetAsyncToolWindowFactory(Guid toolWindowType) =>
        toolWindowType == typeof(PreviewToolWindow).GUID ? this : null;

    protected override string GetToolWindowTitle(Type toolWindowType, int id) =>
        toolWindowType == typeof(PreviewToolWindow) ? "RealizeJSON" : base.GetToolWindowTitle(toolWindowType, id);

    protected override async System.Threading.Tasks.Task<object?> InitializeToolWindowAsync(Type toolWindowType, int id, CancellationToken cancellationToken)
    {
        if (toolWindowType != typeof(PreviewToolWindow))
        {
            return null;
        }

        var componentModel = (IComponentModel?)await GetServiceAsync(typeof(SComponentModel));
        if (componentModel is null)
        {
            throw new InvalidOperationException("The Visual Studio component model is unavailable.");
        }

        var workspace = componentModel.GetService<VisualStudioWorkspace>();
        var tracker = componentModel.GetService<TextViewTracker>();
        var formatMaps = componentModel.GetService<IClassificationFormatMapService>();
        var registry = componentModel.GetService<IClassificationTypeRegistryService>();

        await JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);
        var store = new VsSettingsStore(this);
        return new ToolWindowServices(this, JoinableTaskFactory, workspace, tracker, formatMaps, registry, store);
    }

    /// <summary>Shows (and creates if needed) the tool window.</summary>
    public async System.Threading.Tasks.Task<PreviewToolWindow?> ShowPreviewAsync(bool selectCaretType)
    {
        try
        {
            var window = await ShowToolWindowAsync(typeof(PreviewToolWindow), 0, create: true, DisposalToken) as PreviewToolWindow;
            if (window is not null && selectCaretType)
            {
                window.Session?.PreviewTypeAtCaret();
            }

            return window;
        }
        catch (OperationCanceledException)
        {
            return null;
        }
#pragma warning disable CA1031 // Fault isolation: never let a tool-window failure crash the IDE.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            // Local ActivityLog only (no telemetry); the message never contains source text.
            ActivityLog.TryLogError("RealizeJSON", "Failed to open the RealizeJSON window: " + ex.GetType().Name + ": " + ex.Message);
            return null;
        }
    }
}

/// <summary>Services resolved on a background thread and handed to the tool window.</summary>
internal sealed class ToolWindowServices
{
    public ToolWindowServices(AsyncPackage package, Microsoft.VisualStudio.Threading.JoinableTaskFactory jtf, VisualStudioWorkspace workspace, TextViewTracker tracker, IClassificationFormatMapService formatMaps, IClassificationTypeRegistryService registry, VsSettingsStore store)
    {
        Package = package;
        JoinableTaskFactory = jtf;
        Workspace = workspace;
        Tracker = tracker;
        FormatMaps = formatMaps;
        Registry = registry;
        Store = store;
    }

    public AsyncPackage Package { get; }

    public Microsoft.VisualStudio.Threading.JoinableTaskFactory JoinableTaskFactory { get; }

    public VisualStudioWorkspace Workspace { get; }

    public TextViewTracker Tracker { get; }

    public IClassificationFormatMapService FormatMaps { get; }

    public IClassificationTypeRegistryService Registry { get; }

    public VsSettingsStore Store { get; }
}
