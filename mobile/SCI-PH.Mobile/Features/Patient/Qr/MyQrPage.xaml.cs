using System.ComponentModel;
using SCI_PH.Mobile.Features.Patient.ViewModels;
using ZXing.Net.Maui;
using ZXing.Net.Maui.Controls;

namespace SCI_PH.Mobile.Features.Patient.Qr;

public partial class MyQrPage : ContentPage
{
    private readonly MyQrViewModel _viewModel;
    private readonly IDispatcherTimer _timer;

    public MyQrPage(MyQrViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = _viewModel = viewModel;
        _viewModel.PropertyChanged += OnViewModelChanged;

        _timer = Dispatcher.CreateTimer();
        _timer.Interval = TimeSpan.FromSeconds(1);
        _timer.Tick += async (_, _) => await _viewModel.TickAsync();
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        _timer.Start();
        await _viewModel.LoadCommand.ExecuteAsync(null);
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        _timer.Stop();
    }

    // La vista solo existe con un contenido real: sin QR no queda ninguna imagen ni contenido en ella.
    private void OnViewModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MyQrViewModel.Payload))
        {
            QrHost.Content = _viewModel.Payload is { } payload
                ? new BarcodeGeneratorView
                {
                    Format = BarcodeFormat.QrCode,
                    ForegroundColor = Colors.Black,
                    BackgroundColor = Colors.White,
                    WidthRequest = 280,
                    HeightRequest = 280,
                    Margin = 2,
                    Value = payload
                }
                : null;
        }
    }
}
