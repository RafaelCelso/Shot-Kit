using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using ShotKit.Native;

namespace ShotKit.Views;

/// <summary>Moldura tracejada ao redor da região durante a captura com rolagem. Ignora cliques e capturas.</summary>
internal sealed class ScrollFrameWindow : Window
{
    private const int Pad = 4;

    public ScrollFrameWindow(Int32Rect region)
    {
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        Topmost = true;
        ResizeMode = ResizeMode.NoResize;
        ShowActivated = false;
        Title = "ShotKit - Região de rolagem";

        Content = new Rectangle
        {
            Stroke = new SolidColorBrush(Color.FromRgb(0xE5, 0x39, 0x35)),
            StrokeThickness = 2,
            StrokeDashArray = [4, 2],
            Margin = new Thickness(1),
        };

        SourceInitialized += (_, _) =>
        {
            WindowHelper.MakeClickThrough(this);
            WindowHelper.ExcludeFromCapture(this);
            WindowHelper.SuppressDpiResize(this);
            WindowHelper.SetBoundsPx(this, new Int32Rect(region.X - Pad, region.Y - Pad, region.Width + Pad * 2, region.Height + Pad * 2));
        };
    }
}

/// <summary>Painel com progresso e botões Concluir/Cancelar, posicionado fora da região rolada.</summary>
internal sealed class ScrollControlPanel : Window
{
    private readonly TextBlock _status;

    public event Action? DoneClicked;
    public event Action? CancelClicked;

    public ScrollControlPanel(Int32Rect region)
    {
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        Topmost = true;
        ShowActivated = false;
        SizeToContent = SizeToContent.WidthAndHeight;
        Background = (Brush)Application.Current.FindResource("ToolbarBackground");
        Title = "ShotKit - Captura com rolagem";
        FontFamily = new FontFamily("Segoe UI");

        _status = new TextBlock
        {
            Foreground = Brushes.White,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(4, 0, 12, 0),
            MinWidth = 210,
            Text = "Preparando...",
        };
        var done = new Button { Content = "Concluir", Style = (Style)Application.Current.FindResource("PrimaryButton") };
        var cancel = new Button { Content = "Cancelar", Margin = new Thickness(6, 0, 0, 0), Padding = new Thickness(12, 5, 12, 5) };
        done.Click += (_, _) => DoneClicked?.Invoke();
        cancel.Click += (_, _) => CancelClicked?.Invoke();

        var panel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(10, 8, 10, 8) };
        panel.Children.Add(_status);
        panel.Children.Add(done);
        panel.Children.Add(cancel);
        Content = panel;

        SourceInitialized += (_, _) => WindowHelper.ExcludeFromCapture(this);
        Loaded += (_, _) => PlaceOutside(region);
    }

    public string Status { set => _status.Text = value; }

    /// <summary>Abaixo da região; se não couber, acima; senão, à direita; por último, no canto do monitor.</summary>
    private void PlaceOutside(Int32Rect region)
    {
        var size = WindowHelper.GetBoundsPx(this);
        var mon = WindowHelper.WorkAreaAt(region.X + region.Width / 2, region.Y + region.Height / 2);
        int gap = 10;
        int x = Math.Clamp(region.X + region.Width - size.Width, mon.X, mon.X + mon.Width - size.Width);
        int y;
        if (region.Y + region.Height + gap + size.Height <= mon.Y + mon.Height) y = region.Y + region.Height + gap;
        else if (region.Y - gap - size.Height >= mon.Y) y = region.Y - gap - size.Height;
        else if (region.X + region.Width + gap + size.Width <= mon.X + mon.Width)
        {
            x = region.X + region.Width + gap;
            y = region.Y;
        }
        else
        {
            x = mon.X + mon.Width - size.Width - gap;
            y = mon.Y + gap;
        }
        WindowHelper.SetBoundsPx(this, new Int32Rect(x, y, size.Width, size.Height));
    }
}
