using Microsoft.UI;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Scriblism.Core;
using Windows.ApplicationModel.DataTransfer;
using Windows.System;
using Windows.UI;
using Windows.UI.ViewManagement;

namespace Scriblism.App;

internal sealed class EditorSession : IDisposable
{
    public string? Path { get; set; }
    public string Name { get; set; }
    public DocumentBuffer Buffer { get; }
    public Language Language { get; set; }
    public string EncodingName { get; set; } = "utf-8";
    public string NewLine { get; set; } = "\n";
    public bool Bom { get; set; }
    public string? ExpectedHash { get; set; }
    public bool LiveMarkdown { get; set; } = true;
    public bool MixedNewLines { get; set; }
    public bool IsComposing { get; private set; }
    public RichEditBox Editor { get; }
    public TabViewItem Tab { get; }
    public Grid View { get; }
    public IReadOnlyList<OutlineEntry> Outline { get; private set; } = [];
    public Action<EditorSession>? Changed { get; set; }
    public Action<EditorSession>? SelectionMoved { get; set; }
    public Action<EditorSession>? ModeChanged { get; set; }
    public Action<string>? Error { get; set; }
    private readonly Microsoft.UI.Dispatching.DispatcherQueueTimer _formatTimer;
    private readonly Microsoft.UI.Dispatching.DispatcherQueueTimer _captureTimer;
    private CancellationTokenSource? _formatCancellation;
    private MarkdownLayout? _markdown;
    private bool _programmatic;
    private bool _disposed;
    private const double InterfaceScale = 0.87;
    private double _fontSize = 15 * InterfaceScale;
    private bool _wrap = true;
    private string _sourceFonts = EditorSettings.DefaultSourceFonts;
    private string _markdownFonts = EditorSettings.DefaultMarkdownFonts;
    private readonly AccessibilitySettings _accessibility = new();
    private readonly LineNumberGutter _gutter;
    private readonly MarkdownFlowView _preview;
    private string? _largePresentation;
    private int _lineIndexRevision = -1;
    private int[] _lineStarts = [0];
    public bool IsMarkdown => Language.Id == "markdown";
    public int SelectionStart => Math.Clamp(Editor.Document.Selection.StartPosition, 0, Buffer.Text.Length);
    public int SelectionEnd => Math.Clamp(Editor.Document.Selection.EndPosition, 0, Buffer.Text.Length);

