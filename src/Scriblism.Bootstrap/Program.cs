using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Scriblism.Bootstrap;

internal static class Program
{
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int MessageBox(nint window, string text, string caption, uint type);

    private const uint IconError = 0x10;
    private const uint IconQuestion = 0x24;
    private const uint IconInformation = 0x40;
    private const int Yes = 6;

    [STAThread]
    private static int Main(string[] args)
    {
        string? root = null;

        try
        {
            using var payload = Payload.Open();

            // Unpacked beside the other per-user temporary files, under a name of its own so an
            // interrupted install leaves something recognisable rather than loose binaries. The
            // runtime packages are unpacked next to the installation rather than into it: they
            // belong to the installer, and the setup interface copies what it finds beside it.
            root = Path.Combine(Path.GetTempPath(), "Scriblism.Setup", Guid.NewGuid().ToString("N"));
            var work = Path.Combine(root, "app");
            Directory.CreateDirectory(work);

            var layout = ChooseLayout(payload, Path.Combine(root, "runtime"));
            Unpack(payload, layout, work);

            // The setup interface is a WinUI application, so it must run from the folder it was
            // unpacked into: its XAML and resource lookups are relative to the executable.
            var start = new ProcessStartInfo(Path.Combine(work, "Scriblism.Setup.exe"))
            {
                UseShellExecute = false,
                WorkingDirectory = work,
            };
            foreach (var argument in args) start.ArgumentList.Add(argument);
            using var process = Process.Start(start) ?? throw new IOException("無法啟動安裝介面。");
            process.WaitForExit();
            return process.ExitCode;
        }
        catch (Exception exception)
        {
            MessageBox(0, "無法開啟安裝程式。\n" + exception.Message, "Scriblism", IconError);
            return 1;
        }
        finally
        {
            // The installation has already been copied to its own folder by this point, so what
            // is left here is a spent copy. A failure to remove it is not worth a second error
            // dialog on top of whatever brought us here.
            try
            {
                if (root is not null && Directory.Exists(root)) Directory.Delete(root, recursive: true);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
            }
        }
    }

    /// <summary>
    /// Decides which of the two layouts to install, registering the shared Windows App SDK
    /// runtime first if it is wanted and missing.
    /// </summary>
    /// <remarks>
    /// There used to be two installers, and a machine that would not register shared components
    /// was told to go and find the other one. The two layouts now travel together and the
    /// choice is made here, because every reason to prefer one is something this code can see:
    /// whether the framework package is already registered, whether the person wants it, and
    /// whether registering it actually worked. Nobody has to know which installer they need
    /// before they have found out that they needed a different one.
    /// </remarks>
    private static string ChooseLayout(System.IO.Compression.ZipArchive payload, string packages)
    {
        // Already registered by an earlier install or another Windows App SDK application.
        // about, and the shared layout is the smaller installation.
        if (WindowsAppRuntime.IsPresent()) return Payload.SharedPrefix;

        var answer = MessageBox(
            0,
            "這台電腦還沒有 Scriblism 需要的 Microsoft 元件。要安裝可供其他 App 共用的版本嗎？\n\n"
                + "選「是」可節省安裝空間；選「否」也能繼續安裝 Scriblism。",
            "Scriblism",
            IconQuestion);

        if (answer != Yes) return Payload.StandalonePrefix;

        try
        {
            using var progress = new ProgressDialog("Scriblism", "正在準備必要元件…");
            Payload.Extract(payload, [Payload.RuntimePrefix], packages);
            WindowsAppRuntime.Install(packages, progress);
            return Payload.SharedPrefix;
        }
        catch (Exception exception)
        {
            // Not a dead end any more. The commonest cause is a machine where registering
            // packages is not permitted at all, and the layout that needs no shared component
            // is right here, so the install carries on with it.
            MessageBox(
                0,
                "無法安裝共用元件：\n"
                    + exception.Message
                    + "\n\n將繼續安裝 Scriblism。",
                "Scriblism",
                IconInformation);
            return Payload.StandalonePrefix;
        }
    }

    private static void Unpack(System.IO.Compression.ZipArchive payload, string layout, string work)
    {
        using var progress = new ProgressDialog("Scriblism", "正在準備安裝內容…");
        Payload.Extract(payload, [Payload.CommonPrefix, layout], work, (done, total) => progress.Report(done, total));
    }
}
