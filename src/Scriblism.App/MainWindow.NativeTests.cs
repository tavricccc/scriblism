using System.Text.Json;
using Scriblism.Core;

namespace Scriblism.App;

public sealed partial class MainWindow
{
    private async Task RunNativeTests(string[] args)
    {
        var index = Array.IndexOf(args, "--native-test");
        var output = index + 1 < args.Length ? args[index + 1] : Path.Combine(_state.Root, "native-test.json");
        var checks = new List<object>(); var failed = false;
        try
        {
            foreach (var text in new[] { "", "abc", "abc\n", "abc\n\n", "繁體中文\n第二行\n", "a😀b\t終", "\n", "a\u200bb\u200dc" })
            {
                var doc = new EditorSession("roundtrip.md", text); AddDocument(doc);
                await Ready(doc);
                Check("native text roundtrip " + JsonSerializer.Serialize(text), doc.ReadNativeText() == text, doc.ReadNativeText());
                await doc.FormatAsync();
                Check("format preserves bytes " + JsonSerializer.Serialize(text), doc.ReadNativeText() == text, doc.ReadNativeText());
                Remove(doc);
            }
            foreach (var unsupported in new[] { "a\u2028b", "a\u2029b", "a\uFFFCb", "a\0b" })
            {
                var rejected = false;
                try { new EditorSession("unsupported.txt", unsupported).Dispose(); }
                catch (IOException) { rejected = true; }
                Check("unsupported text rejected without mutation", rejected, unsupported);
            }
            var markdown = "# 原生 Markdown\n\n**粗體** 與 *斜體*、~~刪除~~、[連結](https://example.com)\n\n```csharp\npublic class Hello { }\n```\n\n最後一行\n";
            var sample = new EditorSession("native.md", markdown); AddDocument(sample);
            await Ready(sample);
            sample.Select(markdown.Length, 0); await sample.FormatAsync();
            Check("hidden delimiters preserve raw markdown", sample.ReadNativeText() == markdown, sample.ReadNativeText());
            Check("inactive heading delimiter is hidden", sample.Editor.Document.GetRange(0, 1).CharacterFormat.Hidden == Microsoft.UI.Text.FormatEffect.On, "Hidden flag");
            sample.Select(0, 0);
            Check("active heading delimiter is visible", sample.Editor.Document.GetRange(0, 1).CharacterFormat.Hidden == Microsoft.UI.Text.FormatEffect.Off, "Hidden flag");
            sample.Select(markdown.Length, 0);
            Check("formatting does not mark dirty", !sample.Buffer.IsDirty, sample.Buffer.Text);
            var bodyOffset = markdown.IndexOf('與');
            Check("markdown headings use the body text color", sample.Editor.Document.GetRange(2, 3).CharacterFormat.ForegroundColor == sample.Editor.Document.GetRange(bodyOffset, bodyOffset + 1).CharacterFormat.ForegroundColor, "Heading and body foreground match");
            sample.ReplaceSelection("新增中文😀");
            Check("native insertion matches buffer", sample.ReadNativeText() == sample.Buffer.Text, sample.ReadNativeText());
            await sample.FormatAsync(); sample.Undo();
            Check("undo skips presentation", sample.Buffer.Text == markdown && sample.ReadNativeText() == markdown, sample.ReadNativeText());
            sample.Redo(); Check("redo text", sample.ReadNativeText() == markdown + "新增中文😀", sample.ReadNativeText());
            sample.Select(0, 0); sample.ReplaceSelection("prefix\n");
            Check("insert at document start", sample.ReadNativeText() == sample.Buffer.Text, sample.ReadNativeText());
            sample.LiveMarkdown = false; await sample.FormatAsync();
            Check("source toggle preserves text", sample.ReadNativeText() == sample.Buffer.Text, sample.ReadNativeText());
            sample.SelectAll(); sample.ReplaceSelection("");
            Check("delete all is empty", sample.ReadNativeText() == "" && sample.Buffer.Text == "", sample.ReadNativeText());
            Remove(sample);
            var code = new EditorSession("native.cs", "using System;\n// 中文\nvar greeting = \"Hello\";\n"); AddDocument(code);
            await Ready(code); await code.FormatAsync();
            Check("code formatting preserves text", code.ReadNativeText() == code.Buffer.Text, code.ReadNativeText());
            Root.UpdateLayout();
            var editorHeight = code.Editor.ActualHeight; var editorWidth = code.Editor.ActualWidth;
            var editorTop = code.Editor.TransformToVisual(Root).TransformPoint(new Windows.Foundation.Point()).Y;
            var languageTop = LanguageButton.TransformToVisual(Root).TransformPoint(new Windows.Foundation.Point()).Y;
            Check("language switch is below the editor, not in a toolbar", languageTop >= editorTop + editorHeight - 1, $"editor bottom {editorTop + editorHeight}, language top {languageTop}");
            Check("tabs are integrated into the title bar", ExtendsContentIntoTitleBar && Tabs.TransformToVisual(Root).TransformPoint(new Windows.Foundation.Point()).Y == 0 && Tabs.ActualHeight <= 48, $"Tab height {Tabs.ActualHeight}");
            Check("title bar uses wide equal-width tabs and a new-tab dropdown", Tabs.TabWidthMode == Microsoft.UI.Xaml.Controls.TabViewWidthMode.Equal && code.Tab.MinWidth >= 200 && !Tabs.IsAddTabButtonVisible && NewTabButton.Flyout is Microsoft.UI.Xaml.Controls.MenuFlyout { Items.Count: 4 }, $"Tab minimum {code.Tab.MinWidth}, width mode {Tabs.TabWidthMode}");
            Check("document is hosted separately with no second tab strip", ReferenceEquals(DocumentHost.Content, code.View) && code.Tab.Content is null, "Shared workspace hosts selected document");
            Check("caption buttons retain their reserved width", CaptionInset.Width.Value > 0 && Tabs.ActualWidth + CaptionInset.Width.Value <= Root.ActualWidth + 1, $"Inset {CaptionInset.Width.Value}");
            var dragPoint = TitleBarDragArea.TransformToVisual(Root).TransformPoint(new Windows.Foundation.Point(TitleBarDragArea.ActualWidth / 2, TitleBarDragArea.ActualHeight / 2));
            var screenPoint = new NativeTestPoint { X = (int)(dragPoint.X * Root.XamlRoot.RasterizationScale), Y = (int)(dragPoint.Y * Root.XamlRoot.RasterizationScale) };
            NativeTestClientToScreen(Hwnd, ref screenPoint);
            var packedPoint = (nint)((screenPoint.Y << 16) | (screenPoint.X & 0xffff));
            var hit = NativeTestSendMessage(Hwnd, 0x0084 /* WM_NCHITTEST */, 0, packedPoint);
            Check("empty title-bar area is a native caption drag region", hit == 2, $"WM_NCHITTEST={hit}");
            NativeTestSendMessage(Hwnd, 0x00A3 /* WM_NCLBUTTONDBLCLK */, 2, packedPoint);
            await Task.Delay(200);
            Check("native caption double click maximizes", AppWindow.Presenter is Microsoft.UI.Windowing.OverlappedPresenter { State: Microsoft.UI.Windowing.OverlappedPresenterState.Maximized }, AppWindow.Presenter.Kind.ToString());
            if (AppWindow.Presenter is Microsoft.UI.Windowing.OverlappedPresenter restored) restored.Restore();
            await Task.Delay(200); Root.UpdateLayout();
            editorHeight = code.Editor.ActualHeight; editorWidth = code.Editor.ActualWidth;
            ShowFind(false); Root.UpdateLayout();
            Check("floating find does not resize editor", code.Editor.ActualHeight == editorHeight && code.Editor.ActualWidth == editorWidth, $"{editorWidth}x{editorHeight} -> {code.Editor.ActualWidth}x{code.Editor.ActualHeight}");
            ReplaceExpander.IsChecked = true; SetReplaceExpanded(true); Root.UpdateLayout();
            Check("expanding replacement does not resize editor", code.Editor.ActualHeight == editorHeight && code.Editor.ActualWidth == editorWidth, $"{code.Editor.ActualWidth}x{code.Editor.ActualHeight}");
            ShowNotice("測試通知", "右下角浮動訊息，不改變文件版面。", Microsoft.UI.Xaml.Controls.InfoBarSeverity.Success); Root.UpdateLayout();
            Check("toast does not resize editor", code.Editor.ActualHeight == editorHeight && code.Editor.ActualWidth == editorWidth, $"{code.Editor.ActualWidth}x{code.Editor.ActualHeight}");
            _toastTimer.Interval = TimeSpan.FromMilliseconds(80); _toastTimer.Stop(); _toastTimer.Start();
            await Task.Delay(220);
            Check("success toast auto-dismisses without a shadow remnant", !Notice.IsOpen && Notice.Visibility == Microsoft.UI.Xaml.Visibility.Collapsed, Notice.Visibility.ToString());
            ShowNotice("測試警告", "警告必須手動關閉。", Microsoft.UI.Xaml.Controls.InfoBarSeverity.Warning);
            _toastTimer.Interval = TimeSpan.FromMilliseconds(80); await Task.Delay(220);
            Check("warning toast remains until dismissed", Notice.IsOpen, Notice.IsOpen.ToString());
            OnCloseFind(this, new Microsoft.UI.Xaml.RoutedEventArgs()); Notice.IsOpen = false;
            code.Select(code.Buffer.Text.Length, 0);
            code.Editor.Document.Selection.TypeText("// native 輸入😀");
            code.CaptureText();
            Check("native typing reaches buffer", code.Buffer.Text.EndsWith("// native 輸入😀"), code.Buffer.Text);
            await code.FormatAsync(); code.Undo();
            Check("native typing undo", !code.Buffer.Text.EndsWith("// native 輸入😀"), code.Buffer.Text);
            code.Redo(); Check("native typing redo", code.ReadNativeText() == code.Buffer.Text, code.ReadNativeText());
            Remove(code);
            var temp = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(output))!, "native-files");
            Directory.CreateDirectory(temp);
            var filePath = Path.Combine(temp, "保存測試.md");
            await File.WriteAllTextAsync(filePath, "# heading\n");
            await OpenPath(filePath);
            Check("opening a file shows its parent folder in the sidebar", string.Equals(_folder, temp, StringComparison.OrdinalIgnoreCase) && FileList.Items.Count > 0, _folder ?? "");
            var savedDoc = Current!; savedDoc.Select(savedDoc.Buffer.Text.Length, 0); savedDoc.ReplaceSelection("中文😀\n");
            Check("save integration", await Save(savedDoc), filePath);
            Check("disk text equals editor text", (await TextFileStore.ReadAsync(filePath)).Text == savedDoc.Buffer.Text, savedDoc.Buffer.Text);
            Check("save clears dirty state", !savedDoc.Buffer.IsDirty, savedDoc.Name);
            var otherFolder = Path.Combine(temp, "other");
            Directory.CreateDirectory(otherFolder);
            var otherPath = Path.Combine(otherFolder, "other.txt");
            await File.WriteAllTextAsync(otherPath, "other");
            await OpenPath(otherPath);
            Check("opening another file switches the sidebar folder", string.Equals(_folder, otherFolder, StringComparison.OrdinalIgnoreCase), _folder ?? "");
            var otherDoc = Current!;
            Tabs.SelectedItem = savedDoc.Tab;
            for (var attempt = 0; attempt < 20 && !string.Equals(_folder, temp, StringComparison.OrdinalIgnoreCase); attempt++) await Task.Delay(50);
            Check("switching tabs follows the selected file folder", string.Equals(_folder, temp, StringComparison.OrdinalIgnoreCase), _folder ?? "");
            Remove(otherDoc);
            Remove(savedDoc);
        }
        catch (Exception error) { failed = true; checks.Add(new { name = "exception", passed = false, detail = error.ToString() }); }
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);
        await File.WriteAllTextAsync(output, JsonSerializer.Serialize(new { passed = !failed, checks }, new JsonSerializerOptions { WriteIndented = true }));
        _recoveryTimer.Stop(); await _state.SaveRecoveryAsync([]);
        _canClose = true; Close();
        void Check(string name, bool passed, string detail) { failed |= !passed; checks.Add(new { name, passed, detail }); }
        void Remove(EditorSession doc) { Tabs.TabItems.Remove(doc.Tab); _documents.Remove(doc); doc.Dispose(); }
        static async Task Ready(EditorSession doc)
        {
            if (doc.Editor.IsLoaded) return;
            var source = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            Microsoft.UI.Xaml.RoutedEventHandler? handler = null;
            handler = (_, _) => { doc.Editor.Loaded -= handler; source.TrySetResult(); };
            doc.Editor.Loaded += handler;
            await source.Task.WaitAsync(TimeSpan.FromSeconds(10));
        }
    }
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct NativeTestPoint { public int X; public int Y; }
    [System.Runtime.InteropServices.DllImport("user32.dll", EntryPoint = "ClientToScreen")]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    private static extern bool NativeTestClientToScreen(nint window, ref NativeTestPoint point);
    [System.Runtime.InteropServices.DllImport("user32.dll", EntryPoint = "SendMessageW")]
    private static extern nint NativeTestSendMessage(nint window, uint message, nint wParam, nint lParam);
}