    public EditorSession(string name, string text = "", string? path = null)
    {
        TextFileStore.ValidateEditableText(text);
        Name = name; Path = path; Buffer = new(text); Language = Languages.Detect(path ?? name);
        Editor = new RichEditBox
        {
            AcceptsReturn = true, IsSpellCheckEnabled = false, IsTextPredictionEnabled = false,
            MaxLength = TextFileStore.MaximumBytes,
            FontFamily = new FontFamily("Cascadia Mono, Consolas"), FontSize = _fontSize,
            CharacterSpacing = 8, TextWrapping = text.Length > SyntaxHighlighter.MaximumHighlightLength ? TextWrapping.NoWrap : TextWrapping.Wrap,
            Padding = new Thickness(14, 8, 14, 20),
            BorderThickness = new Thickness(0), CornerRadius = new CornerRadius(0),
            HorizontalAlignment = HorizontalAlignment.Stretch, VerticalAlignment = VerticalAlignment.Stretch,
            DisabledFormattingAccelerators = DisabledFormattingAccelerators.All,
            ClipboardCopyFormat = RichEditClipboardFormat.PlainText
        };
        AutomationProperties.SetName(Editor, "文件編輯區");
        AutomationProperties.SetAutomationId(Editor, "DocumentEditor");
        // Theme resources stay live when Windows or the app theme changes.
        Editor.Background = new SolidColorBrush(Colors.Transparent);
        Editor.Resources["TextControlBackground"] = new SolidColorBrush(Colors.Transparent);
        Editor.Resources["TextControlBackgroundFocused"] = new SolidColorBrush(Colors.Transparent);
        Editor.Resources["TextControlBackgroundPointerOver"] = new SolidColorBrush(Colors.Transparent);
        Editor.Resources["TextControlBorderBrushFocused"] = new SolidColorBrush(Colors.Transparent);
        Editor.Document.UndoLimit = 0; // Text history is independent of presentation changes.
        ScrollViewer.SetHorizontalScrollBarVisibility(Editor, ScrollBarVisibility.Auto);
        ScrollViewer.SetVerticalScrollBarVisibility(Editor, ScrollBarVisibility.Auto);
        Editor.Document.SetText(TextSetOptions.None, text.Replace('\n', '\r'));
        if (ReadNativeText() != text) throw new IOException("無法完整讀取這份文字，檔案未開啟。");
        Editor.Document.Selection.SetRange(0, 0);
        _gutter = new LineNumberGutter(Editor, Buffer);
        var host = new Grid();
        host.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        host.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        host.Children.Add(_gutter); Grid.SetColumn(Editor, 1); host.Children.Add(Editor);
        _preview = new MarkdownFlowView(EnterSourceAt);
        Grid.SetColumn(_preview.Scroller, 1); host.Children.Add(_preview.Scroller);
        View = host;
        Tab = new TabViewItem { Header = name, IconSource = new FontIconSource { Glyph = "\uE8A5" }, Tag = this, FontSize = 12, MinWidth = 174, MaxWidth = 208, MinHeight = 35 };
        ToolTipService.SetToolTip(Tab, path ?? name);
        _formatTimer = Editor.DispatcherQueue.CreateTimer();
        _formatTimer.Interval = TimeSpan.FromMilliseconds(220);
        _formatTimer.IsRepeating = false;
        _formatTimer.Tick += async (_, _) => await FormatAsync();
        _captureTimer = Editor.DispatcherQueue.CreateTimer();
        _captureTimer.Interval = TimeSpan.FromMilliseconds(300); _captureTimer.IsRepeating = false;
        _captureTimer.Tick += (_, _) => CaptureText();
        Editor.TextChanged += (_, _) =>
        {
            if (_programmatic || _disposed || IsComposing) return;
            if (Buffer.Text.Length <= SyntaxHighlighter.MaximumHighlightLength) CaptureText();
            else { _captureTimer.Stop(); _captureTimer.Start(); }
        };
        Editor.SelectionChanged += (_, _) =>
        {
            if (_programmatic || _disposed || IsComposing) return;
            Buffer.RememberCaret(SelectionEnd);
            UpdateMarkers();
            _gutter.Refresh(); SelectionMoved?.Invoke(this);
        };
        Editor.TextCompositionStarted += (_, _) => { IsComposing = true; _formatTimer.Stop(); _formatCancellation?.Cancel(); Buffer.BreakUndoGroup(); };
        Editor.TextCompositionEnded += (_, _) => { IsComposing = false; CaptureText(); Buffer.BreakUndoGroup(); ScheduleFormat(); };
        Editor.ActualThemeChanged += (_, _) => ScheduleFormat();
        Editor.Loaded += (_, _) => { ScheduleFormat(); Focus(); };
        Editor.Paste += async (_, args) => { args.Handled = true; await PasteAsync(); };
        Editor.PreviewKeyDown += OnKeyDown;
        var menu = new MenuFlyout();
        AddMenu("復原", Undo); AddMenu("重做", Redo);
        menu.Items.Add(new MenuFlyoutSeparator());
        AddMenu("剪下", () => Copy(true)); AddMenu("複製", () => Copy(false));
        AddMenu("貼上純文字", async () => await PasteAsync()); AddMenu("全選", SelectAll);
        Editor.ContextFlyout = menu;
        void AddMenu(string label, Action action) { var item = new MenuFlyoutItem { Text = label }; item.Click += (_, _) => action(); menu.Items.Add(item); }
    }

