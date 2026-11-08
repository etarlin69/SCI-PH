using System.ComponentModel;
using SCI_PH.Mobile.Features.Paramedic.Scanner;
using SCI_PH.Mobile.Features.Paramedic.ViewModels;
using ZXing.Net.Maui;
using ZXing.Net.Maui.Controls;

namespace SCI_PH.Mobile.Features.Paramedic.Views;

/// <summary>
/// Escáner de QR del paramédico. La vista de cámara de ZXing solo existe mientras la página se ve, la app está en primer plano
/// y el estado es de lectura: al salir, al pasar a segundo plano, con el candidato y al confirmar se apaga y se desconecta, así
/// la cámara nunca queda activa detrás de otra página. ZXing avisa desde el hilo de la cámara: allí solo se decide, de forma
/// atómica, la primera lectura (<see cref="QrScanGate"/>); el resto ocurre en el hilo de la interfaz.
/// </summary>
public partial class QrScannerPage : ContentPage
{
    private readonly QrScannerViewModel _viewModel;
    private readonly QrScanGate _gate = new();
    private CameraBarcodeReaderView? _reader;
    private Window? _window;
    private Shell? _shell;
#if WINDOWS
    private Microsoft.UI.Xaml.Window? _platformWindow;
#endif
    private bool _visible;
    private bool _foreground = true;

    public QrScannerPage(QrScannerViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = _viewModel = viewModel;
        _viewModel.PropertyChanged += OnViewModelChanged;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        _visible = true;
        _foreground = true;
        AttachWindow();
        AttachShell();
        await _viewModel.ActivateAsync();
        UpdateCamera();
    }

    protected override void OnDisappearing()
    {
        _visible = false;
        DetachWindow();
        DetachShell();
        StopCamera();
        _viewModel.Deactivate();
        base.OnDisappearing();
    }

    // Atrás de Shell (barra y botón del sistema) mientras se confirma: la petición sigue su curso y su respuesta decide.
    private void AttachShell()
    {
        DetachShell();
        _shell = Shell.Current;
        if (_shell is not null)
        {
            _shell.Navigating += OnShellNavigating;
        }
    }

    private void DetachShell()
    {
        if (_shell is not null)
        {
            _shell.Navigating -= OnShellNavigating;
            _shell = null;
        }
    }

    private void OnShellNavigating(object? sender, ShellNavigatingEventArgs e)
    {
        if (e.CanCancel && _viewModel.BlocksNavigation(e.Source))
        {
            e.Cancel();
        }
    }

    protected override bool OnBackButtonPressed() =>
        _viewModel.BlocksNavigation(ShellNavigationSource.Pop) || base.OnBackButtonPressed();

    private void AttachWindow()
    {
        DetachWindow();
        // En Android, Window aún es null en OnAppearing para una página que Shell acaba de abrir: sin esta alternativa no
        // llegarían Stopped ni Resumed (comprobado en el emulador). La app tiene una sola ventana.
        _window = Window ?? Application.Current?.Windows.FirstOrDefault();
        if (_window is not null)
        {
            _window.Stopped += OnWindowStopped;
            _window.Resumed += OnWindowResumed;
#if WINDOWS
            // En Windows, al restaurar una ventana minimizada puede no llegar Resumed: se mira lo contrario de Stopped, que
            // MAUI obtiene de este mismo evento.
            _platformWindow = _window.Handler?.PlatformView as Microsoft.UI.Xaml.Window;
            if (_platformWindow is not null)
            {
                _platformWindow.VisibilityChanged += OnPlatformVisibilityChanged;
            }
#endif
        }
    }

#if WINDOWS
    private void OnPlatformVisibilityChanged(object sender, Microsoft.UI.Xaml.WindowVisibilityChangedEventArgs args)
    {
        if (args.Visible)
        {
            OnWindowResumed(sender, EventArgs.Empty);
        }
    }
#endif

    private void DetachWindow()
    {
        if (_window is not null)
        {
            _window.Stopped -= OnWindowStopped;
            _window.Resumed -= OnWindowResumed;
            _window = null;
        }

#if WINDOWS
        if (_platformWindow is not null)
        {
            _platformWindow.VisibilityChanged -= OnPlatformVisibilityChanged;
            _platformWindow = null;
        }
#endif
    }

    // Segundo plano: la cámara se apaga. Nada se confirma al volver.
    private void OnWindowStopped(object? sender, EventArgs e)
    {
        _foreground = false;
        StopCamera();
    }

    private async void OnWindowResumed(object? sender, EventArgs e)
    {
        // Sin haber pasado a segundo plano no hay nada que reanudar (en Windows pueden llegar los dos avisos).
        if (_foreground)
        {
            return;
        }

        _foreground = true;
        if (!_visible)
        {
            return;
        }

        await _viewModel.ResumeAsync();
        UpdateCamera();
    }

    private void OnViewModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(QrScannerViewModel.State):
                UpdateCamera();
                break;
            case nameof(QrScannerViewModel.IsDetecting):
                ApplyDetecting();
                break;
        }
    }

    private void UpdateCamera()
    {
        if (_visible && _foreground && _viewModel.IsCameraActive)
        {
            StartCamera();
        }
        else
        {
            StopCamera();
        }
    }

    private void StartCamera()
    {
        if (_reader is not null)
        {
            ApplyDetecting();
            return;
        }

        _reader = new CameraBarcodeReaderView
        {
            Options = new BarcodeReaderOptions
            {
                Formats = BarcodeFormat.QrCode,
                Multiple = false,
                AutoRotate = true
            },
            CameraLocation = CameraLocation.Rear,
            IsDetecting = false
        };
        _reader.BarcodesDetected += OnBarcodesDetected;
        CameraHost.Content = _reader;
        ApplyDetecting();
    }

    private void StopCamera()
    {
        if (_reader is not { } reader)
        {
            return;
        }

        _reader = null;
        reader.IsDetecting = false;
        reader.BarcodesDetected -= OnBarcodesDetected;
        CameraHost.Content = null;
        // MAUI 9 no desconecta el handler al quitar la vista: sin esto la cámara seguiría abierta.
        reader.Handler?.DisconnectHandler();
    }

    private void ApplyDetecting()
    {
        if (_reader is null)
        {
            return;
        }

        if (_viewModel.IsDetecting)
        {
            _gate.Rearm();
            _reader.IsDetecting = true;
        }
        else
        {
            _reader.IsDetecting = false;
        }
    }

    // Hilo de la cámara: solo la decisión atómica. Las repeticiones del mismo cuadro y otros formatos se ignoran.
    private void OnBarcodesDetected(object? sender, BarcodeDetectionEventArgs e)
    {
        if (_gate.TryAccept(e.Results) is not { } value)
        {
            return;
        }

        Dispatcher.Dispatch(async () =>
        {
            if (sender is CameraBarcodeReaderView reader)
            {
                reader.IsDetecting = false;
            }

            if (!ReferenceEquals(sender, _reader))
            {
                return;
            }

            await _viewModel.ProcessScanAsync(value);
        });
    }
}
