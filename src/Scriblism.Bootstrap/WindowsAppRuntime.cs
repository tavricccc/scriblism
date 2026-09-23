using System.Runtime.InteropServices;
using Windows.Management.Deployment;

namespace Scriblism.Bootstrap;

/// <summary>
/// Registers the shared Windows App SDK runtime before the installer's own interface starts.
/// </summary>
/// <remarks>
/// Scriblism's WinUI is framework-dependent: the Windows App SDK binaries come from one MSIX
/// framework package that Windows keeps in a single shared location, instead of a 145 MB copy
/// inside every installation. Flowlism and Downlism point at the same package, so the three of
/// them stop carrying three identical copies on disk and, whenever more than one is open,
/// three copies of the same code pages in memory.
///
/// The packages travel inside the installer rather than being downloaded. It makes the download
/// larger and the installation simpler: no network at the one moment the product cannot yet
/// run, no progress bar that stalls on someone's hotel wifi, no second thing that can fail.
/// What is shared is where they end up, not where they come from.
///
/// This runs here rather than in the setup interface because the setup interface is itself a
/// WinUI application — it cannot be the thing that registers what it needs in order to start.
/// The bootstrap launcher is plain .NET with its own copy of the runtime, so it starts on a
/// machine that has nothing.
///
/// Declining, or failing, is no longer the end of the install: the same installer carries a
/// layout that needs none of this, and the caller falls back to it.
///
/// No elevation is requested. Windows stages the packages and registers them for the user
/// running the installer, which is all a per-user installation needs; provisioning them for
/// every user on the machine is the only part that wants an administrator.
/// </remarks>
internal static class WindowsAppRuntime
{
    /// <summary>
    /// The runtime packages Scriblism's WinUI resolves at run time.
    /// </summary>
    private static readonly (string FileName, string FamilyName, Version MinVersion)[] Packages =
    [
        ("Microsoft.WindowsAppRuntime.2.msix", "Microsoft.WindowsAppRuntime.2_8wekyb3d8bbwe", new Version(2, 4, 0, 0)),
        ("Microsoft.WindowsAppRuntime.Main.2.msix", "MicrosoftCorporationII.WinAppRuntime.Main.2_8wekyb3d8bbwe", new Version(2, 4, 0, 0)),
        ("Microsoft.WindowsAppRuntime.Singleton.2.msix", "MicrosoftCorporationII.WinAppRuntime.Singleton_8wekyb3d8bbwe", new Version(8002, 4, 0, 0)),
        ("Microsoft.WindowsAppRuntime.DDLM.2.msix", "Microsoft.WinAppRuntime.DDLM.2.4.0.0-x6_8wekyb3d8bbwe", new Version(2, 4, 0, 0)),
    ];

    private static bool IsPackageInstalled(PackageManager manager, string familyName, Version minVersion)
    {
        try
        {
            foreach (var package in manager.FindPackagesForUser(string.Empty, familyName))
            {
                var version = package.Id.Version;
                if (new Version(version.Major, version.Minor, version.Build, version.Revision) >= minVersion)
                    return true;
            }
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException or COMException)
        {
        }

        return false;
    }

    /// <summary>Whether all required Windows App Runtime packages are already registered for this user.</summary>
    public static bool IsPresent()
    {
        try
        {
            var manager = new PackageManager();
            foreach (var (_, familyName, minVersion) in Packages)
            {
                if (!IsPackageInstalled(manager, familyName, minVersion))
                    return false;
            }

            return true;
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException or COMException)
        {
        }

        return false;
    }

    /// <summary>
    /// Registers missing packages that ship beside the installer, then confirms the result.
    /// Existing packages are preserved and never reinstalled.
    /// </summary>
    /// <param name="directory">Where the packages were unpacked to.</param>
    /// <param name="progress">The caller's dialog, which is already on screen by this point.</param>
    public static void Install(string directory, ProgressDialog progress)
    {
        if (!Directory.Exists(directory)) throw new IOException($"找不到執行環境套件資料夾：{directory}");

        var manager = new PackageManager();

        // Check which packages are missing; only install the missing ones, skipping what already exists.
        var missing = Packages
            .Where(p => File.Exists(Path.Combine(directory, p.FileName)) && !IsPackageInstalled(manager, p.FamilyName, p.MinVersion))
            .ToArray();

        if (missing.Length == 0)
        {
            if (IsPresent()) return;
            throw new IOException($"{directory} 裡沒有足夠的執行環境套件可供安裝。");
        }

        Exception? firstFailure = null;
        var total = missing.Length * 100L;

        for (var index = 0; index < missing.Length; index++)
        {
            var (fileName, _, _) = missing[index];
            var path = Path.Combine(directory, fileName);
            var completed = index * 100L;

            try
            {
                var operation = manager.AddPackageAsync(new Uri(path), null, DeploymentOptions.None);
                operation.Progress = (_, state) => progress.Report(completed + state.percentage, total);

                if (operation.AsTask().GetAwaiter().GetResult().ExtendedErrorCode is { } error) firstFailure ??= error;
            }
            catch (Exception exception)
            {
                firstFailure ??= exception;
            }

            progress.Report(completed + 100, total);
        }

        if (IsPresent()) return;

        throw new IOException(
            "無法登錄 Windows App 執行環境。" + (firstFailure is null ? string.Empty : "\n" + firstFailure.Message));
    }
}
