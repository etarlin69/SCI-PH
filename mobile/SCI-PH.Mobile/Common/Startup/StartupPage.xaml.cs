namespace SCI_PH.Mobile.Common.Startup;

public partial class StartupPage : ContentPage
{
    private readonly StartupViewModel _viewModel;

    public StartupPage(StartupViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = _viewModel = viewModel;
        Loaded += OnLoaded;
    }

    // Se navega cuando la página ya está cargada y fuera de ese pase: hacerlo mientras el Shell crea sus handlers
    // (por ejemplo en OnAppearing al arrancar) falla con "Pending Navigations still processing".
    private void OnLoaded(object? sender, EventArgs e)
    {
        Loaded -= OnLoaded;
        Dispatcher.Dispatch(async () => await _viewModel.StartAsync());
    }
}
