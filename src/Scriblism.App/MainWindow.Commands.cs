using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Scriblism.Core;
using Windows.System;

namespace Scriblism.App;

public sealed partial class MainWindow
{
    private void InstallShortcuts()
    {
        Root.PreviewKeyDown += OnWorkspaceKeyDown;
        Shortcut(VirtualKey.N, VirtualKeyModifiers.Control, () => OnNew(this, new()));
        Shortcut(VirtualKey.N, VirtualKeyModifiers.Control | VirtualKeyModifiers.Shift, () => OnNewMarkdown(this, new()));
        Shortcut(VirtualKey.O, VirtualKeyModifiers.Control, () => OnOpen(this, new()));
        Shortcut(VirtualKey.O, VirtualKeyModifiers.Control | VirtualKeyModifiers.Shift, () => OnOpenFolder(this, new()));
        Shortcut(VirtualKey.S, VirtualKeyModifiers.Control, () => OnSave(this, new()));
        Shortcut(VirtualKey.S, VirtualKeyModifiers.Control | VirtualKeyModifiers.Shift, () => OnSaveAs(this, new()));
        Shortcut(VirtualKey.W, VirtualKeyModifiers.Control, () => OnCloseTab(this, new()));
        Shortcut((VirtualKey)188, VirtualKeyModifiers.Control, () => OnSettings(this, new()));
        Shortcut(VirtualKey.F, VirtualKeyModifiers.Control, () => ShowFind(false));
        Shortcut(VirtualKey.H, VirtualKeyModifiers.Control, () => ShowFind(true));
        Shortcut(VirtualKey.G, VirtualKeyModifiers.Control, () => OnGoTo(this, new()));
        Shortcut(VirtualKey.B, VirtualKeyModifiers.Control, () => { SidebarMenu.IsChecked = !SidebarMenu.IsChecked; OnSidebar(this, new()); });
        Shortcut(VirtualKey.E, VirtualKeyModifiers.Control, () =>
        {
            if (Current?.IsMarkdown != true) return;
            OnMarkdownMode(this, new());
        });
        Shortcut(VirtualKey.Z, VirtualKeyModifiers.Menu, () => { WrapMenu.IsChecked = !WrapMenu.IsChecked; OnWrap(this, new()); });
        Shortcut(VirtualKey.F3, VirtualKeyModifiers.None, () => FindNext(false));
        Shortcut(VirtualKey.F3, VirtualKeyModifiers.Shift, () => FindNext(true));
        Shortcut(VirtualKey.F5, VirtualKeyModifiers.None, () => OnRefresh(this, new()));
        Shortcut(VirtualKey.Number0, VirtualKeyModifiers.Control, () => OnZoomReset(this, new()));
        Shortcut(VirtualKey.Add, VirtualKeyModifiers.Control, () => Zoom(1));
        Shortcut(VirtualKey.Subtract, VirtualKeyModifiers.Control, () => Zoom(-1));
        Shortcut((VirtualKey)187, VirtualKeyModifiers.Control, () => Zoom(1));
        Shortcut((VirtualKey)189, VirtualKeyModifiers.Control, () => Zoom(-1));
        // Ctrl+Tab is handled in the preview key route: RichEdit consumes Tab before accelerators.
        Shortcut(VirtualKey.Escape, VirtualKeyModifiers.None, () => { if (FindPanel.Visibility == Visibility.Visible) OnCloseFind(this, new()); else Current?.Focus(); });
    }
    private void OnWorkspaceKeyDown(object sender, KeyRoutedEventArgs args)
    {
        if (args.Key != VirtualKey.Tab || _closing || Current?.IsComposing == true) return;
        bool IsDown(VirtualKey key) => (Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(key) & Windows.UI.Core.CoreVirtualKeyStates.Down) != 0;
        if (!IsDown(VirtualKey.Control) || Tabs.TabItems.Count == 0) return;
        args.Handled = true;
        var delta = IsDown(VirtualKey.Shift) ? -1 : 1;
        Tabs.SelectedIndex = (Tabs.SelectedIndex + delta + Tabs.TabItems.Count) % Tabs.TabItems.Count;
        Current?.Focus();
    }
    private void Shortcut(VirtualKey key, VirtualKeyModifiers modifiers, Action action)
    {
        var shortcut = new KeyboardAccelerator { Key = key, Modifiers = modifiers };
        shortcut.Invoked += (_, args) => { if (!_closing && Current?.IsComposing != true) { action(); args.Handled = true; } };
        Root.KeyboardAccelerators.Add(shortcut);
    }
    private void OnUndo(object sender, RoutedEventArgs e) => Current?.Undo();
    private void OnRedo(object sender, RoutedEventArgs e) => Current?.Redo();
    private void OnCut(object sender, RoutedEventArgs e) => Current?.Copy(true);
    private void OnCopy(object sender, RoutedEventArgs e) => Current?.Copy(false);
    private async void OnPaste(object sender, RoutedEventArgs e) { if (Current is { } doc) await doc.PasteAsync(); }
    private void OnSelectAll(object sender, RoutedEventArgs e) => Current?.SelectAll();
    private void OnSidebar(object sender, RoutedEventArgs e) { _settings.SidebarVisible = SidebarMenu.IsChecked; SetSidebar(); _ = PersistSettings(); }
    private void SetSidebar()
    {
        Sidebar.Visibility = SidebarSplitter.Visibility = _settings.SidebarVisible ? Visibility.Visible : Visibility.Collapsed;
        SidebarSplitterColumn.Width = new GridLength(_settings.SidebarVisible ? 5 : 0);
        SidebarColumn.Width = new GridLength(_settings.SidebarVisible ? _settings.SidebarWidth : 0);
        UpdateResponsiveLayout();
    }
    private void OnWrap(object sender, RoutedEventArgs e)
    {
        _settings.WordWrap = WrapMenu.IsChecked;
        foreach (var doc in _documents) doc.ApplySettings(_settings.FontSize, _settings.WordWrap, _settings.SourceFonts, _settings.MarkdownFonts);
        _loadingSettingsPage = true; WrapSetting.IsOn = _settings.WordWrap; _loadingSettingsPage = false;
        _ = PersistSettings();
    }
    private void OnTheme(object sender, RoutedEventArgs e)
    {
        _settings.Theme = (string)((MenuFlyoutItem)sender).Tag;
        _loadingSettingsPage = true;
        ThemeSetting.SelectedIndex = _settings.Theme switch { "Light" => 1, "Dark" => 2, _ => 0 };
        _loadingSettingsPage = false;
        ApplyTheme(); _ = PersistSettings();
    }
    private void ApplyTheme()
    {
        Root.RequestedTheme = _settings.Theme switch { "Light" => ElementTheme.Light, "Dark" => ElementTheme.Dark, _ => ElementTheme.Default };
        UpdateNativeTitleBar();
    }
    private void UpdateNativeTitleBar()
    {
        var dark = Root.ActualTheme == ElementTheme.Dark ? 1 : 0;
        _ = DwmSetWindowAttribute(Hwnd, 20 /* DWMWA_USE_IMMERSIVE_DARK_MODE */, ref dark, sizeof(int));
        var contrast = new Windows.UI.ViewManagement.AccessibilitySettings().HighContrast;
        var foreground = contrast
            ? new Windows.UI.ViewManagement.UISettings().GetColorValue(Windows.UI.ViewManagement.UIColorType.Foreground)
            : dark == 1 ? Microsoft.UI.Colors.White : Microsoft.UI.Colors.Black;
        var titleBar = AppWindow.TitleBar;
        titleBar.ButtonBackgroundColor = titleBar.ButtonInactiveBackgroundColor = Microsoft.UI.Colors.Transparent;
        titleBar.ButtonForegroundColor = titleBar.ButtonInactiveForegroundColor = foreground;
        titleBar.ButtonHoverForegroundColor = titleBar.ButtonPressedForegroundColor = foreground;
        titleBar.ButtonHoverBackgroundColor = contrast ? null : dark == 1 ? Windows.UI.Color.FromArgb(255, 50, 50, 50) : Windows.UI.Color.FromArgb(255, 230, 230, 230);
        titleBar.ButtonPressedBackgroundColor = contrast ? null : dark == 1 ? Windows.UI.Color.FromArgb(255, 60, 60, 60) : Windows.UI.Color.FromArgb(255, 210, 210, 210);
    }
    [System.Runtime.InteropServices.DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(nint window, int attribute, ref int value, int size);
    private void Zoom(double delta)
    {
        _settings.FontSize = Math.Clamp(_settings.FontSize + delta, 10, 32);
        foreach (var doc in _documents) doc.ApplySettings(_settings.FontSize, _settings.WordWrap, _settings.SourceFonts, _settings.MarkdownFonts);
        _ = PersistSettings();
    }
    private void OnZoomIn(object sender, RoutedEventArgs e) => Zoom(1);
    private void OnZoomOut(object sender, RoutedEventArgs e) => Zoom(-1);
    private void OnZoomReset(object sender, RoutedEventArgs e) { _settings.FontSize = 15; Zoom(0); }
    private async void OnGoTo(object sender, RoutedEventArgs e)
    {
        var doc = Current; if (doc is null) return;
        var input = new TextBox { PlaceholderText = "行號，例如 42", MinWidth = 280 };
        if (await Ask("移至行", input, "移至") != ContentDialogResult.Primary) return;
        if (!int.TryParse(input.Text, out var line) || line < 1) { ShowNotice("行號無效", "請輸入大於 0 的整數。", InfoBarSeverity.Warning); return; }
        var position = 0;
        for (var i = 1; i < line; i++)
        {
            var next = doc.Buffer.Text.IndexOf('\n', position);
            if (next < 0) { ShowNotice("超過文件行數", "請輸入文件中的行號。", InfoBarSeverity.Warning); return; }
            position = next + 1;
        }
        doc.Select(position, 0);
    }
    private async void OnHelp(object sender, RoutedEventArgs e)
    {
        await Ask("快捷鍵與功能範圍", new ScrollViewer { MaxHeight = 480, Content = new TextBlock
        {
            Text = "Ctrl+N 新增文件　Ctrl+Shift+N 新增 Markdown\nCtrl+O 開啟檔案　Ctrl+Shift+O 開啟資料夾\nCtrl+S 儲存　Ctrl+Shift+S 另存新檔\nCtrl+W 關閉分頁　Ctrl+Tab 切換分頁\nCtrl+, 開啟設定\nCtrl+F 搜尋　Ctrl+H 取代　F3 下一個結果\nCtrl+G 移至行　Ctrl+B 顯示／隱藏側邊欄\nCtrl+E Markdown 預覽／原始碼　Alt+Z 自動換行\nCtrl+Z 復原　Ctrl+Y 重做\nCtrl+Shift+B 粗體　Ctrl+I 斜體（Markdown）\nCtrl++／Ctrl+- 縮放　Ctrl+0 重設\nTab 插入四個空白；Shift+Tab 可離開編輯區。\n\nMarkdown 預覽不更動原始文字。表格與正文一起捲動，儲存格文字會換行；點選預覽區塊可切到原始碼編輯，Ctrl+E 可返回預覽。圖片保留原始語法，不載入外部內容。\n\n支援語法上色，但沒有自動完成或編譯功能。超過 262,144 字元的文件停用高亮；文字檔案上限 16 MiB。\n\n保留 UTF-8、帶 BOM 的 UTF-16／32 與換行格式。不猜測舊式 ANSI 編碼。異常結束的未儲存內容可在下次啟動復原。",
            TextWrapping = TextWrapping.Wrap, MaxWidth = 520
        } }, "", "", "關閉");
    }
    private async void OnAbout(object sender, RoutedEventArgs e)
    { await Ask($"Scriblism {typeof(MainWindow).Assembly.GetName().Version?.ToString(3)}", "編輯文字、程式碼與 Markdown。可同時開啟多個檔案，並在原始碼與預覽間切換。\n\n未儲存內容的備份位置：" + _state.Root, "", "", "關閉"); }

    private SearchResult _search = new([], null);
    private int _searchVersion, _matchIndex = -1;
    private SearchOptions Options => new(CaseOption.IsChecked == true, WordOption.IsChecked == true, RegexOption.IsChecked == true);
    private void OnFind(object sender, RoutedEventArgs e) => ShowFind(false);
    private void OnReplace(object sender, RoutedEventArgs e) => ShowFind(true);
    private void ShowFind(bool replace)
    {
        var doc = Current;
        if (doc is not null && doc.SelectionEnd > doc.SelectionStart)
        {
            var selected = doc.Buffer.Text[doc.SelectionStart..doc.SelectionEnd];
            if (!selected.Contains('\n') && selected.Length < 500) FindBox.Text = selected;
        }
        FindPanel.Visibility = Visibility.Visible;
        ReplaceExpander.IsChecked = replace; SetReplaceExpanded(replace); UpdateOverlayBounds();
        FindBox.Focus(FocusState.Programmatic); FindBox.SelectAll(); ScheduleSearch();
    }
    private void OnToggleReplace(object sender, RoutedEventArgs e) => SetReplaceExpanded(ReplaceExpander.IsChecked == true);
    private void SetReplaceExpanded(bool expanded)
    {
        ReplaceRow.Visibility = expanded ? Visibility.Visible : Visibility.Collapsed;
        ReplaceChevron.Glyph = expanded ? "\uE70D" : "\uE76C";
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(ReplaceExpander, expanded ? "收合取代" : "展開取代");
    }
    private void OnCloseFind(object sender, RoutedEventArgs e) { FindPanel.Visibility = Visibility.Collapsed; Current?.Focus(); }
    private void OnFindChanged(object sender, TextChangedEventArgs e) { if (_ready) ScheduleSearch(); }
    private void OnSearchOption(object sender, RoutedEventArgs e) => ScheduleSearch();
    private async void ScheduleSearch()
    {
        var version = ++_searchVersion; var doc = Current; if (doc is null) return;
        var text = doc.Buffer.Text; var query = FindBox.Text; var options = Options;
        await Task.Delay(150); if (version != _searchVersion) return;
        var result = await Task.Run(() => SearchEngine.Find(text, query, options));
        if (version != _searchVersion || doc != Current || text != doc.Buffer.Text) return;
        _search = result; _matchIndex = -1;
        FindStatus.Text = result.Error ?? (query.Length == 0 ? "輸入文字開始搜尋。" : result.Truncated ? "超過 10,000 個結果；請縮小搜尋範圍。" : $"{result.Matches.Count:N0} 個結果");
    }
    private void FindNext(bool previous)
    {
        var doc = Current; if (doc is null) return;
        // Recompute synchronously at activation to avoid navigating stale debounced results.
        _search = SearchEngine.Find(doc.Buffer.Text, FindBox.Text, Options);
        if (_search.Error is not null || _search.Matches.Count == 0)
        { FindStatus.Text = _search.Error ?? "找不到符合的文字。"; return; }
        var matches = _search.Matches;
        var position = previous ? doc.SelectionStart : doc.SelectionEnd;
        var index = -1;
        if (previous)
        {
            for (var i = matches.Count - 1; i >= 0; i--) if (matches[i].Start < position) { index = i; break; }
            if (index < 0) index = matches.Count - 1;
        }
        else
        {
            for (var i = 0; i < matches.Count; i++)
                if (matches[i].Start >= position && !(matches[i].Length == 0 && matches[i].Start == position && _matchIndex == i)) { index = i; break; }
            if (index < 0) index = 0;
        }
        _matchIndex = index; var match = matches[index]; doc.Select(match.Start, match.Length);
        FindStatus.Text = $"{index + 1:N0} / {matches.Count:N0}" + (matches.Count == 10000 ? "（顯示上限）" : "");
    }
    private void OnFindNext(object sender, RoutedEventArgs e) => FindNext(false);
    private void OnFindPrevious(object sender, RoutedEventArgs e) => FindNext(true);
    private void OnFindKey(object sender, KeyRoutedEventArgs args)
    {
        if (args.Key == VirtualKey.Enter) { args.Handled = true; FindNext(false); }
        if (args.Key == VirtualKey.Escape) { args.Handled = true; OnCloseFind(sender, new()); }
    }
    private void OnReplaceOne(object sender, RoutedEventArgs e)
    {
        var doc = Current; if (doc is null || FindBox.Text.Length == 0) return;
        try
        {
            var results = SearchEngine.Find(doc.Buffer.Text, FindBox.Text, Options);
            if (results.Error is not null) { FindStatus.Text = results.Error; return; }
            var match = results.Matches.FirstOrDefault(m => m.Start == doc.SelectionStart && m.Length == doc.SelectionEnd - doc.SelectionStart);
            if (match is null) { FindNext(false); return; }
            var replacement = SearchEngine.ReplacementFor(doc.Buffer.Text, FindBox.Text, ReplaceBox.Text, Options, match);
            doc.ReplaceSelection(replacement); FindNext(false);
        }
        catch (Exception error) when (error is ArgumentException or IOException or System.Text.RegularExpressions.RegexMatchTimeoutException)
        { FindStatus.Text = "無法取代：" + error.Message; }
    }
    private async void OnReplaceAll(object sender, RoutedEventArgs e)
    {
        var doc = Current; if (doc is null || FindBox.Text.Length == 0) return;
        var original = doc.Buffer.Text; var query = FindBox.Text; var replacement = ReplaceBox.Text; var options = Options;
        try
        {
            var text = await Task.Run(() => SearchEngine.ReplaceAll(original, query, replacement, options));
            if (doc.Buffer.Text != original || !_documents.Contains(doc)) { FindStatus.Text = "文件已變更，請重新執行取代。"; return; }
            if (text.Length > TextFileStore.MaximumBytes) { FindStatus.Text = "取代後的內容超過 16 MiB，未執行。"; return; }
            doc.ReplaceDocument(text, Math.Min(doc.SelectionStart, text.Length)); ScheduleSearch();
        }
        catch (Exception error) when (error is ArgumentException or IOException or System.Text.RegularExpressions.RegexMatchTimeoutException)
        { FindStatus.Text = "無法取代：" + error.Message; }
    }
}
