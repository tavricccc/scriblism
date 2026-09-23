using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace Scriblism.Setup;

public sealed partial class MainWindow : Window
{
    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(nint window);
    private readonly bool _uninstall;
    private readonly bool _updating;
    private bool _busy;
    private bool _complete;
    public MainWindow()
    {
        InitializeComponent();
        Title = "Scriblism 安裝程式";
        AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "Scriblism.ico"));
        SystemBackdrop = new MicaBackdrop();
        var scale = GetDpiForWindow(WinRT.Interop.WindowNative.GetWindowHandle(this)) / 96d;
        var workArea = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Nearest).WorkArea;
        var width = Math.Min((int)(600 * scale), workArea.Width);
        var height = Math.Min((int)(580 * scale), workArea.Height);
        AppWindow.MoveAndResize(new Windows.Graphics.RectInt32(workArea.X + (workArea.Width - width) / 2,
            workArea.Y + (workArea.Height - height) / 2, width, height));
        if (AppWindow.Presenter is OverlappedPresenter presenter) presenter.IsMaximizable = false;
        AppWindow.Closing += (_, args) => args.Cancel = _busy;
        var args = Environment.GetCommandLineArgs();
        _uninstall = args.Contains("--uninstall");
        _updating = !_uninstall && InstallationService.InstalledPath is not null;
        VersionText.Text = $"Scriblism {typeof(MainWindow).Assembly.GetName().Version?.ToString(3)}";
        InstallPath.Text = InstallationService.InstalledPath ?? InstallationService.DefaultPath;
        if (_uninstall)
        {
            Heading.Text = "解除安裝 Scriblism";
            Description.Text = "移除 Scriblism 及其本機資料。";
            Details.Text = "預設清除設定與復原備份。若想在重新安裝後繼續使用原設定，請勾選保留資料。請先儲存文件並關閉 Scriblism。";
            InstallPath.IsReadOnly = true;
            DesktopShortcut.Visibility = Visibility.Collapsed;
            KeepData.Visibility = Visibility.Visible;
            ActionButton.Content = "解除安裝";
        }
        else if (_updating)
        {
            Heading.Text = "更新 Scriblism";
            Description.Text = "直接更新目前安裝，保留設定與復原備份。";
            Details.Text = "請先儲存文件並關閉 Scriblism。更新失敗會嘗試回復原檔案。";
            InstallPath.IsReadOnly = true;
            DesktopShortcut.IsChecked = InstallationService.HasDesktopShortcut;
            DesktopShortcut.Visibility = Visibility.Collapsed;
            ActionButton.Content = "更新";
        }
    }

    private void CloseClick(object sender, RoutedEventArgs e) => Close();

    private async void ActionClick(object sender, RoutedEventArgs e)
    {
        if (_complete)
        {
            if (!_uninstall)
            {
                // Without an explicit working directory the app inherits the setup's, pinning
                // whatever folder the installer was run from for as long as the app lives.
                try
                {
                    Process.Start(new ProcessStartInfo(Path.Combine(InstallPath.Text, "Scriblism.exe"))
                    {
                        UseShellExecute = true,
                        WorkingDirectory = InstallPath.Text,
                    });
                }
                catch (Exception ex) { ShowError(ex); return; }
            }
            Close();
            return;
        }
        _busy = true;
        ActionButton.IsEnabled = CloseButton.IsEnabled = InstallPath.IsEnabled = DesktopShortcut.IsEnabled = KeepData.IsEnabled = false;
        Progress.Visibility = Visibility.Visible;
        Status.IsOpen = false;
        var target = InstallPath.Text;
        var desktop = DesktopShortcut.IsChecked == true;
        var keepData = KeepData.IsChecked == true;
        try
        {
            await Task.Run(() =>
            {
                if (_uninstall) InstallationService.Uninstall(keepData);
                else InstallationService.Install(target, desktop);
            });
            _complete = true;
            Heading.Text = _uninstall ? "已解除安裝" : _updating ? "更新完成" : "安裝完成";
            Description.Text = _uninstall ? "Scriblism 已從這台電腦的安裝位置移除。" : "現在可以開始編輯文字、程式碼與 Markdown。";
            InstallPath.Visibility = DesktopShortcut.Visibility = KeepData.Visibility = Details.Visibility = Visibility.Collapsed;
            Status.Severity = InfoBarSeverity.Success;
            Status.Message = _uninstall
                ? (keepData ? "設定與使用紀錄已保留。按「完成」結束清理。" : "程式已移除。按「完成」清除剩餘本機資料。")
                : "從開始功能表開啟，或按下方按鈕開始使用。";
            Status.IsOpen = true;
            ActionButton.Content = _uninstall ? "完成" : "開啟 Scriblism";
            CloseButton.Content = "關閉";
            if (_uninstall) CloseButton.Visibility = Visibility.Collapsed;
        }
        catch (Exception ex) { ShowError(ex); }
        finally
        {
            _busy = false;
            Progress.Visibility = Visibility.Collapsed;
            ActionButton.IsEnabled = CloseButton.IsEnabled = true;
            InstallPath.IsEnabled = DesktopShortcut.IsEnabled = KeepData.IsEnabled = !_complete;
        }
    }

    private void ShowError(Exception ex)
    {
        Status.Severity = InfoBarSeverity.Error;
        Status.Message = ex.Message;
        Status.IsOpen = true;
    }
}
