using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Scriblism.Core;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.System;

namespace Scriblism.App;

public sealed partial class MainWindow : Window
{
    private readonly List<EditorSession> _documents = [];
    private readonly LocalStateStore _state = new(Environment.GetEnvironmentVariable("SCRIBLISM_STATE_ROOT"));
    private readonly EditorSettings _settings;
    private readonly SemaphoreSlim _dialogGate = new(1, 1);
    private readonly Microsoft.UI.Dispatching.DispatcherQueueTimer _recoveryTimer;
    private readonly Microsoft.UI.Dispatching.DispatcherQueueTimer _toastTimer;
    private bool _toastHovered, _toastFocused;
    private bool _ready, _canClose, _closing, _fileOperation;
    private int _untitled;
    private string? _folder;
    private new EditorSession? Current => (Tabs.SelectedItem as TabViewItem)?.Tag as EditorSession;
    private nint Hwnd => WinRT.Interop.WindowNative.GetWindowHandle(this);

    public MainWindow(string[] paths)
    {
        _settings = _state.LoadSettings();
        InitializeComponent();
        FindPanel.Translation = new System.Numerics.Vector3(0, 0, 24);
        Notice.Translation = new System.Numerics.Vector3(0, 0, 24);
        SystemBackdrop = new MicaBackdrop();
        ExtendsContentIntoTitleBar = true;
        AppWindow.TitleBar.PreferredHeightOption = TitleBarHeightOption.Tall;
        SetTitleBar(TitleBarDragArea);
        AppWindow.Changed += (_, _) => UpdateCaptionInset();
        Root.Loaded += (_, _) =>
        {
            UpdateCaptionInset();
            Root.XamlRoot.Changed += (_, _) => UpdateCaptionInset();
        };
        AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "Scriblism.ico"));
        AppWindow.Resize(new Windows.Graphics.SizeInt32(1220, 840));
        if (AppWindow.Presenter is OverlappedPresenter presenter) { presenter.PreferredMinimumWidth = 740; presenter.PreferredMinimumHeight = 440; }
        ApplyTheme();
        foreach (var language in Languages.All)
        {
            var item = new ToggleMenuFlyoutItem { Text = language.Name, Tag = language };
            item.Click += OnLanguageChanged; LanguageMenu.Items.Add(item);
        }
        SidebarMenu.IsChecked = _settings.SidebarVisible;
        WrapMenu.IsChecked = _settings.WordWrap;
        HiddenMenu.IsChecked = _settings.ShowHidden;
        SetSidebar();
        _recoveryTimer = DispatcherQueue.CreateTimer();
        _recoveryTimer.Interval = TimeSpan.FromSeconds(2); _recoveryTimer.IsRepeating = false;
        _recoveryTimer.Tick += async (_, _) => await WriteRecovery();
        _toastTimer = DispatcherQueue.CreateTimer(); _toastTimer.IsRepeating = false;
        _toastTimer.Tick += (_, _) => Notice.IsOpen = false;
        Notice.Closed += (_, _) => { _toastTimer.Stop(); Notice.Visibility = Visibility.Collapsed; _toastHovered = false; _toastFocused = false; };
        Notice.PointerEntered += (_, _) => { _toastHovered = true; _toastTimer.Stop(); };
        Notice.PointerExited += (_, _) => { _toastHovered = false; RestartToastTimer(); };
        Notice.GotFocus += (_, _) => { _toastFocused = true; _toastTimer.Stop(); };
        Notice.LostFocus += (_, _) => { _toastFocused = false; RestartToastTimer(); };
        InstallShortcuts();
        AppWindow.Closing += OnWindowClosing;
        Closed += (_, _) => { _recoveryTimer.Stop(); _toastTimer.Stop(); foreach (var doc in _documents) doc.Dispose(); };
        Root.ActualThemeChanged += (_, _) => { UpdateNativeTitleBar(); foreach (var doc in _documents) doc.ScheduleFormat(); };
        Root.Loaded += async (_, _) =>
        {
            if (_ready) return; _ready = true;
            if (paths.Contains("--native-test")) { await RunNativeTests(paths); return; }
            await RestoreRecovery();
            foreach (var path in paths.Where(p => !p.StartsWith("--", StringComparison.Ordinal)))
            {
                if (Directory.Exists(path)) await OpenFolder(path); else await OpenPath(path);
            }
            if (_documents.Count == 0) AddDocument(new EditorSession("未命名 " + ++_untitled));
            RefreshRecent(); Current?.Focus();
        };
        Activated += async (_, args) =>
        {
            if (!_ready || _closing || args.WindowActivationState == WindowActivationState.Deactivated || Current?.Path is not { } path) return;
            try
            {
                var doc = Current;
                if (doc is null || !File.Exists(path)) return;
                // Saving always rechecks the content hash. Activation is informational, never an automatic reload.
                var modified = await Task.Run(() => new FileInfo(path).LastWriteTimeUtc);
                if (_knownWriteTimes.TryGetValue(path, out var known) && modified != known)
                    ShowNotice("磁碟上的檔案有變更", "目前編輯內容保留不變；儲存時會先確認衝突。", InfoBarSeverity.Warning);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { App.Log(e); }
        };
    }
    private readonly Dictionary<string, DateTime> _knownWriteTimes = new(StringComparer.OrdinalIgnoreCase);

    private void AddDocument(EditorSession document)
    {
        if (_canClose || _closing) { document.Dispose(); return; }
        if (_documents.Count >= 32)
        { document.Dispose(); ShowNotice("分頁數量已達上限", "請先關閉部分分頁（最多 32 個）。", InfoBarSeverity.Warning); return; }
        document.Error = message => ShowNotice("編輯器", message, InfoBarSeverity.Warning);
        document.Changed = OnDocumentChanged;
        document.SelectionMoved = doc => { if (doc == Current) { UpdateStatus(); RefreshOutline(); } };
        document.ApplySettings(_settings.FontSize, _settings.WordWrap);
        _documents.Add(document); Tabs.TabItems.Add(document.Tab); Tabs.SelectedItem = document.Tab;
        UpdateDocumentUi(); document.Focus();
    }
    private void OnDocumentChanged(EditorSession doc)
    {
        _recoveryTimer.Stop(); _recoveryTimer.Start();
        if (doc == Current) { UpdateStatus(); if (FindPanel.Visibility == Visibility.Visible) ScheduleSearch(); }
    }
    private void UpdateDocumentUi()
    {
        if (LanguageButton is null) return;
        var doc = Current;
        if (!ReferenceEquals(DocumentHost.Content, doc?.View)) DocumentHost.Content = doc?.View;
        LanguageButton.Content = doc?.Language.Name ?? "純文字";
        foreach (var item in LanguageMenu.Items.OfType<ToggleMenuFlyoutItem>()) item.IsChecked = ReferenceEquals(item.Tag, doc?.Language);
        MarkdownMode.Visibility = doc?.IsMarkdown == true ? Visibility.Visible : Visibility.Collapsed;
        MarkdownMode.Content = doc?.LiveMarkdown != false ? "即時排版" : "原始碼";
        MarkdownSourceMenu.IsEnabled = MarkdownFormatMenu.IsEnabled = doc?.IsMarkdown == true;
        MarkdownSourceMenu.IsChecked = doc?.LiveMarkdown == false;
        UpdateResponsiveLayout(); UpdateStatus(); RefreshOutline();
        if (FindPanel.Visibility == Visibility.Visible) ScheduleSearch();
    }
    private void UpdateStatus()
    {
        var doc = Current; if (doc is null) return;
        var position = doc.SelectionEnd; var text = doc.Buffer.Text;
        var line = 1; var last = -1;
        for (var i = 0; i < position; i++) if (text[i] == '\n') { line++; last = i; }
        var selected = doc.SelectionEnd - doc.SelectionStart;
        PositionStatus.Text = $"行 {line:N0}，欄 {position - last:N0}" + (selected > 0 ? $" · 已選 {selected:N0}" : "");
        PathStatus.Text = (doc.Path ?? doc.Name) + (text.Length > SyntaxHighlighter.MaximumHighlightLength ? " · 大型文件：停用高亮" : "");
        ToolTipService.SetToolTip(PathStatus, doc.Path ?? doc.Name);
        EncodingStatus.Text = doc.EncodingName.ToUpperInvariant() + (doc.Bom ? " BOM" : "") + " · " + (doc.NewLine == "\r\n" ? "CRLF" : doc.NewLine == "\r" ? "CR" : "LF");
        Title = $"{(doc.Buffer.IsDirty ? "• " : "")}{doc.Name} — Scriblism";
    }
    private void OnNew(object sender, RoutedEventArgs e) => AddDocument(new EditorSession("未命名 " + ++_untitled));
    private void OnNewMarkdown(object sender, RoutedEventArgs e) => AddDocument(new EditorSession("未命名 " + ++_untitled + ".md"));
    private void OnNewTabButtonClick(SplitButton sender, SplitButtonClickEventArgs args) => OnNew(sender, new RoutedEventArgs());
    private void UpdateCaptionInset()
    {
        var scale = Root.XamlRoot?.RasterizationScale ?? 1;
        CaptionInset.Width = new GridLength(AppWindow.TitleBar.RightInset / scale);
    }
    private async void OnTabChanged(object sender, SelectionChangedEventArgs e)
    {
        UpdateDocumentUi();
        await ShowFolderForDocument(Current);
    }
    private void OnLanguageChanged(object sender, RoutedEventArgs e)
    {
        if (Current is not { } doc || ((FrameworkElement)sender).Tag is not Language language) return;
        doc.Language = language; doc.ScheduleFormat(); UpdateDocumentUi(); doc.Focus();
    }
    private void OnMarkdownMode(object sender, RoutedEventArgs e)
    {
        if (Current is not { } doc) return;
        doc.LiveMarkdown = !doc.LiveMarkdown; doc.ScheduleFormat(); UpdateDocumentUi(); doc.Focus();
    }
    private void OnFormat(object sender, RoutedEventArgs e) => Current?.InsertMarkdown((string)((FrameworkElement)sender).Tag);
    private void OnRootSizeChanged(object sender, SizeChangedEventArgs e) => UpdateResponsiveLayout();
    private void OnEditorWorkspaceSizeChanged(object sender, SizeChangedEventArgs e) => UpdateOverlayBounds();
    private void UpdateOverlayBounds()
    {
        if (EditorWorkspace is null || FindPanel is null || Notice is null) return;
        FindPanel.MaxWidth = Math.Max(0, EditorWorkspace.ActualWidth - 24);
        Notice.MaxWidth = Math.Max(0, Root.ActualWidth - 32);
    }
    private void UpdateResponsiveLayout()
    {
        if (SidebarColumn is null) return;
        if (_settings.SidebarVisible) SidebarColumn.Width = new GridLength(Root.ActualWidth < 780 ? 196 : 248);
        UpdateOverlayBounds();
    }

    private async void OnOpen(object sender, RoutedEventArgs e)
    {
        if (_fileOperation) return; _fileOperation = true;
        try
        {
            var picker = new FileOpenPicker(); picker.FileTypeFilter.Add("*");
            WinRT.Interop.InitializeWithWindow.Initialize(picker, Hwnd);
            var files = await picker.PickMultipleFilesAsync();
            foreach (var file in files) await OpenPath(file.Path);
        }
        catch (Exception error) { ShowError("無法開啟檔案", error); }
        finally { _fileOperation = false; }
    }
    private async Task OpenPath(string path)
    {
        if (_closing || _canClose) return;
        try
        {
            path = Path.GetFullPath(path);
            var existing = _documents.FirstOrDefault(d => string.Equals(d.Path, path, StringComparison.OrdinalIgnoreCase));
            if (existing is not null) { Tabs.SelectedItem = existing.Tab; existing.Focus(); await ShowFolderForDocument(existing); return; }
            if (_documents.Count >= 32) { ShowNotice("分頁數量已達上限", "請先關閉部分分頁（最多 32 個），再開啟檔案。", InfoBarSeverity.Warning); return; }
            var file = await TextFileStore.ReadAsync(path);
            if (_closing || _canClose) return;
            // A second open may finish while a file picker or a tree invocation is awaiting I/O.
            existing = _documents.FirstOrDefault(d => string.Equals(d.Path, path, StringComparison.OrdinalIgnoreCase));
            if (existing is not null) { Tabs.SelectedItem = existing.Tab; await ShowFolderForDocument(existing); return; }
            var doc = new EditorSession(Path.GetFileName(path), file.Text, path)
            { EncodingName = file.EncodingName, Bom = file.Bom, NewLine = file.NewLine, ExpectedHash = file.Hash, MixedNewLines = file.MixedNewLines };
            AddDocument(doc); RememberFile(path);
            await ShowFolderForDocument(doc);
            _knownWriteTimes[path] = File.GetLastWriteTimeUtc(path);
            if (file.MixedNewLines) ShowNotice("換行格式不一致", "編輯後儲存會統一成狀態列所示的換行格式；未修改時儲存不會重寫檔案。", InfoBarSeverity.Warning);
        }
        catch (Exception error) { ShowError("無法開啟檔案", error); }
    }
    private async void OnSave(object sender, RoutedEventArgs e) { if (Current is { } doc) await Save(doc); }
    private async void OnSaveAs(object sender, RoutedEventArgs e) { if (Current is { } doc) await Save(doc, true); }
    private async void OnSaveAll(object sender, RoutedEventArgs e)
    {
        foreach (var doc in _documents.ToArray()) if (doc.Buffer.IsDirty && !await Save(doc)) break;
    }
    private readonly HashSet<EditorSession> _saving = [];
    private async Task<bool> Save(EditorSession doc, bool saveAs = false)
    {
        if (doc.IsComposing || !_saving.Add(doc)) return false;
        try
        {
            doc.CaptureText();
            if (!saveAs && doc.Path is not null && !doc.Buffer.IsDirty) return true;
            var path = doc.Path;
            if (saveAs || path is null)
            {
                var picker = new FileSavePicker { SuggestedFileName = doc.Name };
                picker.FileTypeChoices.Add(doc.IsMarkdown ? "Markdown" : "文字檔案", new List<string> { doc.IsMarkdown ? ".md" : Path.GetExtension(doc.Name) is { Length: > 0 } extension ? extension : ".txt" });
                picker.FileTypeChoices.Add("所有檔案", new List<string> { ".*" });
                WinRT.Interop.InitializeWithWindow.Initialize(picker, Hwnd);
                var result = await picker.PickSaveFileAsync(); if (result is null) return false;
                path = result.Path;
                if (_documents.Any(other => other != doc && string.Equals(other.Path, path, StringComparison.OrdinalIgnoreCase)))
                { ShowNotice("這個檔案已在另一個分頁開啟", "請切換到該分頁，或另選檔案名稱。", InfoBarSeverity.Warning); return false; }
            }
            var snapshot = doc.Buffer.Text;
            var samePath = string.Equals(path, doc.Path, StringComparison.OrdinalIgnoreCase);
            TextFile saved;
            try { saved = await TextFileStore.SaveAsync(path, snapshot, doc.EncodingName, doc.Bom, doc.NewLine, samePath ? doc.ExpectedHash : null); }
            catch (FileConflictException)
            {
                var result = await Ask("確認覆寫檔案", "磁碟上的檔案已存在、被修改或刪除。覆寫會以目前分頁取代磁碟內容；取消可保留雙方內容，再另存新檔。", "覆寫", "", "取消");
                if (result != ContentDialogResult.Primary) return false;
                saved = await TextFileStore.SaveAsync(path, snapshot, doc.EncodingName, doc.Bom, doc.NewLine, null, true);
            }
            doc.Path = saved.Path; doc.Name = Path.GetFileName(saved.Path); doc.ExpectedHash = saved.Hash;
            doc.Buffer.MarkSaved(snapshot); doc.MixedNewLines = false;
            if (doc.Language.Id == "text") doc.Language = Languages.Detect(saved.Path);
            _knownWriteTimes[path] = File.GetLastWriteTimeUtc(path);
            doc.RefreshHeader(); doc.ScheduleFormat(); UpdateDocumentUi(); RememberFile(path);
            await ShowFolderForDocument(doc);
            await WriteRecovery();
            ShowNotice("已儲存", path, InfoBarSeverity.Success);
            return true;
        }
        catch (Exception error) { ShowError("儲存失敗，編輯內容仍保留", error); return false; }
        finally { _saving.Remove(doc); }
    }

    private async Task<bool> CanCloseDocument(EditorSession doc)
    {
        if (_saving.Contains(doc) || doc.IsComposing) return false;
        doc.CaptureText(); if (!doc.Buffer.IsDirty) return true;
        Tabs.SelectedItem = doc.Tab;
        var answer = await Ask("儲存變更？", $"「{doc.Name}」有尚未儲存的內容。", "儲存", "不儲存", "取消");
        return answer == ContentDialogResult.Secondary || answer == ContentDialogResult.Primary && await Save(doc);
    }
    private async Task CloseDocument(EditorSession doc)
    {
        if (_closing || !await CanCloseDocument(doc) || !_documents.Contains(doc)) return;
        Tabs.TabItems.Remove(doc.Tab); _documents.Remove(doc); doc.Dispose();
        if (_documents.Count == 0) AddDocument(new EditorSession("未命名 " + ++_untitled));
        await WriteRecovery(); UpdateDocumentUi();
    }
    private async void OnTabCloseRequested(TabView sender, TabViewTabCloseRequestedEventArgs args)
    { if (args.Tab.Tag is EditorSession doc) await CloseDocument(doc); }
    private async void OnCloseTab(object sender, RoutedEventArgs e) { if (Current is { } doc) await CloseDocument(doc); }
    private void OnExit(object sender, RoutedEventArgs e) => Close();
    private async void OnWindowClosing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        if (_canClose) return; args.Cancel = true;
        if (_closing || _fileOperation || _saving.Count > 0) return;
        _closing = true;
        try
        {
            foreach (var doc in _documents.ToArray()) if (!await CanCloseDocument(doc)) return;
            _recoveryTimer.Stop(); await _state.SaveRecoveryAsync([]); await _state.SaveSettingsAsync(_settings);
            _canClose = true; Close();
        }
        catch (Exception error) { ShowError("無法完成關閉作業", error); }
        finally { _closing = false; }
    }
    private async Task<ContentDialogResult> Ask(string title, object content, string primary, string secondary = "", string close = "取消")
    {
        await _dialogGate.WaitAsync();
        try
        {
            return await new ContentDialog { XamlRoot = Root.XamlRoot, RequestedTheme = Root.ActualTheme,
                Title = title, Content = content, PrimaryButtonText = primary, SecondaryButtonText = secondary,
                CloseButtonText = close, DefaultButton = ContentDialogButton.Close }.ShowAsync();
        }
        finally { _dialogGate.Release(); }
    }
    private void ShowNotice(string title, string message, InfoBarSeverity severity = InfoBarSeverity.Informational)
    {
        _toastTimer.Stop();
        Notice.Title = title; Notice.Message = message; Notice.Severity = severity;
        Notice.Visibility = Visibility.Visible; Notice.IsOpen = true;
        _toastTimer.Interval = TimeSpan.FromSeconds(severity == InfoBarSeverity.Success ? 4 : 7);
        RestartToastTimer();
    }
    private void RestartToastTimer()
    {
        if (Notice.IsOpen && !_toastHovered && !_toastFocused && Notice.Severity is InfoBarSeverity.Success or InfoBarSeverity.Informational)
        { _toastTimer.Stop(); _toastTimer.Start(); }
    }
    private void ShowError(string title, Exception error) { App.Log(error); ShowNotice(title, error.Message, InfoBarSeverity.Error); }

    private async Task WriteRecovery()
    {
        try { await _state.SaveRecoveryAsync(_documents.Where(d => d.Buffer.IsDirty).Select(d => d.Recovery()).ToArray()); }
        catch (Exception error) { ShowError("無法建立復原備份，請先儲存檔案", error); }
    }
    private async Task RestoreRecovery()
    {
        var sessions = await Task.Run(_state.FindRecoverable);
        if (sessions.Count == 0) return;
        var answer = await Ask("找到未儲存的文件", "上次異常結束前的本機備份可以復原。暫不復原會保留備份，不會修改原檔。", "復原", "", "暫不復原");
        if (answer != ContentDialogResult.Primary) return;
        foreach (var (file, session) in sessions)
        {
            var fullyRestored = true;
            foreach (var recovered in session.Documents)
            {
                try
                {
                    if (_documents.Count >= 32) throw new IOException("已復原 32 個分頁；其餘備份保留在本機 Recovery 資料夾。");
                    // Recovered tabs begin as unsaved copies; even an empty recovered text remains dirty.
                    var doc = new EditorSession(recovered.Name, recovered.Text.Length == 0 ? "\n" : "", recovered.Path)
                    { Language = Languages.ById(recovered.LanguageId), EncodingName = recovered.EncodingName, Bom = recovered.Bom, NewLine = recovered.NewLine, ExpectedHash = recovered.ExpectedHash };
                    doc.ReplaceDocument(recovered.Text, 0); AddDocument(doc);
                }
                catch (Exception error) { fullyRestored = false; ShowError("部分文件無法復原，備份仍保留", error); }
            }
            // Only remove the old recovery after a durable replacement exists.
            try
            {
                await _state.SaveRecoveryAsync(_documents.Where(d => d.Buffer.IsDirty).Select(d => d.Recovery()).ToArray());
                if (fullyRestored) _state.AcknowledgeRecovery(file);
            }
            catch (Exception error) { ShowError("舊復原備份仍保留", error); }
        }
    }
    private void RememberFile(string path)
    {
        _settings.RecentFiles.RemoveAll(p => string.Equals(p, path, StringComparison.OrdinalIgnoreCase));
        _settings.RecentFiles.Insert(0, path); _settings.RecentFiles = _settings.RecentFiles.Take(15).ToList(); RefreshRecent();
        _ = PersistSettings();
    }
    private void RefreshRecent()
    {
        RecentMenu.Items.Clear();
        foreach (var path in _settings.RecentFiles)
        {
            var item = new MenuFlyoutItem { Text = path };
            item.Click += async (_, _) => await OpenPath(path); RecentMenu.Items.Add(item);
        }
        if (RecentMenu.Items.Count == 0) RecentMenu.Items.Add(new MenuFlyoutItem { Text = "尚無最近開啟的檔案", IsEnabled = false });
    }
    private async Task PersistSettings()
    { try { await _state.SaveSettingsAsync(_settings); } catch (Exception e) { App.Log(e); } }
    private void OnDragOver(object sender, DragEventArgs args)
    { args.AcceptedOperation = args.DataView.Contains(StandardDataFormats.StorageItems) ? DataPackageOperation.Copy : DataPackageOperation.None; }
    private async void OnDrop(object sender, DragEventArgs args)
    {
        if (!args.DataView.Contains(StandardDataFormats.StorageItems)) return;
        args.Handled = true;
        try { foreach (var item in await args.DataView.GetStorageItemsAsync()) { if (item is StorageFolder) await OpenFolder(item.Path); else await OpenPath(item.Path); } }
        catch (Exception error) { ShowError("無法開啟拖入的項目", error); }
    }
}
