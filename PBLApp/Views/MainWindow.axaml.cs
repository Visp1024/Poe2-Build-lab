using Avalonia.Controls;
using PBLApp.Controls;

namespace PBLApp.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        WindowDefaults.Apply(this);
    }
}