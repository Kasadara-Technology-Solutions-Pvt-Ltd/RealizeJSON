using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.VisualStudio.PlatformUI;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Text.Classification;
using RealizeJSON.Core;
using RealizeJSON.UI;

namespace RealizeJSON.VisualStudio;

/// <summary>
/// Tool-window content built in code (no XAML) using native WPF controls and Visual Studio theme resource keys.
/// Colors come from the IDE theme and the editor classification format map; brand colors are never used here.
/// </summary>
internal sealed class PreviewControl : UserControl
{
    private const int MaxHighlightedLines = 3000;
    private readonly PreviewSession _session;
    private readonly PreviewViewModel _vm;
    private readonly IClassificationFormatMap _formatMap;
    private readonly IClassificationTypeRegistryService _registry;
    private readonly RichTextBox _jsonView;

    public PreviewControl(PreviewSession session, ToolWindowServices services)
    {
        _session = session;
        _vm = session.ViewModel;
        _formatMap = services.FormatMaps.GetClassificationFormatMap("text");
        _registry = services.Registry;
        DataContext = _vm;
        SetResourceReference(BackgroundProperty, EnvironmentColors.ToolWindowBackgroundBrushKey);
        SetResourceReference(ForegroundProperty, EnvironmentColors.ToolWindowTextBrushKey);
        AutomationProperties.SetName(this, "RealizeJSON preview");

        _jsonView = CreateJsonView();
        var root = new DockPanel { LastChildFill = true };
        var header = CreateHeader();
        DockPanel.SetDock(header, Dock.Top);
        root.Children.Add(header);
        var status = CreateStatusBar();
        DockPanel.SetDock(status, Dock.Bottom);
        root.Children.Add(status);
        root.Children.Add(CreateTabs());
        Content = root;

        InputBindings.Add(new KeyBinding(_vm.RefreshCommand, Key.F5, ModifierKeys.None));
        InputBindings.Add(new KeyBinding(_vm.CopyCommand, Key.C, ModifierKeys.Control | ModifierKeys.Shift));

        _vm.PropertyChanged += OnViewModelChanged;
        _formatMap.ClassificationFormatMappingChanged += OnFormatMappingChanged;
        IsVisibleChanged += OnIsVisibleChanged;
        RenderJson();
    }

    public void Detach()
    {
        _vm.PropertyChanged -= OnViewModelChanged;
        _formatMap.ClassificationFormatMappingChanged -= OnFormatMappingChanged;
        IsVisibleChanged -= OnIsVisibleChanged;
    }

