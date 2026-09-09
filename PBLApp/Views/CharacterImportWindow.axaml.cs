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
        // Кроме случая, когда окно открыто ИЗ-ЗА ошибки («Из игры» не смогла обновить
        // билд молча): загрузка списка затёрла бы причину, ради которой окно и открыли.
        Opened += (_, _) =>
        {
            if (DataContext is CharacterImportViewModel
                { IsLoggedIn: true, StatusKind: not ImportStatusKind.Error } vm)
                _ = vm.LoadCharactersAsync();
        };
    }
}
