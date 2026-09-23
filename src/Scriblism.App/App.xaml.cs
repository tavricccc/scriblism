using Microsoft.UI.Xaml;

namespace Scriblism.App;

public partial class App : Application
{
    private MainWindow? _window;
    public App()
    {
        InitializeComponent();
        UnhandledException += (_, e) => Log(e.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, e) => Log(e.ExceptionObject as Exception);
    }
    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        _window = new MainWindow(Environment.GetCommandLineArgs().Skip(1).ToArray());
        _window.Activate();
    }
    internal static void Log(Exception? error)
    {
        try
        {
            var directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Scriblism");
            Directory.CreateDirectory(directory);
            File.AppendAllText(Path.Combine(directory, "errors.log"), $"{DateTime.Now:O} {error}\n");
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
