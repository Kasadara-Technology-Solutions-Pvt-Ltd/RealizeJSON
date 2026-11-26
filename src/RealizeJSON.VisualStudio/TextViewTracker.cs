using System;
using System.ComponentModel.Composition;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.Utilities;

namespace RealizeJSON.VisualStudio;

/// <summary>
/// Tracks the most recently focused document text view and forwards caret, text and focus changes. Events are raised on
/// the UI thread. No text content is logged or retained beyond the live view reference.
/// </summary>
[Export(typeof(TextViewTracker))]
[PartCreationPolicy(CreationPolicy.Shared)]
public sealed class TextViewTracker
{
    private IWpfTextView? _active;

    public event EventHandler? ActiveViewChanged;

    public event EventHandler? CaretMoved;

    public event EventHandler? TextChanged;

    /// <summary>The most recently focused document view (may be non-C#).</summary>
    public IWpfTextView? ActiveView => _active is { IsClosed: false } view ? view : null;

    public bool IsActiveViewCSharp => ActiveView?.TextBuffer.ContentType.IsOfType("CSharp") == true;

    internal void Register(IWpfTextView view)
    {
        view.GotAggregateFocus += OnGotFocus;
        view.Caret.PositionChanged += OnCaretPositionChanged;
        view.TextBuffer.PostChanged += OnBufferChanged;
        view.Closed += OnClosed;
        if (view.HasAggregateFocus)
        {
            SetActive(view);
        }
    }

    private void OnGotFocus(object sender, EventArgs e) => SetActive((IWpfTextView)sender);

    private void SetActive(IWpfTextView view)
    {
        if (!ReferenceEquals(_active, view))
        {
            _active = view;
            ActiveViewChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private void OnCaretPositionChanged(object sender, CaretPositionChangedEventArgs e)
    {
        if (ReferenceEquals(e.TextView, _active))
        {
            CaretMoved?.Invoke(this, EventArgs.Empty);
        }
    }

    private void OnBufferChanged(object sender, EventArgs e)
    {
        if (_active is not null && ReferenceEquals(sender, _active.TextBuffer))
        {
            TextChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private void OnClosed(object sender, EventArgs e)
    {
        var view = (IWpfTextView)sender;
        view.GotAggregateFocus -= OnGotFocus;
        view.Caret.PositionChanged -= OnCaretPositionChanged;
        view.TextBuffer.PostChanged -= OnBufferChanged;
        view.Closed -= OnClosed;
        if (ReferenceEquals(_active, view))
        {
            _active = null;
            ActiveViewChanged?.Invoke(this, EventArgs.Empty);
        }
    }
}

/// <summary>MEF listener registering document text views with the tracker.</summary>
[Export(typeof(IWpfTextViewCreationListener))]
[ContentType("text")]
[TextViewRole(PredefinedTextViewRoles.Document)]
internal sealed class TextViewCreationListener : IWpfTextViewCreationListener
{
    [Import]
    internal TextViewTracker Tracker { get; set; } = null!;

    public void TextViewCreated(IWpfTextView textView) => Tracker.Register(textView);
}
