using Avalonia.Controls;
using PBLApp.Services;
using PBLApp.ViewModels;

namespace PBLApp.Views;

public partial class AppSettingsWindow : Window
{
    public AppSettingsWindow()
    {
        InitializeComponent();
        DataContext = new AppSettingsViewModel(ThemeService.Instance);
    }
}
