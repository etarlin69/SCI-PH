namespace SCI_PH.Mobile.Common.Entry;

public partial class EntryPage : ContentPage
{
    public EntryPage(EntryViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }
}
