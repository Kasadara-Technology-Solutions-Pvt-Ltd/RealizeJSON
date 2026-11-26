using System;
using System.Runtime.InteropServices;
using Microsoft.VisualStudio.Imaging;
using Microsoft.VisualStudio.Shell;

namespace RealizeJSON.VisualStudio;

/// <summary>Dockable RealizeJSON tool window.</summary>
[Guid(PackageIds.ToolWindowGuidString)]
public sealed class PreviewToolWindow : ToolWindowPane
{
    private readonly PreviewControl? _control;

    /// <summary>Parameterless constructor required by the shell; content is created by the async factory.</summary>
    public PreviewToolWindow()
        : base(null)
    {
        Caption = "RealizeJSON";
        BitmapImageMoniker = KnownMonikers.JSONScript;
    }

    public PreviewToolWindow(object state)
        : this()
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        if (state is ToolWindowServices services)
        {
            Session = new PreviewSession(services);
            _control = new PreviewControl(Session, services);
            Content = _control;
        }
    }

    internal PreviewSession? Session { get; }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _control?.Detach();
            Session?.Dispose();
        }

        base.Dispose(disposing);
    }
}
