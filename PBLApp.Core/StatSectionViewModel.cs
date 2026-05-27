using PBLApp.Core.Localization;
using System.Collections.ObjectModel;

namespace PBLApp.ViewModels;

public class StatSectionViewModel : ViewModelBase
{
    private readonly string _key;

    public string Label => LocalizationService.Get(_key);
    public ObservableCollection<StatRowViewModel> Rows { get; } = [];

    public StatSectionViewModel(string key)
    {
        _key = key;
        LocalizationService.Instance.LanguageChanged += (_, _) => OnPropertyChanged(nameof(Label));
    }
}