    private void OnIsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e) => _session.SetVisible((bool)e.NewValue);

    private void OnFormatMappingChanged(object? sender, EventArgs e) => RenderJson();

    private void OnViewModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (string.IsNullOrEmpty(e.PropertyName) || e.PropertyName == nameof(PreviewViewModel.Json) || e.PropertyName == nameof(PreviewViewModel.WordWrap))
        {
            RenderJson();
        }
    }

    // ---- Layout -----------------------------------------------------------------------------------------------

    private UIElement CreateHeader()
    {
        var panel = new StackPanel { Margin = new Thickness(6, 4, 6, 2) };

        var titleRow = new DockPanel { LastChildFill = true };
        var actions = new StackPanel { Orientation = Orientation.Horizontal };
        DockPanel.SetDock(actions, Dock.Right);
        actions.Children.Add(StateIndicator());
        actions.Children.Add(Toggle("_Pin", "Pin the selected type (keeps it while navigating)", nameof(PreviewViewModel.IsPinned), _vm.PinCommand));
        actions.Children.Add(Button("_Copy", "Copy JSON (Ctrl+Shift+C)", _vm.CopyCommand));
        titleRow.Children.Add(actions);
        var type = new TextBlock { FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 0, 6, 0) };
        type.SetBinding(TextBlock.TextProperty, new Binding(nameof(PreviewViewModel.SelectedTypeDisplay)));
        type.SetBinding(ToolTipProperty, new Binding(nameof(PreviewViewModel.ContextDisplay)));
        AutomationProperties.SetName(type, "Selected type");
        titleRow.Children.Add(type);
        panel.Children.Add(titleRow);

        var context = new TextBlock { Opacity = 0.8, TextTrimming = TextTrimming.CharacterEllipsis };
        context.SetBinding(TextBlock.TextProperty, new Binding(nameof(PreviewViewModel.ContextDisplay)));
        panel.Children.Add(context);

        var row1 = new WrapPanel { Margin = new Thickness(0, 4, 0, 0) };
        row1.Children.Add(Labeled("_Serializer", Combo(nameof(PreviewViewModel.SerializerChoices), nameof(PreviewViewModel.SelectedSerializer), "Serializer", 130)));
        row1.Children.Add(Labeled("P_rofile", Combo(nameof(PreviewViewModel.PresetChoices), nameof(PreviewViewModel.SelectedPreset), "Profile preset", 170)));
        row1.Children.Add(Labeled("_Naming", Combo(nameof(PreviewViewModel.NamingChoices), nameof(PreviewViewModel.SelectedNaming), "Property naming policy", 140)));
        row1.Children.Add(Button("_Settings", "Open RealizeJSON options", _vm.SettingsCommand));
        panel.Children.Add(row1);

        var row2 = new WrapPanel();
        row2.Children.Add(Labeled("_Values", Combo(nameof(PreviewViewModel.SampleModeChoices), nameof(PreviewViewModel.SelectedSampleMode), "Sample values mode", 120)));
        var count = new ComboBox { ItemsSource = new[] { 0, 1, 2, 3, 4, 5 }, MinWidth = 48, Margin = new Thickness(2) };
        count.SetBinding(Selector.SelectedItemProperty, new Binding(nameof(PreviewViewModel.CollectionCount)) { Mode = BindingMode.TwoWay });
        ApplyStyle(count, VsResourceKeys.ComboBoxStyleKey);
        AutomationProperties.SetName(count, "Collection sample count");
        row2.Children.Add(Labeled("Co_unt", count));
        row2.Children.Add(Check("_Enum strings", nameof(PreviewViewModel.EnumsAsStrings), "Write enums as strings (JsonStringEnumConverter / StringEnumConverter)"));
        row2.Children.Add(Check("_Formatted", nameof(PreviewViewModel.IsFormatted), "Formatted (indented) or compact output"));
        row2.Children.Add(Check("_Wrap", nameof(PreviewViewModel.WordWrap), "Wrap long lines"));
        row2.Children.Add(Button("Re_fresh", "Refresh now (F5)", _vm.RefreshCommand));
        row2.Children.Add(Button("E_xport…", "Export the preview as a .json file", _vm.ExportCommand));
        var saveProfile = Button("Save solution profile…", "Write the current profile to .realizejson.json next to the solution", new RelayCommand(_ => _session.SaveSolutionProfile()));
        row2.Children.Add(saveProfile);
        panel.Children.Add(row2);

        var row3 = new WrapPanel();
        var candidates = Combo(nameof(PreviewViewModel.Candidates), nameof(PreviewViewModel.SelectedCandidate), "Type selector", 200);
        candidates.DisplayMemberPath = nameof(CandidateInfo.Display);
        row3.Children.Add(Labeled("_Type", candidates));
        var variants = Combo(nameof(PreviewViewModel.Variants), nameof(PreviewViewModel.SelectedVariant), "Polymorphic variant", 180);
        variants.DisplayMemberPath = nameof(VariantInfo.Display);
        row3.Children.Add(Labeled("V_ariant", variants));
        panel.Children.Add(row3);

        var configuration = new TextBlock { Opacity = 0.8, TextWrapping = TextWrapping.Wrap };
        configuration.SetBinding(TextBlock.TextProperty, new Binding(nameof(PreviewViewModel.ConfigurationSummary)));
        panel.Children.Add(configuration);
        return panel;
    }

    private UIElement StateIndicator()
    {
        var text = new TextBlock { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0, 8, 0), FontWeight = FontWeights.SemiBold };
        text.SetBinding(TextBlock.TextProperty, new Binding(nameof(PreviewViewModel.StateText)) { StringFormat = "● {0}" });
        AutomationProperties.SetName(text, "Preview state");
        AutomationProperties.SetLiveSetting(text, AutomationLiveSetting.Polite);
        return text;
    }

    private UIElement CreateTabs()
    {
        var tabs = new TabControl { Margin = new Thickness(4, 2, 4, 2) };
        SetThemedBackground(tabs);
        tabs.Items.Add(new TabItem { Header = "JSON Preview", Content = _jsonView });
        tabs.Items.Add(new TabItem { Header = "Tree", Content = CreateTree() });
        tabs.Items.Add(new TabItem { Header = "Contract Inspector", Content = CreateInspector() });
        tabs.Items.Add(new TabItem { Header = "Diagnostics", Content = CreateDiagnostics() });
        foreach (TabItem item in tabs.Items)
        {
            AutomationProperties.SetName(item, (string)item.Header);
        }

        return tabs;
    }

    private RichTextBox CreateJsonView()
    {
        var box = new RichTextBox
        {
            IsReadOnly = true,
            IsReadOnlyCaretVisible = true,
            BorderThickness = new Thickness(0),
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            AcceptsTab = false,
        };
        SetThemedBackground(box);
        AutomationProperties.SetName(box, "JSON preview (read-only). Double-click or press Enter on a property to go to its declaration.");
        box.MouseDoubleClick += (_, _) => NavigateFromCaret(box);
        box.PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter)
            {
                NavigateFromCaret(box);
                e.Handled = true;
            }
        };
        return box;
    }

    private void NavigateFromCaret(RichTextBox box)
    {
        var paragraph = box.CaretPosition.Paragraph;
        if (paragraph is null)
        {
            return;
        }

        var index = 0;
        foreach (var block in box.Document.Blocks)
        {
            if (ReferenceEquals(block, paragraph))
            {
                _vm.NavigateCommand.Execute(index);
                return;
            }

            index++;
        }
    }

    private UIElement CreateTree()
    {
        var tree = new TreeView { BorderThickness = new Thickness(0) };
        SetThemedBackground(tree);
        tree.SetBinding(ItemsControl.ItemsSourceProperty, new Binding(nameof(PreviewViewModel.Tree)));
        var template = new HierarchicalDataTemplate(typeof(JsonTreeItem)) { ItemsSource = new Binding(nameof(JsonTreeItem.Children)) };
        var text = new FrameworkElementFactory(typeof(TextBlock));
        text.SetBinding(TextBlock.TextProperty, new Binding(nameof(JsonTreeItem.Label)));
        text.SetBinding(ToolTipProperty, new Binding(nameof(JsonTreeItem.Path)));
        template.VisualTree = text;
        tree.ItemTemplate = template;
        AutomationProperties.SetName(tree, "JSON tree");
        tree.MouseDoubleClick += (_, _) => _vm.NavigateCommand.Execute(tree.SelectedItem);
        return tree;
    }

    private UIElement CreateInspector()
    {
        var panel = new DockPanel();
        var search = new TextBox { Margin = new Thickness(2) };
        ApplyStyle(search, VsResourceKeys.TextBoxStyleKey);
        search.SetBinding(TextBox.TextProperty, new Binding(nameof(PreviewViewModel.SearchText)) { Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged });
        AutomationProperties.SetName(search, "Search members");
        var searchRow = Labeled("Searc_h", search);
        DockPanel.SetDock(searchRow, Dock.Top);
        panel.Children.Add(searchRow);

        var grid = Grid(nameof(PreviewViewModel.Inspector), "Serialization contract inspector");
        grid.Columns.Add(Column("Member", nameof(InspectorEntry.ClrMember)));
        grid.Columns.Add(Column("JSON name", nameof(InspectorEntry.JsonName)));
        grid.Columns.Add(Column("CLR type", nameof(InspectorEntry.ClrType)));
        grid.Columns.Add(Column("JSON path", nameof(InspectorEntry.JsonPath)));
        grid.Columns.Add(Column("Included", nameof(InspectorEntry.Included)));
        grid.Columns.Add(Column("Classification", nameof(InspectorEntry.Classification)));
        grid.Columns.Add(Column("Source", nameof(InspectorEntry.Source)));
        grid.Columns.Add(Column("Explanation", nameof(InspectorEntry.Explanation)));
        grid.Columns.Add(Column("Declared in", nameof(InspectorEntry.DeclaringType)));
        grid.MouseDoubleClick += (_, _) => _vm.NavigateCommand.Execute(grid.SelectedItem);
        grid.PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter)
            {
                _vm.NavigateCommand.Execute(grid.SelectedItem);
                e.Handled = true;
            }
        };
        panel.Children.Add(grid);
        return panel;
    }

    private UIElement CreateDiagnostics()
    {
        var grid = Grid(nameof(PreviewViewModel.Diagnostics), "Diagnostics");
        grid.Columns.Add(Column("Severity", nameof(DiagnosticItem.Severity)));
        grid.Columns.Add(Column("Code", nameof(DiagnosticItem.Id)));
        grid.Columns.Add(Column("Classification", nameof(DiagnosticItem.Classification)));
        grid.Columns.Add(Column("Message", nameof(DiagnosticItem.Message)));
        grid.Columns.Add(Column("Location", nameof(DiagnosticItem.Location)));
        return grid;
    }

    private UIElement CreateStatusBar()
    {
        var panel = new StackPanel { Margin = new Thickness(6, 2, 6, 4) };
        var label = new TextBlock { FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap };
        label.SetBinding(TextBlock.TextProperty, new Binding(nameof(PreviewViewModel.Label)));
        AutomationProperties.SetName(label, "Preview label");
        var summary = new TextBlock { TextWrapping = TextWrapping.Wrap, Opacity = 0.85 };
        summary.SetBinding(TextBlock.TextProperty, new Binding(nameof(PreviewViewModel.StatusSummary)));
        AutomationProperties.SetName(summary, "Preview status");
        AutomationProperties.SetLiveSetting(summary, AutomationLiveSetting.Polite);
        panel.Children.Add(label);
        panel.Children.Add(summary);
        return panel;
    }

    // ---- JSON rendering -----------------------------------------------------------------------------------------

    private void RenderJson()
    {
        var document = new FlowDocument { PagePadding = new Thickness(4) };
        var defaults = _formatMap.DefaultTextProperties;
        document.FontFamily = defaults.Typeface.FontFamily;
        document.FontSize = defaults.FontRenderingEmSize;
        document.SetResourceReference(FlowDocument.ForegroundProperty, EnvironmentColors.ToolWindowTextBrushKey);
        if (!_vm.WordWrap)
        {
            document.PageWidth = 20000;
        }

        var lines = _vm.Json.Split('\n');
        var highlight = lines.Length <= MaxHighlightedLines;
        var brushes = highlight ? new JsonBrushes(_formatMap, _registry) : null;
        foreach (var line in lines)
        {
            var paragraph = new Paragraph { Margin = new Thickness(0) };
            if (brushes is null)
            {
                paragraph.Inlines.Add(new Run(line));
            }
            else
            {
                JsonLineTokenizer.Append(paragraph.Inlines, line, brushes);
            }

            document.Blocks.Add(paragraph);
        }

        _jsonView.Document = document;
    }

    // ---- Helpers --------------------------------------------------------------------------------------------

    private static void SetThemedBackground(Control control)
    {
        control.SetResourceReference(BackgroundProperty, EnvironmentColors.ToolWindowBackgroundBrushKey);
        control.SetResourceReference(ForegroundProperty, EnvironmentColors.ToolWindowTextBrushKey);
    }

    private static void ApplyStyle(FrameworkElement element, object key) => element.SetResourceReference(StyleProperty, key);

    private static ComboBox Combo(string itemsPath, string selectedPath, string automationName, double minWidth)
    {
        var combo = new ComboBox { MinWidth = minWidth, Margin = new Thickness(2) };
        combo.SetBinding(ItemsControl.ItemsSourceProperty, new Binding(itemsPath));
        combo.SetBinding(Selector.SelectedItemProperty, new Binding(selectedPath) { Mode = BindingMode.TwoWay });
        ApplyStyle(combo, VsResourceKeys.ComboBoxStyleKey);
        AutomationProperties.SetName(combo, automationName);
        return combo;
    }

    private static UIElement Labeled(string text, Control target)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 8, 0) };
        var label = new Label { Content = text, Target = target, Padding = new Thickness(0, 0, 4, 0), VerticalAlignment = VerticalAlignment.Center };
        label.SetResourceReference(ForegroundProperty, EnvironmentColors.ToolWindowTextBrushKey);
        panel.Children.Add(label);
        panel.Children.Add(target);
        return panel;
    }

    private static Button Button(string text, string tooltip, ICommand command)
    {
        var button = new Button { Content = new AccessText { Text = text }, Command = command, ToolTip = tooltip, Margin = new Thickness(2), Padding = new Thickness(6, 1, 6, 1), MinWidth = 60 };
        ApplyStyle(button, VsResourceKeys.ButtonStyleKey);
        AutomationProperties.SetName(button, text.Replace("_", string.Empty));
        AutomationProperties.SetHelpText(button, tooltip);
        return button;
    }

    private UIElement Toggle(string text, string tooltip, string isCheckedPath, ICommand command)
    {
        var toggle = new ToggleButton { Content = new AccessText { Text = text }, ToolTip = tooltip, Margin = new Thickness(2), Padding = new Thickness(6, 1, 6, 1), Command = command };
        toggle.SetBinding(ToggleButton.IsCheckedProperty, new Binding(isCheckedPath) { Mode = BindingMode.OneWay });
        ApplyStyle(toggle, VsResourceKeys.ButtonStyleKey);
        AutomationProperties.SetName(toggle, text.Replace("_", string.Empty));
        AutomationProperties.SetHelpText(toggle, tooltip);
        return toggle;
    }

    private static CheckBox Check(string text, string path, string tooltip)
    {
        var check = new CheckBox { Content = new AccessText { Text = text }, ToolTip = tooltip, Margin = new Thickness(4, 2, 4, 2), VerticalAlignment = VerticalAlignment.Center };
        check.SetBinding(ToggleButton.IsCheckedProperty, new Binding(path) { Mode = BindingMode.TwoWay });
        ApplyStyle(check, VsResourceKeys.CheckBoxStyleKey);
        AutomationProperties.SetName(check, text.Replace("_", string.Empty));
        return check;
    }

    private static DataGrid Grid(string itemsPath, string automationName)
    {
        var grid = new DataGrid
        {
            AutoGenerateColumns = false,
            IsReadOnly = true,
            CanUserAddRows = false,
            SelectionMode = DataGridSelectionMode.Single,
            HeadersVisibility = DataGridHeadersVisibility.Column,
            GridLinesVisibility = DataGridGridLinesVisibility.None,
            BorderThickness = new Thickness(0),
        };
        SetThemedBackground(grid);
        grid.SetResourceReference(DataGrid.RowBackgroundProperty, EnvironmentColors.ToolWindowBackgroundBrushKey);
        grid.SetBinding(ItemsControl.ItemsSourceProperty, new Binding(itemsPath));
        AutomationProperties.SetName(grid, automationName);
        return grid;
    }

    private static DataGridTextColumn Column(string header, string path) => new() { Header = header, Binding = new Binding(path) };
}

