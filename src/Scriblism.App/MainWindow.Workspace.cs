using System.Diagnostics;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Scriblism.Core;
using Windows.Storage.Pickers;

namespace Scriblism.App;

public sealed partial class MainWindow
{
    private int _folderRevision;
    private readonly HashSet<FileRow> _loadingRows = [];
    private sealed class FileRow(FileEntry entry, int depth)
    {
        public FileEntry Entry { get; } = entry;
        public int Depth { get; } = depth;
        public bool Expanded { get; set; }
        public TextBlock? Chevron { get; set; }
        public ListViewItem? Item { get; set; }
    }
    private void OnSidebarSection(object sender, SelectionChangedEventArgs e)
    {
        if (SidebarSections.SelectedIndex == 1) RefreshOutline();
    }
    private async void OnOpenFolder(object sender, RoutedEventArgs e)
    {
        if (_fileOperation) return; _fileOperation = true;
        try
        {
            var picker = new FolderPicker(); picker.FileTypeFilter.Add("*");
            WinRT.Interop.InitializeWithWindow.Initialize(picker, Hwnd);
            var folder = await picker.PickSingleFolderAsync(); if (folder is not null) await OpenFolder(folder.Path);
        }
        catch (Exception error) { ShowError("無法開啟資料夾", error); }
        finally { _fileOperation = false; }
    }
    private async Task OpenFolder(string path)
    {
        var revision = ++_folderRevision;
        try
        {
            path = Path.GetFullPath(path);
            var entries = await Task.Run(() => Workspace.List(path, _settings.ShowHidden));
            if (revision != _folderRevision) return;
            FileList.Items.Clear();
            _folder = path;
            foreach (var entry in entries.Entries) FileList.Items.Add(MakeItem(entry, 0));
            FolderEmpty.Visibility = Visibility.Collapsed;
            if (entries.Truncated) ShowNotice("資料夾項目過多", "僅顯示前 5,000 個項目；仍可用「開啟檔案」開啟其他文件。", InfoBarSeverity.Warning);
            await PersistSettings();
        }
        catch (Exception error) { ShowError("無法讀取資料夾", error); }
    }
    private async Task ShowFolderForDocument(EditorSession? document)
    {
        if (document?.Path is not { } path) return;
        var folder = Path.GetDirectoryName(path);
        if (folder is not null && !string.Equals(_folder, folder, StringComparison.OrdinalIgnoreCase))
            await OpenFolder(folder);
    }
    private ListViewItem MakeItem(FileEntry entry, int depth)
    {
        var fileRow = new FileRow(entry, depth);
        var label = new TextBlock { Text = entry.Name, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Margin = new Thickness(8 + depth * 14, 0, 4, 0) };
        var chevron = new TextBlock { Text = entry.IsDirectory && !entry.IsLink ? "›" : "", Width = 10, FontSize = 16, VerticalAlignment = VerticalAlignment.Center, TextAlignment = TextAlignment.Center };
        fileRow.Chevron = chevron;
        row.Children.Add(chevron);
        row.Children.Add(new FontIcon { Glyph = entry.IsDirectory ? "\uE8B7" : "\uE8A5", FontSize = 14, Opacity = .8 });
        row.Children.Add(label); ToolTipService.SetToolTip(row, entry.Path);
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(row, entry.Name + (entry.IsLink ? "（連結）" : ""));
        var menu = new MenuFlyout();
        if (entry.IsDirectory && !entry.IsLink)
        {
            Add("新增檔案…", async () => await CreateEntry(entry.Path, false));
            Add("新增資料夾…", async () => await CreateEntry(entry.Path, true));
        }
        if (!entry.IsDirectory) Add("開啟", async () => await OpenPath(entry.Path));
        Add("在檔案總管顯示", () => Reveal(entry.Path));
        Add("複製路徑", () =>
        {
            try { var package = new Windows.ApplicationModel.DataTransfer.DataPackage(); package.SetText(entry.Path); Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(package); }
            catch (Exception error) { ShowError("無法複製路徑", error); }
        });
        var item = new ListViewItem { Content = row, Tag = fileRow, Height = 34, MinHeight = 34, Padding = new Thickness(0), Margin = new Thickness(0) };
        item.ContextFlyout = menu;
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(item, entry.Name + (entry.IsLink ? "（連結）" : ""));
        item.Tapped += async (_, args) => { args.Handled = true; await ActivateFileRow(fileRow); };
        item.KeyDown += async (_, args) =>
        {
            if (args.Key is Windows.System.VirtualKey.Enter or Windows.System.VirtualKey.Space)
            { args.Handled = true; await ActivateFileRow(fileRow); }
        };
        fileRow.Item = item;
        return item;
        void Add(string title, Action action) { var menuItem = new MenuFlyoutItem { Text = title }; menuItem.Click += (_, _) => action(); menu.Items.Add(menuItem); }
    }
    private async Task ActivateFileRow(FileRow row)
    {
        var entry = row.Entry;
        if (!entry.IsDirectory) { await OpenPath(entry.Path); return; }
        if (entry.IsLink) { ShowNotice("資料夾連結", "為避免循環走訪，檔案樹不展開連結。可用右鍵在檔案總管開啟，或直接開啟目標資料夾。"); return; }
        if (row.Expanded)
        {
            var index = FileList.Items.IndexOf(row.Item);
            while (index + 1 < FileList.Items.Count && FileList.Items[index + 1] is ListViewItem { Tag: FileRow child } && child.Depth > row.Depth)
                FileList.Items.RemoveAt(index + 1);
            row.Expanded = false; if (row.Chevron is not null) row.Chevron.Text = "›";
            return;
        }
        if (!_loadingRows.Add(row)) return;
        var revision = _folderRevision;
        try
        {
            var listing = await Task.Run(() => Workspace.List(entry.Path, _settings.ShowHidden));
            if (revision != _folderRevision || row.Item is null) return;
            var index = FileList.Items.IndexOf(row.Item);
            if (index < 0) return;
            foreach (var child in listing.Entries) FileList.Items.Insert(++index, MakeItem(child, row.Depth + 1));
            row.Expanded = true; if (row.Chevron is not null) row.Chevron.Text = "⌄";
            if (listing.Truncated) ShowNotice("部分項目未顯示", entry.Path + " 超過 5,000 個項目。", InfoBarSeverity.Warning);
        }
        catch (Exception error) { ShowError("無法展開資料夾", error); }
        finally { _loadingRows.Remove(row); }
    }
    private async void OnRefresh(object sender, RoutedEventArgs e) { if (_folder is not null) await OpenFolder(_folder); }
    private async void OnHidden(object sender, RoutedEventArgs e)
    { _settings.ShowHidden = HiddenMenu.IsChecked; if (_folder is not null) await OpenFolder(_folder); }
    private async void OnCreateFile(object sender, RoutedEventArgs e)
    {
        if (_folder is null) { OnNew(sender, e); return; }
        var selected = (FileList.SelectedItem as ListViewItem)?.Tag is FileRow row ? row.Entry : null;
        await CreateEntry(selected?.IsDirectory == true && !selected.IsLink ? selected.Path : _folder, false);
    }
    private async Task CreateEntry(string directory, bool folder)
    {
        var input = new TextBox { PlaceholderText = folder ? "資料夾名稱" : "例如 notes.md", MinWidth = 320 };
        var answer = await Ask(folder ? "新增資料夾" : "新增檔案", input, "建立");
        if (answer != ContentDialogResult.Primary) return;
        try
        {
            var path = Workspace.ChildPath(directory, input.Text);
            if (File.Exists(path) || Directory.Exists(path)) throw new IOException("同名項目已存在。");
            if (folder) Directory.CreateDirectory(path);
            else
            {
                await using (var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None)) { await stream.FlushAsync(); }
                await OpenPath(path);
            }
            if (_folder is not null) await OpenFolder(_folder);
        }
        catch (Exception error) { ShowError("無法建立項目", error); }
    }
    private void Reveal(string path)
    {
        try
        {
            var info = new ProcessStartInfo("explorer.exe") { UseShellExecute = false };
            info.ArgumentList.Add("/select," + path); Process.Start(info);
        }
        catch (Exception error) { ShowError("無法開啟檔案總管", error); }
    }
    private IReadOnlyList<OutlineEntry>? _shownOutline;
    private void RefreshOutline()
    {
        if (OutlineList is null) return;
        var outline = Current?.Outline;
        if (ReferenceEquals(outline, _shownOutline)) return; _shownOutline = outline;
        OutlineList.Items.Clear();
        if (outline is not null)
            foreach (var item in outline)
            {
                var row = new ListViewItem { Content = item.Title, Tag = item, MinHeight = 34, Padding = new Thickness(12 + (item.Level - 1) * 12, 5, 8, 5) };
                OutlineList.Items.Add(row);
            }
        OutlineEmpty.Visibility = outline?.Count > 0 ? Visibility.Collapsed : Visibility.Visible;
    }
    private void OnOutlineClick(object sender, ItemClickEventArgs args)
    { if (args.ClickedItem is ListViewItem { Tag: OutlineEntry entry }) Current?.Select(entry.Offset, 0); }
}