    public string ReadNativeText()
    {
        Editor.Document.GetText(TextGetOptions.AllowFinalEop | TextGetOptions.UseLf, out var text);
        text = TextFileStore.Normalize(text);
        // RichEdit owns one mandatory final paragraph; user-authored final newlines precede it.
        return text.EndsWith('\n') ? text[..^1] : text;
    }
    public void CaptureText()
    {
        if (_programmatic || _disposed || IsComposing) return;
        _captureTimer.Stop();
        var text = ReadNativeText();
        if (Buffer.Update(text, Math.Clamp(Editor.Document.Selection.EndPosition, 0, text.Length)))
        {
            RefreshHeader(); _markdown = null; Changed?.Invoke(this); ScheduleFormat();
        }
    }
    public (int Line, int Column) GetLineColumn(int position)
    {
        if (_lineIndexRevision != Buffer.Revision)
        {
            var starts = new List<int> { 0 };
            var text = Buffer.Text;
            for (var i = 0; i < text.Length; i++) if (text[i] == '\n') starts.Add(i + 1);
            _lineStarts = starts.ToArray(); _lineIndexRevision = Buffer.Revision;
        }
        position = Math.Clamp(position, 0, Buffer.Text.Length);
        var index = Array.BinarySearch(_lineStarts, position);
        if (index < 0) index = ~index - 1;
        return (index + 1, position - _lineStarts[index] + 1);
    }
    public void RefreshHeader()
    {
        Tab.Header = Name + (Buffer.IsDirty ? " •" : "");
        AutomationProperties.SetName(Tab, Name + (Buffer.IsDirty ? "，尚未儲存" : ""));
        ToolTipService.SetToolTip(Tab, Path ?? Name);
    }
    public void ApplySettings(double fontSize, bool wrap, string sourceFonts, string markdownFonts)
    {
        _fontSize = Math.Clamp(fontSize, 10, 32) * InterfaceScale; _wrap = wrap;
        _sourceFonts = EditorSettings.NormalizeFonts(sourceFonts, EditorSettings.DefaultSourceFonts);
        _markdownFonts = EditorSettings.NormalizeFonts(markdownFonts, EditorSettings.DefaultMarkdownFonts);
        Editor.FontSize = _fontSize;
        Editor.FontFamily = new FontFamily(IsMarkdown && LiveMarkdown ? _markdownFonts : _sourceFonts);
        Editor.TextWrapping = Buffer.Text.Length <= SyntaxHighlighter.MaximumHighlightLength && (IsMarkdown && LiveMarkdown || wrap)
            ? TextWrapping.Wrap : TextWrapping.NoWrap;
        ScheduleFormat();
    }
    public void ScheduleFormat()
    {
        if (_disposed || IsComposing) return;
        UpdateMode();
        _formatCancellation?.Cancel(); _formatTimer.Stop(); _formatTimer.Start();
    }

    public async Task FormatAsync()
    {
        if (_disposed || IsComposing) return;
        _formatTimer.Stop();
        _formatCancellation?.Cancel(); _formatCancellation?.Dispose();
        if (Buffer.Text.Length > SyntaxHighlighter.MaximumHighlightLength)
        {
            _gutter.Visibility = Visibility.Collapsed;
            Editor.TextWrapping = TextWrapping.NoWrap;
            return;
        }
        _formatCancellation = new(); var cancellation = _formatCancellation.Token;
        var text = Buffer.Text; var revision = Buffer.Revision; var language = Language;
        var presentationKey = $"{language.Id}:{LiveMarkdown}:{_fontSize}:{_wrap}:{_sourceFonts}:{_markdownFonts}:{Editor.ActualTheme}:{_accessibility.HighContrast}";
        if (text.Length > SyntaxHighlighter.MaximumHighlightLength && _largePresentation == presentationKey) return;
        _largePresentation = text.Length > SyntaxHighlighter.MaximumHighlightLength ? presentationKey : null;
        try
        {
            var markdown = IsMarkdown ? await Task.Run(() => MarkdownPresentation.Parse(text, cancellation), cancellation) : null;
            var tokens = IsMarkdown ? markdown!.CodeTokens : await Task.Run(() => SyntaxHighlighter.Highlight(text, language, cancellation), cancellation);
            if (_disposed || cancellation.IsCancellationRequested || revision != Buffer.Revision || IsComposing) return;
            _markdown = markdown; Outline = markdown?.Outline ?? [];
            ApplyFormatting(text, tokens, markdown);
            if (IsMarkdown && LiveMarkdown) _preview.Render(text, _fontSize, _markdownFonts);
            UpdateMode();
            _gutter.Refresh(); SelectionMoved?.Invoke(this);
        }
        catch (OperationCanceledException) { }
        catch (Exception e) { App.Log(e); Error?.Invoke("無法更新語法顏色；文字仍可編輯與儲存。"); }
    }

