using System.Diagnostics;

namespace Scriblism.Core.Installation;

public static class InstalledAppShutdown
{
    public static void Stop(string installationDirectory)
    {
        var executable = Path.GetFullPath(Path.Combine(installationDirectory, "Scriblism.exe"));
        foreach (var process in Process.GetProcessesByName("Scriblism"))
        {
            using (process)
            {
                try
                {
                    if (process.HasExited) continue;
                    if (!string.Equals(process.MainModule?.FileName, executable, StringComparison.OrdinalIgnoreCase)) continue;
                    throw new IOException("請先儲存文件並關閉 Scriblism，再繼續安裝或解除安裝。");
                }
                catch (System.ComponentModel.Win32Exception error)
                {
                    throw new IOException("無法確認 Scriblism 是否仍在執行，請先關閉程式。", error);
                }
            }
        }
    }
}
