using System;
using System.IO;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace PBLApp.Views;

public partial class CreditsWindow : Window
{
    private static readonly string FlagPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "PathOfBuilding", "credits_hidden.txt");

    public CreditsWindow()
    {
        InitializeComponent();
    }

    public static bool ShouldShow() => !File.Exists(FlagPath);

    public static async Task ShowIfNeeded(Window owner)
    {
        if (!ShouldShow()) return;
        var dlg = new CreditsWindow();
        await dlg.ShowDialog(owner);
    }

    private void Close_Click(object? sender, RoutedEventArgs e)
    {
        if (DontShowCheck.IsChecked == true)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FlagPath)!);
                File.WriteAllText(FlagPath, "1");
            }
            catch { /* best-effort: if write fails, we just keep showing */ }
        }
        Close();
    }
}
