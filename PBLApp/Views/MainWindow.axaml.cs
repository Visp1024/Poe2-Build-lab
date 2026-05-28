using System;
using PBLApp.Controls;
using SukiUI.Controls;

namespace PBLApp.Views;

public partial class MainWindow : SukiWindow
{
    public MainWindow()
    {
        InitializeComponent();
        WindowDefaults.Apply(this);
        Opened += OnOpened;
    }

    private async void OnOpened(object? sender, EventArgs e)
    {
        Opened -= OnOpened;
        await CreditsWindow.ShowIfNeeded(this);
    }
}