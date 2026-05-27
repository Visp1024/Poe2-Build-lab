using Avalonia.Controls;
using PBLApp.Controls;

namespace PBLApp.Views;

public partial class TabWindow : Window
{
    public TabWindow()
    {
        InitializeComponent();
        WindowDefaults.Apply(this);
    }
}
