using Avalonia.Controls;
using GBFileCopier.App.ViewModels;

namespace GBFileCopier.App.Views;

public partial class ConflictWindow : Window
{
    public ConflictWindow()
    {
        InitializeComponent();
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (DataContext is ConflictViewModel viewModel)
        {
            viewModel.Answered += answer => Close(answer);
        }
    }
}
