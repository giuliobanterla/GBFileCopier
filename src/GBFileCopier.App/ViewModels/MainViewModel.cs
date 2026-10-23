using CommunityToolkit.Mvvm.ComponentModel;
using GBFileCopier.Core;

namespace GBFileCopier.App.ViewModels;

public partial class MainViewModel : ViewModelBase
{
    public string Title { get; } = $"{AppInfo.Name} {AppInfo.Version}";

    [ObservableProperty]
    public partial string Status { get; set; } = "Ready";
}
