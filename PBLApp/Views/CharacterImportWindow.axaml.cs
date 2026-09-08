using Avalonia.Controls;
using PBLApp.Controls;
using PBLApp.ViewModels;

namespace PBLApp.Views;

public partial class CharacterImportWindow : Window
{
    public CharacterImportWindow()
    {
        InitializeComponent();
        WindowDefaults.Apply(this);

        // Уже вошли в аккаунт — сразу тянем список, чтобы окно не встречало пустотой.
        Opened += (_, _) =>
        {
            if (DataContext is CharacterImportViewModel { IsLoggedIn: true } vm)
                _ = vm.LoadCharactersAsync();
        };
    }
}
