using Avalonia;
using System;
using System.IO;
using System.Threading.Tasks;

namespace PBLApp;

sealed class Program
{
    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    [STAThread]
    public static void Main(string[] args)
    {
        InstallCrashHandlers();
        try
        {
            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }
        catch (Exception ex)
        {
            LogCrash("Main", ex);
            throw;
        }
    }

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
#if DEBUG
            .WithDeveloperTools()
#endif
            .WithInterFont()
            .LogToTrace();

    private static void InstallCrashHandlers()
    {
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            LogCrash("AppDomain.UnhandledException", e.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            LogCrash("TaskScheduler.UnobservedTaskException", e.Exception);
            e.SetObserved();
        };
    }

    internal static void LogCrash(string source, Exception? ex)
    {
        try
        {
            var dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "PathOfBuilding2");
            Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, "crash.log");
            var entry =
                $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {source}\n" +
                $"BaseDir: {AppContext.BaseDirectory}\n" +
                $"CWD:     {Directory.GetCurrentDirectory()}\n" +
                (ex?.ToString() ?? "(null exception)") +
                "\n----------------------------------------\n";
            File.AppendAllText(path, entry);
        }
        catch { /* nothing we can do */ }
    }
}
