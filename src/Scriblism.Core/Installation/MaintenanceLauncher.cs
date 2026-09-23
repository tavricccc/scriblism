namespace Scriblism.Core.Installation;

public static class MaintenanceLauncher
{
    public static string CreateCommand(string target)
    {
        return $"\"{Path.Combine(target, "Uninstall.exe")}\"";
    }
}
