using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Data.Core;
using Avalonia.Data.Core.Plugins;
using System.Linq;
using Avalonia.Markup.Xaml;
using PBLApp.Core.Localization;
using PBLApp.Ipc;
using PBLApp.ViewModels;
using PBLApp.Views;

namespace PBLApp;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        // Eagerly initialize GameTranslationService so it subscribes to LanguageChanged
        // before any View is created. Without this, switching language before opening
        // a build would not trigger Load("ru") in GameTranslationService (it wasn't
        // subscribed yet), causing gem descriptions to display in English on first open.
        _ = GameTranslationService.Instance;

        // Apply the saved theme before MainWindow is created so the window
        // comes up in the right variant (no dark→light flash).
        Services.ThemeService.Instance.InitializeAtStartup();

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // Surface UI-thread exceptions to crash.log instead of silent process exit.
            Avalonia.Threading.Dispatcher.UIThread.UnhandledException += (_, e) =>
            {
                Program.LogCrash("Dispatcher.UIThread.UnhandledException", e.Exception);
                // keep the app alive so the user can see the error in BuildListView
                e.Handled = true;
            };

            desktop.MainWindow = new MainWindow
            {
                DataContext = new MainWindowViewModel(),
            };

            // Start IPC server for MCP visual control. Only when --enable-ipc is passed
            // (PBLApp by itself runs without IPC; MCP launches it with --enable-ipc).
            if (desktop.Args is { } args && args.Any(a => a == "--enable-ipc"))
            {
                try { IpcServer.Start(); } catch { /* swallow — IPC is optional */ }
            }

            desktop.Exit += (_, _) => IpcServer.Current?.Stop();
        }

        base.OnFrameworkInitializationCompleted();
    }
}