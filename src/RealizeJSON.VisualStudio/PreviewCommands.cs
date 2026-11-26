using System.ComponentModel.Design;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Threading;
using Task = System.Threading.Tasks.Task;

namespace RealizeJSON.VisualStudio;

/// <summary>View &gt; Other Windows &gt; RealizeJSON and the editor context-menu command "Preview as JSON".</summary>
internal static class PreviewCommands
{
    public static async Task InitializeAsync(RealizeJSONPackage package)
    {
        await package.JoinableTaskFactory.SwitchToMainThreadAsync(package.DisposalToken);
        if (await package.GetServiceAsync(typeof(IMenuCommandService)) is not OleMenuCommandService commands)
        {
            return;
        }

        commands.AddCommand(new MenuCommand(
            (_, _) => package.JoinableTaskFactory.RunAsync(() => package.ShowPreviewAsync(selectCaretType: false)).Task.Forget(),
            new CommandID(PackageIds.CommandSet, PackageIds.ShowToolWindowCommandId)));

        commands.AddCommand(new MenuCommand(
            (_, _) => package.JoinableTaskFactory.RunAsync(() => package.ShowPreviewAsync(selectCaretType: true)).Task.Forget(),
            new CommandID(PackageIds.CommandSet, PackageIds.PreviewAsJsonCommandId)));
    }
}
