using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using ShotKit.Native;
using ShotKit.Services;

namespace ShotKit.Views;

/// <summary>
/// Imagem "fixada" flutuando sobre as outras janelas.
/// Arraste para mover, roda do mouse para zoom, duplo clique ou Esc para fechar.
/// </summary>
public partial class PinWindow : Window
{
    private readonly BitmapSource _image;
    private readonly AppSettings _settings;
    private double _zoom = 1.0;
    private readonly DispatcherTimer _badgeTimer = new() { Interval = TimeSpan.FromSeconds(1.2) };

    public PinWindow(BitmapSource image, Int32Rect screenRect, AppSettings settings)
    {
        InitializeComponent();
        _image = image;
        _settings = settings;
        PinnedImage.Source = image;
        Icon = AppIcon.WindowIcon;

        SourceInitialized += (_, _) =>
        {
            WindowHelper.SuppressDpiResize(this);
            // A borda de 1 px fica do lado de fora, para a imagem continuar exatamente onde estava.
            WindowHelper.SetBoundsPx(this, new Int32Rect(screenRect.X - 1, screenRect.Y - 1,
                image.PixelWidth + 2, image.PixelHeight + 2));
            FixImageMargin();
        };
        DpiChanged += (_, _) => FixImageMargin();

        MouseLeftButtonDown += (_, e) =>
        {
            if (e.ClickCount == 2) Close();
            else DragMove();
        };
        MouseWheel += OnWheel;
        KeyDown += OnKey;

        _badgeTimer.Tick += (_, _) => { ZoomBadge.Visibility = Visibility.Collapsed; _badgeTimer.Stop(); };

        MenuCopy.Click += (_, _) => ImageExporter.CopyToClipboard(_image);
        MenuSave.Click += (_, _) => SaveAs();
        MenuOcr.Click += (_, _) => new OcrWindow(_image, _settings).Show();
        MenuZoomReset.Click += (_, _) => SetZoom(1.0);
        MenuClose.Click += (_, _) => Close();
        foreach (var item in new[] { Opacity100, Opacity80, Opacity60, Opacity40 })
            item.Click += (s, _) => Opacity = double.Parse((string)((MenuItem)s).Tag, System.Globalization.CultureInfo.InvariantCulture);
    }

    /// <summary>Mantém a borda com 1 pixel físico em qualquer DPI.</summary>
    private void FixImageMargin()
    {
        double m = 1 / WindowHelper.DpiScale(this);
        PinnedImage.Margin = new Thickness(m);
    }

    private void OnWheel(object sender, MouseWheelEventArgs e)
    {
        double factor = e.Delta > 0 ? 1.1 : 1 / 1.1;
        SetZoom(Math.Clamp(_zoom * factor, 0.1, 8));
    }

    private void SetZoom(double zoom)
    {
        _zoom = zoom;
        var b = WindowHelper.GetBoundsPx(this);
        int w = (int)Math.Round(_image.PixelWidth * zoom) + 2;
        int h = (int)Math.Round(_image.PixelHeight * zoom) + 2;
        // Zoom em torno do centro atual.
        int x = b.X + (b.Width - w) / 2;
        int y = b.Y + (b.Height - h) / 2;
        WindowHelper.SetBoundsPx(this, new Int32Rect(x, y, Math.Max(w, 12), Math.Max(h, 12)));

        ZoomText.Text = $"{zoom * 100:0}%";
        ZoomBadge.Visibility = Visibility.Visible;
        _badgeTimer.Stop();
        _badgeTimer.Start();
    }

    private void OnKey(object sender, KeyEventArgs e)
    {
        bool ctrl = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
        if (e.Key == Key.Escape) Close();
        else if (ctrl && e.Key == Key.C) ImageExporter.CopyToClipboard(_image);
        else if (ctrl && e.Key == Key.S) SaveAs();
        else if (ctrl && (e.Key == Key.D0 || e.Key == Key.NumPad0)) SetZoom(1.0);
        else return;
        e.Handled = true;
    }

    private void SaveAs()
    {
        try
        {
            ImageExporter.SaveAs(_image, _settings, this);
        }
        catch (Exception ex)
        {
            Log.Error("Falha ao salvar imagem fixada.", ex);
            MessageBox.Show(this, "Não foi possível salvar: " + ex.Message, "ShotKit", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}
