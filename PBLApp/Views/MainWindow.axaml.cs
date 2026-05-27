using PBLApp.Controls;
using SukiUI.Controls;

namespace PBLApp.Views;

public partial class MainWindow : SukiWindow
{
    public MainWindow()
    {
        InitializeComponent();
        WindowDefaults.Apply(this);
    }
}