/// <summary>Editor-theme brushes for JSON tokens (adapts to light, dark and high-contrast themes).</summary>
internal sealed class JsonBrushes
{
    public JsonBrushes(IClassificationFormatMap map, IClassificationTypeRegistryService registry)
    {
        Brush? Get(string name)
        {
            var type = registry.GetClassificationType(name);
            return type is null ? null : map.GetTextProperties(type).ForegroundBrush;
        }

        PropertyName = Get("identifier");
        String = Get("string");
        Number = Get("number");
        Keyword = Get("keyword");
        Punctuation = Get("operator");
    }

    public Brush? PropertyName { get; }

    public Brush? String { get; }

    public Brush? Number { get; }

    public Brush? Keyword { get; }

    public Brush? Punctuation { get; }
}

/// <summary>Splits one line of (valid) JSON text into colored runs.</summary>
internal static class JsonLineTokenizer
{
    public static void Append(InlineCollection inlines, string line, JsonBrushes brushes)
    {
        var i = 0;
        while (i < line.Length)
        {
            var c = line[i];
            if (c == '"')
            {
                var end = i + 1;
                while (end < line.Length && line[end] != '"')
                {
                    end += line[end] == '\\' ? 2 : 1;
                }

                end = Math.Min(end + 1, line.Length);
                var next = end;
                while (next < line.Length && line[next] == ' ')
                {
                    next++;
                }

                var isName = next < line.Length && line[next] == ':';
                Add(inlines, line.Substring(i, end - i), isName ? brushes.PropertyName : brushes.String);
                i = end;
            }
            else if (c == '-' || char.IsDigit(c))
            {
                var end = i + 1;
                while (end < line.Length && (char.IsDigit(line[end]) || line[end] is '.' or 'e' or 'E' or '+' or '-'))
                {
                    end++;
                }

                Add(inlines, line.Substring(i, end - i), brushes.Number);
                i = end;
            }
            else if (char.IsLetter(c))
            {
                var end = i + 1;
                while (end < line.Length && char.IsLetter(line[end]))
                {
                    end++;
                }

                Add(inlines, line.Substring(i, end - i), brushes.Keyword);
                i = end;
            }
            else
            {
                var end = i + 1;
                while (end < line.Length && line[end] is ' ' or '{' or '}' or '[' or ']' or ',' or ':')
                {
                    end++;
                }

                Add(inlines, line.Substring(i, end - i), brushes.Punctuation);
                i = end;
            }
        }
    }

    private static void Add(InlineCollection inlines, string text, Brush? brush)
    {
        var run = new Run(text);
        if (brush is not null)
        {
            run.Foreground = brush;
        }

        inlines.Add(run);
    }
}
