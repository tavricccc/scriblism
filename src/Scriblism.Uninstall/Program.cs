using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Scriblism.Uninstall;

internal static class Program
{
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int MessageBox(nint hWnd, string text, string caption, uint type);

    private const uint IconError = 0x10;

    [STAThread]
    private static int Main(string[] args)
    {
        try
        {
            var sourceDir = Path.TrimEndingDirectorySeparator(AppContext.BaseDirectory);
            var setupSource = Path.Combine(sourceDir, "Scriblism.Setup.exe");
            if (!File.Exists(setupSource))
            {
                MessageBox(0, "找不到 Scriblism.Setup.exe，無法啟動解除安裝程式。", "Scriblism", IconError);
                return 1;
            }

            var tempDir = Path.Combine(Path.GetTempPath(), "Scriblism.Uninstall", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);

            foreach (var file in Directory.GetFiles(sourceDir))
            {
                var name = Path.GetFileName(file);
                // No need to copy uninstall launcher itself or app logs
                if (name.StartsWith("Uninstall", StringComparison.OrdinalIgnoreCase)) continue;
                File.Copy(file, Path.Combine(tempDir, name), true);
            }

            foreach (var dir in Directory.GetDirectories(sourceDir))
            {
                var dirName = Path.GetFileName(dir);
                var destSub = Path.Combine(tempDir, dirName);
                Directory.CreateDirectory(destSub);
                foreach (var file in Directory.GetFiles(dir, "*", SearchOption.AllDirectories))
                {
                    var rel = Path.GetRelativePath(dir, file);
                    var targetFile = Path.Combine(destSub, rel);
                    Directory.CreateDirectory(Path.GetDirectoryName(targetFile)!);
                    File.Copy(file, targetFile, true);
                }
            }

            var extraArgs = string.Join(" ", args
                .Where(item => !item.Equals("--uninstall", StringComparison.OrdinalIgnoreCase) &&
                               !item.StartsWith("--temp-root", StringComparison.OrdinalIgnoreCase))
                .Select(item => $"\"{item}\""));

            var psi = new ProcessStartInfo(Path.Combine(tempDir, "Scriblism.Setup.exe"))
            {
                UseShellExecute = true,
                WorkingDirectory = tempDir,
                Arguments = string.IsNullOrWhiteSpace(extraArgs)
                    ? $"--uninstall --temp-root \"{tempDir}\""
                    : $"--uninstall --temp-root \"{tempDir}\" {extraArgs}",
            };

            Process.Start(psi);
            return 0;
        }
        catch (Exception ex)
        {
            MessageBox(0, "啟動解除安裝程式時發生錯誤：\n" + ex.Message, "Scriblism", IconError);
            return 1;
        }
    }
}