    private void ApplyFormatting(string text, IReadOnlyList<Token> tokens, MarkdownLayout? markdown)
    {
        var document = Editor.Document;
        var dark = Editor.ActualTheme == ElementTheme.Dark;
        var highContrast = _accessibility.HighContrast;
        var foreground = Editor.Foreground is SolidColorBrush brush ? brush.Color : dark ? Colors.White : Colors.Black;
        var source = !(IsMarkdown && LiveMarkdown);
        _programmatic = true;
        document.BatchDisplayUpdates();
        try
        {
            _gutter.Visibility = source && text.Length <= SyntaxHighlighter.MaximumHighlightLength ? Visibility.Visible : Visibility.Collapsed;
            Editor.Padding = source ? new Thickness(7, 8, 14, 20) : new Thickness(14, 8, 14, 20);
            Editor.TextWrapping = !source || _wrap ? TextWrapping.Wrap : TextWrapping.NoWrap;
            Editor.FontFamily = new FontFamily(source ? _sourceFonts : _markdownFonts);
            document.DefaultTabStop = (float)(_fontSize * .6 * 4 * .75);
            var range = document.GetRange(0, text.Length);
            var format = range.CharacterFormat;
            format.Hidden = FormatEffect.Off; format.Bold = FormatEffect.Off; format.Italic = FormatEffect.Off;
            format.Strikethrough = FormatEffect.Off; format.Underline = UnderlineType.None;
            format.Size = (float)(_fontSize * 0.75); // TOM sizes are points; WinUI FontSize is DIPs.
            format.Name = (source ? _sourceFonts : _markdownFonts).Split(',')[0].Trim();
            format.Spacing = source ? 0 : 0.1f;
            format.ForegroundColor = foreground;
            format.BackgroundColor = Colors.Transparent;
            range.ParagraphFormat.SetLineSpacing(LineSpacingRule.AtLeast, (float)(_fontSize * 1.04));
            if (markdown is not null)
            {
                foreach (var span in markdown.Spans.Where(x => x.Style != MarkdownStyle.Marker))
                {
                    var f = document.GetRange(span.Start, span.Start + span.Length).CharacterFormat;
                    switch (span.Style)
                    {
                        case MarkdownStyle.Heading:
                            f.Bold = FormatEffect.On;
                            if (!source) f.Size = (float)(_fontSize * 0.75 * (span.Level == 1 ? 1.9 : span.Level == 2 ? 1.5 : 1.18));
                            break;
                        case MarkdownStyle.Bold: f.Bold = FormatEffect.On; break;
                        case MarkdownStyle.Italic: f.Italic = FormatEffect.On; break;
                        case MarkdownStyle.Strike: f.Strikethrough = FormatEffect.On; break;
                        case MarkdownStyle.Code:
                            f.Name = _sourceFonts.Split(',')[0].Trim();
                            if (!highContrast) { f.BackgroundColor = dark ? ColorOf(0x292D33) : ColorOf(0xEDF1F5); f.ForegroundColor = dark ? ColorOf(0xD7BA7D) : ColorOf(0x795E26); }
                            break;
                        case MarkdownStyle.Quote:
                            f.Italic = FormatEffect.On;
                            if (!highContrast) f.ForegroundColor = dark ? ColorOf(0xB8C2CC) : ColorOf(0x53616D);
                            break;
                        case MarkdownStyle.Link:
                            f.Underline = UnderlineType.Single;
                            if (!highContrast) f.ForegroundColor = dark ? ColorOf(0x7DBBFF) : ColorOf(0x005FB8);
                            break;
                    }
                }
            }
            if (!highContrast)
                foreach (var token in tokens)
                    document.GetRange(token.Start, token.Start + token.Length).CharacterFormat.ForegroundColor = TokenColor(token.Kind, dark);
            ApplyMarkers();
        }
        finally { document.ApplyDisplayUpdates(); _programmatic = false; }
    }

