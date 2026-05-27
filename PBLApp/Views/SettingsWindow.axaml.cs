using Avalonia.Controls;
using PBLApp.Controls;

namespace PBLApp.Views;

public partial class SettingsWindow : Window
{
    public SettingsWindow()
    {
        InitializeComponent();
        WindowDefaults.Apply(this);
    }
}
