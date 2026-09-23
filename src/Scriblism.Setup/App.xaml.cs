using System.Diagnostics;
using System.IO;
using Microsoft.UI.Xaml;
using System.Runtime.InteropServices;
using Scriblism.Core.Lifecycle;

namespace Scriblism.Setup;

public partial class App : Application
{
    [DllImport("user32.dll")]
    private static extern bool ShowWindow(nint window, int command);
    private Window? _window;
    private SingleInstanceGate? _instanceGate;
    public App() => InitializeComponent();
    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        var rawArgs = Environment.GetCommandLineArgs();
        var processName = Path.GetFileNameWithoutExtension(Environment.ProcessPath ?? "");
        var isUninstall = rawArgs.Contains("--uninstall") || processName.Equals("Uninstall", StringComparison.OrdinalIgnoreCase);

        var target = InstallationService.InstalledPath;
        var currentDir = Path.TrimEndingDirectorySeparator(AppContext.BaseDirectory);

        // If running for uninstall directly from the installed target folder, replicate to %TEMP%
        // (following the bootstrap installer pattern) so the target directory can be deleted cleanly.
        if (isUninstall && target is not null &&
            currentDir.Equals(Path.TrimEndingDirectorySeparator(Path.GetFullPath(target)), StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                var tempDir = Path.Combine(Path.GetTempPath(), "Scriblism.Uninstall", Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(tempDir);
                foreach (var file in Directory.GetFiles(currentDir))
                {
                    File.Copy(file, Path.Combine(tempDir, Path.GetFileName(file)), true);
                }
                foreach (var dir in Directory.GetDirectories(currentDir))
                {
                    var destSub = Path.Combine(tempDir, Path.GetFileName(dir));
                    Directory.CreateDirectory(destSub);
                    foreach (var file in Directory.GetFiles(dir, "*", SearchOption.AllDirectories))
                    {
                        var rel = Path.GetRelativePath(dir, file);
                        var targetFile = Path.Combine(destSub, rel);
                        Directory.CreateDirectory(Path.GetDirectoryName(targetFile)!);
                        File.Copy(file, targetFile, true);
                    }
                }

                var psi = new ProcessStartInfo(Path.Combine(tempDir, "Scriblism.Setup.exe"))
                {
                    UseShellExecute = true,
                    WorkingDirectory = tempDir,
                };
                psi.ArgumentList.Add("--uninstall");
                psi.ArgumentList.Add("--temp-root");
                psi.ArgumentList.Add(tempDir);
                foreach (var item in rawArgs.Skip(1))
                {
                    if (!item.Equals("--uninstall", StringComparison.OrdinalIgnoreCase) &&
                        !item.StartsWith("--temp-root", StringComparison.OrdinalIgnoreCase))
                    {
                        psi.ArgumentList.Add(item);
                    }
                }
                Process.Start(psi);
                Exit();
                return;
            }
            catch
            {
                // If replication fails, fall through to normal launch
            }
        }

        var dispatcher = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();
        _instanceGate = new SingleInstanceGate("Scriblism.Setup", () => dispatcher.TryEnqueue(() =>
        {
            if (_window is null) return;
            ShowWindow(WinRT.Interop.WindowNative.GetWindowHandle(_window), 9);
            _window.Activate();
        }));
        if (!_instanceGate.IsPrimary) { _instanceGate.Dispose(); Exit(); return; }
        _window = new MainWindow();
        _window.Closed += (_, _) =>
        {
            _window = null;
            _instanceGate.Dispose();

            var tempRootIdx = Array.IndexOf(rawArgs, "--temp-root");
            if (tempRootIdx >= 0 && tempRootIdx + 1 < rawArgs.Length)
            {
                var tempPath = rawArgs[tempRootIdx + 1];
                var expectedParent = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "Scriblism.Uninstall"));
                var fullPath = Path.GetFullPath(tempPath);
                if (Directory.Exists(fullPath) &&
                    string.Equals(Path.GetDirectoryName(fullPath), expectedParent, StringComparison.OrdinalIgnoreCase) &&
                    Guid.TryParseExact(Path.GetFileName(fullPath), "N", out _))
                {
                    try
                    {
                        var cleanupScript = Path.Combine(Path.GetTempPath(), "Scriblism-cleanup-" + Guid.NewGuid().ToString("N") + ".ps1");
                        File.WriteAllText(cleanupScript, """
                            param([string]$Target, [int]$OwnerPid)
                            try {
                                Wait-Process -Id $OwnerPid -Timeout 120 -ErrorAction SilentlyContinue
                                $full = [IO.Path]::GetFullPath($Target)
                                $parent = [IO.Path]::GetFullPath((Join-Path $env:TEMP 'Scriblism.Uninstall'))
                                if ([IO.Path]::GetDirectoryName($full) -ne $parent -or [guid]::Empty -eq [guid]::ParseExact([IO.Path]::GetFileName($full), 'N')) { exit 1 }
                                if (Test-Path -LiteralPath $full) {
                                    $links = @(Get-ChildItem -LiteralPath $full -Recurse -Force | Where-Object { $_.Attributes -band [IO.FileAttributes]::ReparsePoint })
                                    if ($links.Count -gt 0) { throw 'Cleanup target contains a link.' }
                                    for ($attempt = 0; $attempt -lt 20 -and (Test-Path -LiteralPath $full); $attempt++) {
                                        try { Remove-Item -LiteralPath $full -Recurse -Force -ErrorAction Stop }
                                        catch { if ($attempt -eq 19) { throw }; Start-Sleep -Milliseconds 500 }
                                    }
                                }
                            } catch {
                                Add-Content -LiteralPath (Join-Path $env:TEMP 'Scriblism-cleanup-error.log') -Value $_.Exception.Message
                            } finally { Remove-Item -LiteralPath $PSCommandPath -Force -ErrorAction SilentlyContinue }
                            """);
                        var cleanup = new ProcessStartInfo("powershell.exe")
                        {
                            CreateNoWindow = true,
                            WindowStyle = ProcessWindowStyle.Hidden,
                        };
                        cleanup.ArgumentList.Add("-NoProfile");
                        cleanup.ArgumentList.Add("-NonInteractive");
                        cleanup.ArgumentList.Add("-ExecutionPolicy");
                        cleanup.ArgumentList.Add("Bypass");
                        cleanup.ArgumentList.Add("-WindowStyle");
                        cleanup.ArgumentList.Add("Hidden");
                        cleanup.ArgumentList.Add("-File");
                        cleanup.ArgumentList.Add(cleanupScript);
                        cleanup.ArgumentList.Add("-Target");
                        cleanup.ArgumentList.Add(fullPath);
                        cleanup.ArgumentList.Add("-OwnerPid");
                        cleanup.ArgumentList.Add(Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture));
                        Process.Start(cleanup);
                    }
                    catch { }
                }
            }
        };
        _window.Activate();
        ShowWindow(WinRT.Interop.WindowNative.GetWindowHandle(_window), 5);
        _window.Activate();
    }
}