    private void UpdateMarkers()
    {
        if (_markdown is null || IsComposing || _programmatic || _disposed) return;
        _programmatic = true; Editor.Document.BatchDisplayUpdates();
        try { ApplyMarkers(); }
        finally { Editor.Document.ApplyDisplayUpdates(); _programmatic = false; }
    }
    private void ApplyMarkers()
    {
        if (_markdown is null) return;
        var start = SelectionStart; var end = SelectionEnd;
        foreach (var span in _markdown.Spans.Where(s => s.Style == MarkdownStyle.Marker))
        {
            var format = Editor.Document.GetRange(span.Start, span.Start + span.Length).CharacterFormat;
            format.Hidden = LiveMarkdown && MarkdownPresentation.ShouldHide(span, Buffer.Text, start, end) ? FormatEffect.On : FormatEffect.Off;
            if (!_accessibility.HighContrast) format.ForegroundColor = Editor.ActualTheme == ElementTheme.Dark ? ColorOf(0xA6ADB5) : ColorOf(0x657180);
        }
    }
    private static Color ColorOf(uint rgb) => Color.FromArgb(255, (byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);
    private static Color TokenColor(TokenKind kind, bool dark) => ColorOf((kind, dark) switch
    {
        (TokenKind.Keyword, true) => 0x80BFFF, (TokenKind.Keyword, false) => 0x005DB8,
        (TokenKind.String, true) => 0xE7AE93, (TokenKind.String, false) => 0xA13D18,
        (TokenKind.Comment, true) => 0x91B982, (TokenKind.Comment, false) => 0x467436,
        (TokenKind.Number, true) => 0xB5D5A4, (TokenKind.Number, false) => 0x156F56,
        (TokenKind.Type, true) => 0x65D8C3, (TokenKind.Type, false) => 0x00756B,
        (TokenKind.Function, true) => 0xE0CE91, (TokenKind.Function, false) => 0x765E0B,
        (TokenKind.Property, true) => 0xB7DFFF, (TokenKind.Property, false) => 0x7351A1,
        (TokenKind.Added, true) => 0x99D894, (TokenKind.Added, false) => 0x247328,
        (TokenKind.Removed, true) => 0xFF9B97, (TokenKind.Removed, false) => 0xB32828,
        (_, true) => 0xD5D9DF, _ => 0x414954
    });

    public void ReplaceSelection(string text)
    {
        if (IsComposing) return;
        var start = SelectionStart; var end = SelectionEnd;
        Buffer.RememberCaret(end); Buffer.BreakUndoGroup();
        var normalized = TextFileStore.Normalize(text);
        try { TextFileStore.ValidateEditableText(normalized); }
        catch (IOException error) { Error?.Invoke(error.Message); return; }
        var next = Buffer.Text.Remove(start, end - start).Insert(start, normalized);
        if (next.Length > TextFileStore.MaximumBytes) { Error?.Invoke("貼上內容太大，文件上限為 16 MiB。"); return; }
        _programmatic = true;
        try
        {
            Editor.Document.Selection.SetText(TextSetOptions.None, normalized.Replace('\n', '\r'));
            if (ReadNativeText() != next)
            {
                Editor.Document.SetText(TextSetOptions.None, Buffer.Text.Replace('\n', '\r'));
                Editor.Document.Selection.SetRange(start, end);
                Error?.Invoke("部分字元無法貼上，這次貼上已取消。"); return;
            }
            Editor.Document.Selection.SetRange(start + normalized.Length, start + normalized.Length);
            Buffer.Update(next, start + normalized.Length, false);
        }
        finally { _programmatic = false; }
        RefreshHeader(); _markdown = null; Changed?.Invoke(this); ScheduleFormat();
    }
    public void ReplaceDocument(string text, int caret)
    {
        TextFileStore.ValidateEditableText(text);
        var start = SelectionStart; var end = SelectionEnd;
        _programmatic = true;
        try
        {
            Editor.Document.SetText(TextSetOptions.None, text.Replace('\n', '\r'));
            if (ReadNativeText() != text)
            {
                Editor.Document.SetText(TextSetOptions.None, Buffer.Text.Replace('\n', '\r'));
                Editor.Document.Selection.SetRange(start, end);
                throw new IOException("無法完整保留取代後的文字，這次取代未執行。");
            }
            caret = Math.Clamp(caret, 0, text.Length);
            Editor.Document.Selection.SetRange(caret, caret);
            Buffer.BreakUndoGroup(); Buffer.Update(text, caret, false);
        }
        finally { _programmatic = false; }
        _markdown = null; RefreshHeader(); Changed?.Invoke(this); ScheduleFormat();
    }
    private void LoadBuffer(int caret)
    {
        _programmatic = true;
        try { Editor.Document.SetText(TextSetOptions.None, Buffer.Text.Replace('\n', '\r')); Editor.Document.Selection.SetRange(caret, caret); }
        finally { _programmatic = false; }
        _markdown = null; RefreshHeader(); Changed?.Invoke(this); ScheduleFormat();
    }
    public void Undo() { if (!IsComposing) { var result = Buffer.Undo(); LoadBuffer(result.Caret); Focus(); } }
    public void Redo() { if (!IsComposing) { var result = Buffer.Redo(); LoadBuffer(result.Caret); Focus(); } }
    private void UpdateMode()
    {
        var preview = IsMarkdown && LiveMarkdown;
        Editor.Visibility = preview ? Visibility.Collapsed : Visibility.Visible;
        _preview.Scroller.Visibility = preview ? Visibility.Visible : Visibility.Collapsed;
        if (preview) _gutter.Visibility = Visibility.Collapsed;
    }
    private void EnterSourceAt(int offset)
    {
        LiveMarkdown = false;
        UpdateMode(); ModeChanged?.Invoke(this); ScheduleFormat();
        Select(offset, 0);
    }
    public void Focus()
    {
        if (IsMarkdown && LiveMarkdown) _preview.Scroller.Focus(FocusState.Programmatic);
        else Editor.Focus(FocusState.Programmatic);
    }
    public void Select(int start, int length)
    {
        start = Math.Clamp(start, 0, Buffer.Text.Length); length = Math.Clamp(length, 0, Buffer.Text.Length - start);
        Editor.Document.Selection.SetRange(start, start + length); UpdateMarkers();
        if (IsMarkdown && LiveMarkdown) _preview.ScrollTo(start);
        else Editor.Document.Selection.ScrollIntoView(PointOptions.Start);
        Focus();
    }
    public void SelectAll() => Select(0, Buffer.Text.Length);
    public void Copy(bool cut)
    {
        try
        {
            var start = SelectionStart; var end = SelectionEnd; if (start == end) return;
            var package = new DataPackage(); package.SetText(Buffer.Text[start..end].Replace("\n", NewLine, StringComparison.Ordinal)); Clipboard.SetContent(package);
            if (cut) ReplaceSelection("");
        }
        catch (Exception e) { App.Log(e); Error?.Invoke("無法存取剪貼簿，請重試。"); }
    }
    public async Task PasteAsync()
    {
        try
        {
            var package = Clipboard.GetContent();
            if (package.Contains(StandardDataFormats.Text)) { var text = await package.GetTextAsync(); if (!_disposed) ReplaceSelection(text); }
        }
        catch (Exception e) { App.Log(e); Error?.Invoke("無法讀取剪貼簿，請重試。"); }
    }
    public void InsertMarkdown(string command)
    {
        if (!IsMarkdown || IsComposing) return;
        if (LiveMarkdown) EnterSourceAt(SelectionStart);
        var start = SelectionStart; var end = SelectionEnd; var selected = Buffer.Text[start..end];
        var replacement = command switch
        {
            "# " => "# " + selected,
            "link" => "[" + (selected.Length > 0 ? selected : "連結文字") + "](https://)",
            "code" => "\n```\n" + selected + "\n```\n",
            _ => command + (selected.Length > 0 ? selected : "文字") + command
        };
        ReplaceSelection(replacement);
        if (command is "**" or "*" or "~~" or "`") Select(start + command.Length, selected.Length > 0 ? selected.Length : 2);
        else Focus();
    }
    private async void OnKeyDown(object sender, KeyRoutedEventArgs args)
    {
        if (IsComposing) return;
        var control = (Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Control) & Windows.UI.Core.CoreVirtualKeyStates.Down) != 0;
        var shift = (Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Shift) & Windows.UI.Core.CoreVirtualKeyStates.Down) != 0;
        if (control)
        {
            switch (args.Key)
            {
                case VirtualKey.Z: args.Handled = true; if (shift) Redo(); else Undo(); return;
                case VirtualKey.Y: args.Handled = true; Redo(); return;
                case VirtualKey.C: args.Handled = true; Copy(false); return;
                case VirtualKey.X: args.Handled = true; Copy(true); return;
                case VirtualKey.V: args.Handled = true; await PasteAsync(); return;
                case VirtualKey.A: args.Handled = true; SelectAll(); return;
                case VirtualKey.I: args.Handled = true; InsertMarkdown("*"); return;
                case VirtualKey.B when shift: args.Handled = true; InsertMarkdown("**"); return;
            }
        }
        if (args.Key == VirtualKey.Tab && !control && !shift)
        {
            args.Handled = true; ReplaceSelection("    ");
        }
        else if (args.Key == VirtualKey.Enter && !control && !shift)
        {
            var position = SelectionStart; var lineStart = position == 0 ? 0 : Buffer.Text.LastIndexOf('\n', position - 1) + 1;
            var line = Buffer.Text[lineStart..position]; var indent = new string(line.TakeWhile(c => c is ' ' or '\t').ToArray());
            // Continue indentation only; leave list renumbering and structural edits explicit.
            args.Handled = true; ReplaceSelection("\n" + indent);
        }
    }
    public RecoveryDocument Recovery() => new(Path, Name, Buffer.Text, Language.Id, EncodingName, Bom, NewLine, ExpectedHash);
    public void Dispose() { _disposed = true; _formatTimer.Stop(); _captureTimer.Stop(); _formatCancellation?.Cancel(); _formatCancellation?.Dispose(); Changed = null; SelectionMoved = null; ModeChanged = null; }
}
