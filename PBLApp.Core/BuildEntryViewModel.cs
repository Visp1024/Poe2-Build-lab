using CommunityToolkit.Mvvm.ComponentModel;
using System.Collections.ObjectModel;

namespace PBLApp.ViewModels;

public partial class BuildEntryViewModel : ObservableObject
{
    public string Name { get; }
    public string Path { get; }
    public bool IsFolder { get; }

    public ObservableCollection<BuildEntryViewModel> Children { get; } = new();

    public BuildEntryViewModel(string name, string path, bool isFolder)
    {
        Name = name;
        Path = path;
        IsFolder = isFolder;
    }
}
