using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Quest_Companion.ViewModels;
using Wpf.Ui.Controls;

namespace Quest_Companion;

/// <summary>
/// Interaction logic for MainWindow.xaml
/// </summary>
public partial class MainWindow : FluentWindow
{
    private readonly MainViewModel _viewModel;
    private System.Drawing.Icon? _iconConnected;
    private System.Drawing.Icon? _iconDisconnected;

    public MainWindow()
    {
        InitializeComponent();

        // Safely set Window & Taskbar Icon from embedded resources
        try
        {
            var iconStreamInfo = Application.GetResourceStream(new Uri("pack://application:,,,/Assets/app.ico", UriKind.Absolute))
                              ?? Application.GetResourceStream(new Uri("/Assets/app.ico", UriKind.Relative));
            if (iconStreamInfo?.Stream != null)
            {
                using var ms = new MemoryStream();
                iconStreamInfo.Stream.CopyTo(ms);
                ms.Position = 0;
                Icon = BitmapFrame.Create(ms, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
            }
        }
        catch { }

        Quest_Companion.Converters.ThemeHelper.ApplySystemAccentToApplication();
        Wpf.Ui.Appearance.SystemThemeWatcher.Watch(this, Wpf.Ui.Controls.WindowBackdropType.Mica, updateAccents: false);
        Quest_Companion.Converters.ThemeHelper.ApplySystemAccentToApplication();

        SourceInitialized += (s, e) =>
        {
            Quest_Companion.Converters.ThemeHelper.ApplySystemAccentToApplication();
        };

        _viewModel = (MainViewModel)DataContext;
        _viewModel.RequestToggleWindow += OnRequestToggleWindow;
        _viewModel.RequestExit += OnRequestExit;
        _viewModel.RequestNotification += OnRequestNotification;

        // Load tray icons and listen for connection status changes
        try
        {
            _iconConnected = LoadAppIcon("Assets/tray_connected.ico");
            _iconDisconnected = LoadAppIcon("Assets/tray_disconnected.ico");
            if (_iconConnected == null) _iconConnected = LoadAppIcon("Assets/app.ico");

            UpdateTrayIcon(_viewModel.IsHeadsetConnected);

            _viewModel.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(MainViewModel.IsHeadsetConnected))
                {
                    Dispatcher.Invoke(() => UpdateTrayIcon(_viewModel.IsHeadsetConnected));
                }
            };
        }
        catch { }

        Loaded += (s, e) =>
        {
            UpdateTrayIcon(_viewModel.IsHeadsetConnected);
        };
    }

    private static System.Drawing.Icon? LoadAppIcon(string relativePath)
    {
        try
        {
            // 1. First try reading from embedded WPF resource (crucial for Single-File release!)
            string resourceUri = relativePath.StartsWith("/") ? relativePath : "/" + relativePath;
            var streamInfo = Application.GetResourceStream(new Uri("pack://application:,,,/" + relativePath.TrimStart('/'), UriKind.Absolute))
                          ?? Application.GetResourceStream(new Uri(resourceUri, UriKind.Relative));
            if (streamInfo?.Stream != null)
            {
                using var ms = new MemoryStream();
                streamInfo.Stream.CopyTo(ms);
                ms.Position = 0;
                return new System.Drawing.Icon(ms);
            }
        }
        catch { }

        try
        {
            // 2. Fallback to physical disk file next to executable without locking
            string diskPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, relativePath.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(diskPath))
            {
                byte[] bytes = File.ReadAllBytes(diskPath);
                using var ms = new MemoryStream(bytes);
                return new System.Drawing.Icon(ms);
            }
        }
        catch { }

        return null;
    }

    private void UpdateTrayIcon(bool isConnected)
    {
        try
        {
            var targetIcon = isConnected ? (_iconConnected ?? _iconDisconnected) : (_iconDisconnected ?? _iconConnected);
            if (targetIcon != null)
            {
                MyTaskbarIcon.Icon = targetIcon;
            }
        }
        catch { }
    }

    private void OnRequestNotification(string title, string message)
    {
        Dispatcher.Invoke(() =>
        {
            MyTaskbarIcon.ShowNotification(title, message, H.NotifyIcon.Core.NotificationIcon.Info);
        });
    }

    private void OnRequestToggleWindow()
    {
        Dispatcher.Invoke(() =>
        {
            if (IsVisible && WindowState != WindowState.Minimized)
            {
                Hide();
            }
            else
            {
                Show();
                WindowState = WindowState.Normal;
                Activate();
                Focus();
            }
        });
    }

    public void ShutdownServices()
    {
        try
        {
            MyTaskbarIcon.Dispose();
            _iconConnected?.Dispose();
            _iconDisconnected?.Dispose();
            _viewModel.Shutdown();
        }
        catch { }
    }

    private void OnRequestExit()
    {
        ShutdownServices();
        Application.Current.Shutdown();
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        // Ukryj zamiast zamykać aplikację (zamknięcie następuje tylko przez Tray -> Zakończ)
        e.Cancel = true;
        Hide();
        base.OnClosing(e);
    }

    private void TrayIcon_TrayLeftMouseDown(object sender, RoutedEventArgs e)
    {
        OnRequestToggleWindow();
    }

    private void OpenWindowMenuItem_Click(object sender, RoutedEventArgs e)
    {
        OnRequestToggleWindow();
    }

    private void ExitMenuItem_Click(object sender, RoutedEventArgs e)
    {
        OnRequestExit();
    }

    private void DropZone_DragOver(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            e.Effects = DragDropEffects.Copy;
        }
        else
        {
            e.Effects = DragDropEffects.None;
        }
        e.Handled = true;
    }

    private void DropZone_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            if (e.Data.GetData(DataFormats.FileDrop) is string[] files && files.Length > 0)
            {
                _viewModel.HandleDroppedItem(files[0]);
            }
        }
    }
}
