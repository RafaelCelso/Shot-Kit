using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using ShotKit.Native;
using ShotKit.Services;

namespace ShotKit.Views;

public enum CaptureMode { Edit, Ocr, Scrolling }

/// <summary>O que o usuário pediu ao fechar a sobreposição.</summary>
public enum OverlayOutcome { Cancelled, Copy, Save, Pin, Ocr, Scrolling }

public sealed class OverlayResult
{
    public OverlayOutcome Outcome { get; init; }
    public BitmapSource? Image { get; init; }

    /// <summary>Região selecionada em coordenadas absolutas de tela (pixels físicos).</summary>
    public Int32Rect ScreenRect { get; init; }
}

/// <summary>
/// Janela em tela cheia sobre todos os monitores. Mostra um instantâneo congelado da tela,
/// permite selecionar uma região (ou janela) e anotá-la.
/// Todas as coordenadas internas são pixels físicos relativos ao instantâneo.
/// </summary>
public partial class CaptureOverlay : Window
{
    private enum Tool { None, Rect, Ellipse, Line, Arrow, Pen, Highlighter, Text, Step, Mosaic, Blur }

    private enum DragKind { None, NewSelection, Move, Resize, Draw }

    private static readonly string[] Palette =
        ["#FFE53935", "#FFFB8C00", "#FFFDD835", "#FF43A047", "#FF1E88E5", "#FF8E24AA", "#FF212121", "#FFFFFFFF"];

    private readonly ScreenSnapshot _snap;
    private readonly CaptureMode _mode;
    private readonly AppSettings _settings;

    private Rect _selection = Rect.Empty;
    private bool _hasSelection;
    private Tool _tool = Tool.None;

    private DragKind _drag;
    private Point _dragStart;
    private Rect _dragStartSelection;
    private string _resizeHandle = "";

    // Anotação em andamento.
    private FrameworkElement? _current;
    private Polyline? _currentPolyline;
    private Point _drawStart;
    private TextBox? _editingText;
    private int _stepCounter = 1;

    // Pilhas de desfazer/refazer (apenas inclusões de anotações).
    private readonly Stack<UIElement> _undo = new();
    private readonly Stack<UIElement> _redo = new();

    public OverlayResult Result { get; private set; } = new() { Outcome = OverlayOutcome.Cancelled };

    public CaptureOverlay(ScreenSnapshot snapshot, CaptureMode mode, AppSettings settings)
    {
        InitializeComponent();
        _snap = snapshot;
        _mode = mode;
        _settings = settings;

        int w = snapshot.VirtualBounds.Width, h = snapshot.VirtualBounds.Height;
        Root.Width = w;
        Root.Height = h;
        BackgroundImage.Source = snapshot.Image;
        BackgroundImage.Width = w;
        BackgroundImage.Height = h;
        AnnotationLayer.Width = w;
        AnnotationLayer.Height = h;

        MagnifierView.Fill = new ImageBrush(snapshot.Image)
        {
            ViewboxUnits = BrushMappingMode.Absolute,
            Stretch = Stretch.Fill,
        };

        BuildStyleBar();
        WireToolbar();
        UpdateMask();

        SourceInitialized += (_, _) =>
        {
            WindowHelper.SuppressDpiResize(this);
            WindowHelper.SetBoundsPx(this, snapshot.VirtualBounds);
            ApplyDpiScale();
        };
        DpiChanged += (_, _) => ApplyDpiScale();
        Loaded += (_, _) =>
        {
            Activate();
            Focus();
            Keyboard.Focus(this);
            UpdateHover(Mouse.GetPosition(Root));
        };

        MouseLeftButtonDown += OnMouseDown;
        MouseMove += OnMouseMove;
        MouseLeftButtonUp += OnMouseUp;
        MouseRightButtonUp += OnRightClick;
        PreviewKeyDown += OnKeyDown;
        Deactivated += (_, _) => { if (IsVisible && _editingText == null) Activate(); };
    }

    // ------------------------------------------------------------------ DPI

    /// <summary>Contrapõe o DPI para que 1 unidade do Root seja 1 pixel físico.</summary>
    private void ApplyDpiScale()
    {
        double s = WindowHelper.DpiScale(this);
        Root.LayoutTransform = new ScaleTransform(1 / s, 1 / s);
    }

    // ------------------------------------------------------------------ Toolbar

    private void WireToolbar()
    {
        var tools = new (RadioButton Btn, Tool Tool)[]
        {
            (ToolRect, Tool.Rect), (ToolEllipse, Tool.Ellipse), (ToolLine, Tool.Line), (ToolArrow, Tool.Arrow),
            (ToolPen, Tool.Pen), (ToolHighlighter, Tool.Highlighter), (ToolText, Tool.Text), (ToolStep, Tool.Step),
            (ToolMosaic, Tool.Mosaic), (ToolBlur, Tool.Blur),
        };
        foreach (var (btn, tool) in tools)
        {
            btn.Checked += (_, _) => SetTool(tool);
            // Clicar de novo na ferramenta ativa desmarca (volta a mover/redimensionar).
            btn.PreviewMouseLeftButtonDown += (_, e) =>
            {
                if (btn.IsChecked == true)
                {
                    btn.IsChecked = false;
                    SetTool(Tool.None);
                    e.Handled = true;
                }
            };
        }

        BtnUndo.Click += (_, _) => Undo();
        BtnRedo.Click += (_, _) => Redo();
        BtnCancel.Click += (_, _) => Finish(OverlayOutcome.Cancelled);
        BtnCopy.Click += (_, _) => Finish(OverlayOutcome.Copy);
        BtnSave.Click += (_, _) => Finish(OverlayOutcome.Save);
        BtnPin.Click += (_, _) => Finish(OverlayOutcome.Pin);
        BtnOcr.Click += (_, _) => Finish(OverlayOutcome.Ocr);
        BtnScroll.Click += (_, _) => Finish(OverlayOutcome.Scrolling);

        // Cliques na barra não devem iniciar seleção/desenho.
        Toolbar.PreviewMouseLeftButtonDown += (_, e) => CommitText();
        Toolbar.MouseLeftButtonDown += (_, e) => e.Handled = true;
        StyleBar.MouseLeftButtonDown += (_, e) => e.Handled = true;

        UpdateUndoButtons();
    }

    private void BuildStyleBar()
    {
        foreach (string hex in Palette)
        {
            var brush = (SolidColorBrush)new BrushConverter().ConvertFromString(hex)!;
            var rb = new RadioButton
            {
                Style = (Style)FindResource("ColorSwatch"),
                Background = brush,
                ToolTip = hex,
                IsChecked = string.Equals(hex, _settings.Style.Color, StringComparison.OrdinalIgnoreCase),
            };
            System.Windows.Automation.AutomationProperties.SetName(rb, "Cor " + hex);
            rb.Checked += (_, _) => _settings.Style.Color = hex;
            ColorPanel.Children.Add(rb);
        }

        var t = _settings.Style.StrokeThickness;
        (t <= 2 ? SizeSmall : t >= 6 ? SizeLarge : SizeMedium).IsChecked = true;
        SizeSmall.Checked += (_, _) => SetSize(2, 16, 8);
        SizeMedium.Checked += (_, _) => SetSize(4, 22, 12);
        SizeLarge.Checked += (_, _) => SetSize(7, 32, 20);

        FillToggle.IsChecked = _settings.Style.Filled;
        UpdateFillToggle();
        FillToggle.Checked += (_, _) => { _settings.Style.Filled = true; UpdateFillToggle(); };
        FillToggle.Unchecked += (_, _) => { _settings.Style.Filled = false; UpdateFillToggle(); };
    }

    private void UpdateFillToggle()
    {
        FillToggle.Background = FillToggle.IsChecked == true ? (Brush)FindResource("ToolbarChecked") : Brushes.Transparent;
        FillToggle.Content = FillToggle.IsChecked == true ? "\uEA3B" : "\uE91F";
    }

    private void SetSize(double stroke, double font, int mosaic)
    {
        _settings.Style.StrokeThickness = stroke;
        _settings.Style.FontSize = font;
        _settings.Style.MosaicBlockSize = mosaic;
    }

    private void SetTool(Tool tool)
    {
        CommitText();
        _tool = tool;
        Cursor = tool == Tool.None ? Cursors.SizeAll : tool == Tool.Text ? Cursors.IBeam : Cursors.Cross;
        StyleBar.Visibility = tool is Tool.None ? Visibility.Collapsed : Visibility.Visible;
        HandleLayer.Visibility = tool == Tool.None && _undo.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        PositionToolbars();
    }

    private Brush CurrentBrush()
    {
        var b = (SolidColorBrush)new BrushConverter().ConvertFromString(_settings.Style.Color)!;
        b.Freeze();
        return b;
    }

    // ------------------------------------------------------------------ Mouse

    private void OnMouseDown(object sender, MouseButtonEventArgs e)
    {
        Point p = Clamp(e.GetPosition(Root));

        if (e.ClickCount == 2 && _hasSelection && _selection.Contains(p) && _tool == Tool.None)
        {
            Finish(OverlayOutcome.Copy);
            return;
        }

        CommitText();

        if (!_hasSelection)
        {
            _drag = DragKind.NewSelection;
            _dragStart = p;
            CaptureMouse();
            return;
        }

        if (_tool != Tool.None)
        {
            if (!_selection.Contains(p)) return;
            BeginDraw(p);
            return;
        }

        // Sem ferramenta: redimensionar pelas alças ou mover a seleção (só antes de anotar).
        if (_undo.Count > 0) return;
        string handle = HitHandle(p);
        if (handle != "")
        {
            _drag = DragKind.Resize;
            _resizeHandle = handle;
        }
        else if (_selection.Contains(p))
        {
            _drag = DragKind.Move;
        }
        else
        {
            // Clique fora: recomeça a seleção.
            _drag = DragKind.NewSelection;
            _hasSelection = false;
        }
        _dragStart = p;
        _dragStartSelection = _selection;
        CaptureMouse();
    }

    private void OnMouseMove(object sender, MouseEventArgs e)
    {
        Point p = Clamp(e.GetPosition(Root));

        switch (_drag)
        {
            case DragKind.NewSelection:
                if (Distance(p, _dragStart) > 3)
                {
                    HoverRect.Visibility = Visibility.Collapsed;
                    SetSelection(new Rect(_dragStart, p));
                }
                UpdateMagnifier(p);
                break;

            case DragKind.Move:
            {
                var v = p - _dragStart;
                var r = _dragStartSelection;
                double x = Math.Clamp(r.X + v.X, 0, Root.Width - r.Width);
                double y = Math.Clamp(r.Y + v.Y, 0, Root.Height - r.Height);
                SetSelection(new Rect(x, y, r.Width, r.Height));
                break;
            }

            case DragKind.Resize:
                SetSelection(ResizeRect(_dragStartSelection, _resizeHandle, p));
                break;

            case DragKind.Draw:
                UpdateDraw(p, Keyboard.Modifiers.HasFlag(ModifierKeys.Shift));
                break;

            default:
                if (!_hasSelection)
                {
                    UpdateHover(p);
                    UpdateMagnifier(p);
                }
                else if (_tool == Tool.None && _undo.Count == 0)
                {
                    Cursor = CursorForHandle(HitHandle(p)) ?? (_selection.Contains(p) ? Cursors.SizeAll : Cursors.Arrow);
                }
                break;
        }
    }

    private void OnMouseUp(object sender, MouseButtonEventArgs e)
    {
        Point p = Clamp(e.GetPosition(Root));
        var kind = _drag;
        _drag = DragKind.None;
        ReleaseMouseCapture();

        switch (kind)
        {
            case DragKind.NewSelection:
                if (Distance(p, _dragStart) <= 3)
                {
                    // Clique simples: seleciona a janela/monitor sob o cursor.
                    var hit = _snap.HitTest((int)p.X, (int)p.Y);
                    if (hit is { } r) SetSelection(new Rect(r.X, r.Y, r.Width, r.Height));
                    else return;
                }
                if (_selection.Width < 4 || _selection.Height < 4)
                {
                    ClearSelection();
                    return;
                }
                _hasSelection = true;
                Magnifier.Visibility = Visibility.Collapsed;
                HoverRect.Visibility = Visibility.Collapsed;

                if (_mode == CaptureMode.Ocr) { Finish(OverlayOutcome.Ocr); return; }
                if (_mode == CaptureMode.Scrolling) { Finish(OverlayOutcome.Scrolling); return; }

                ShowEditingChrome();
                break;

            case DragKind.Move:
            case DragKind.Resize:
                PositionToolbars();
                break;

            case DragKind.Draw:
                EndDraw(p);
                break;
        }
    }

    private void OnRightClick(object sender, MouseButtonEventArgs e)
    {
        if (_editingText != null) return;
        if (_hasSelection && _undo.Count == 0)
        {
            // Volta à escolha da região.
            ClearSelection();
            UpdateHover(Clamp(e.GetPosition(Root)));
        }
        else if (!_hasSelection)
        {
            Finish(OverlayOutcome.Cancelled);
        }
    }

    // ------------------------------------------------------------------ Teclado

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (_editingText != null)
        {
            if (e.Key == Key.Escape || (e.Key == Key.Enter && Keyboard.Modifiers.HasFlag(ModifierKeys.Control)))
            {
                CommitText();
                e.Handled = true;
            }
            return; // demais teclas vão para a caixa de texto
        }

        bool ctrl = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
        switch (e.Key)
        {
            case Key.Escape:
                Finish(OverlayOutcome.Cancelled);
                break;
            case Key.Enter when _hasSelection:
                Finish(OverlayOutcome.Copy);
                break;
            case Key.C when ctrl && _hasSelection:
                Finish(OverlayOutcome.Copy);
                break;
            case Key.S when ctrl && _hasSelection:
                Finish(OverlayOutcome.Save);
                break;
            case Key.Z when ctrl:
                Undo();
                break;
            case Key.Y when ctrl:
                Redo();
                break;
            case Key.F3 when _hasSelection:
                Finish(OverlayOutcome.Pin);
                break;
            case Key.A when ctrl:
                // Seleciona o monitor sob o cursor.
                var p = Mouse.GetPosition(Root);
                var mon = ScreenCapture.GetMonitorRects()
                    .Select(m => new Rect(m.X - _snap.VirtualBounds.X, m.Y - _snap.VirtualBounds.Y, m.Width, m.Height))
                    .FirstOrDefault(r => r.Contains(p), new Rect(0, 0, Root.Width, Root.Height));
                if (_undo.Count == 0)
                {
                    SetSelection(mon);
                    _hasSelection = true;
                    Magnifier.Visibility = HoverRect.Visibility = Visibility.Collapsed;
                    ShowEditingChrome();
                }
                break;
            case Key.Left or Key.Right or Key.Up or Key.Down when _hasSelection && _undo.Count == 0:
                NudgeSelection(e.Key, Keyboard.Modifiers.HasFlag(ModifierKeys.Shift));
                break;
            default:
                if (!_hasSelection || ctrl) return;
                var tool = e.Key switch
                {
                    Key.R => ToolRect, Key.E => ToolEllipse, Key.L => ToolLine, Key.A => ToolArrow,
                    Key.P => ToolPen, Key.H => ToolHighlighter, Key.T => ToolText, Key.N => ToolStep,
                    Key.M => ToolMosaic, Key.B => ToolBlur, _ => null,
                };
                if (tool == null) return;
                tool.IsChecked = true;
                break;
        }
        e.Handled = true;
    }

    /// <summary>Setas movem a seleção 1 px; Shift+setas aumentam/diminuem.</summary>
    private void NudgeSelection(Key key, bool resize)
    {
        var r = _selection;
        int dx = key == Key.Left ? -1 : key == Key.Right ? 1 : 0;
        int dy = key == Key.Up ? -1 : key == Key.Down ? 1 : 0;
        r = resize
            ? new Rect(r.X, r.Y, Math.Max(4, r.Width + dx), Math.Max(4, r.Height + dy))
            : new Rect(Math.Clamp(r.X + dx, 0, Root.Width - r.Width), Math.Clamp(r.Y + dy, 0, Root.Height - r.Height), r.Width, r.Height);
        SetSelection(r);
        PositionToolbars();
    }

    // ------------------------------------------------------------------ Seleção

    private void SetSelection(Rect r)
    {
        r = Rect.Intersect(new Rect(Math.Round(r.X), Math.Round(r.Y), Math.Round(r.Width), Math.Round(r.Height)),
            new Rect(0, 0, Root.Width, Root.Height));
        if (r.IsEmpty) return;
        _selection = r;

        Canvas.SetLeft(SelectionBorder, r.X - 1);
        Canvas.SetTop(SelectionBorder, r.Y - 1);
        SelectionBorder.Width = r.Width + 2;
        SelectionBorder.Height = r.Height + 2;
        SelectionBorder.Visibility = Visibility.Visible;

        AnnotationLayer.Clip = new RectangleGeometry(r);

        SizeText.Text = $"{(int)r.Width} × {(int)r.Height}";
        SizeLabel.Visibility = Visibility.Visible;
        SizeLabel.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        double labelY = r.Y - SizeLabel.DesiredSize.Height - 6;
        Canvas.SetLeft(SizeLabel, r.X);
        Canvas.SetTop(SizeLabel, labelY < 0 ? r.Y + 6 : labelY);

        UpdateMask();
        LayoutHandles();
    }

    private void ClearSelection()
    {
        _hasSelection = false;
        _selection = Rect.Empty;
        SelectionBorder.Visibility = SizeLabel.Visibility = Toolbar.Visibility = StyleBar.Visibility = Visibility.Collapsed;
        HandleLayer.Visibility = Visibility.Collapsed;
        AnnotationLayer.Clip = null;
        Cursor = Cursors.Cross;
        UpdateMask();
    }

    private void UpdateMask()
    {
        var full = new RectangleGeometry(new Rect(0, 0, Root.Width, Root.Height));
        if (_selection.IsEmpty)
        {
            DimMask.Data = full;
            return;
        }
        DimMask.Data = new CombinedGeometry(GeometryCombineMode.Exclude, full, new RectangleGeometry(_selection));
    }

    private void ShowEditingChrome()
    {
        Toolbar.Visibility = Visibility.Visible;
        HandleLayer.Visibility = Visibility.Visible;
        Cursor = Cursors.SizeAll;
        PositionToolbars();
    }

    /// <summary>Coloca a barra abaixo da seleção (ou acima / dentro, se não couber).</summary>
    private void PositionToolbars()
    {
        if (!_hasSelection || Toolbar.Visibility != Visibility.Visible) return;

        Toolbar.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        StyleBar.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var tb = Toolbar.DesiredSize;
        var sb = StyleBar.Visibility == Visibility.Visible ? StyleBar.DesiredSize : new Size(0, 0);
        double gap = 8;
        double needed = tb.Height + (sb.Height > 0 ? sb.Height + 4 : 0) + gap;

        // Limita ao monitor que contém a seleção, para a barra não cair entre monitores.
        var center = new Point(_selection.X + _selection.Width / 2, _selection.Y + _selection.Height / 2);
        var mon = MonitorRectFor(center);

        double x = Math.Clamp(_selection.Right - tb.Width, mon.Left + 4, Math.Max(mon.Left + 4, mon.Right - tb.Width - 4));
        double y;
        bool below = _selection.Bottom + needed <= mon.Bottom;
        bool above = _selection.Top - needed >= mon.Top;
        if (below) y = _selection.Bottom + gap;
        else if (above) y = _selection.Top - needed;
        else y = _selection.Bottom - needed; // dentro da seleção

        Canvas.SetLeft(Toolbar, x);
        Canvas.SetTop(Toolbar, y);

        if (sb.Height > 0)
        {
            double sx = Math.Clamp(_selection.Right - sb.Width, mon.Left + 4, Math.Max(mon.Left + 4, mon.Right - sb.Width - 4));
            double sy = below || !above ? y + tb.Height + 4 : y + tb.Height + 4;
            Canvas.SetLeft(StyleBar, sx);
            Canvas.SetTop(StyleBar, sy);
        }
    }

    private Rect MonitorRectFor(Point p)
    {
        foreach (var m in ScreenCapture.GetMonitorRects())
        {
            var r = new Rect(m.X - _snap.VirtualBounds.X, m.Y - _snap.VirtualBounds.Y, m.Width, m.Height);
            if (r.Contains(p)) return r;
        }
        return new Rect(0, 0, Root.Width, Root.Height);
    }

    private void UpdateHover(Point p)
    {
        if (_snap.HitTest((int)p.X, (int)p.Y) is { } r)
        {
            Canvas.SetLeft(HoverRect, r.X);
            Canvas.SetTop(HoverRect, r.Y);
            HoverRect.Width = r.Width;
            HoverRect.Height = r.Height;
            HoverRect.Visibility = Visibility.Visible;
        }
        else
        {
            HoverRect.Visibility = Visibility.Collapsed;
        }
    }

    private void UpdateMagnifier(Point p)
    {
        const double zoomSrc = 15; // pixels de origem mostrados na lupa
        ((ImageBrush)MagnifierView.Fill).Viewbox = new Rect(Math.Floor(p.X) - zoomSrc / 2 + 0.5, Math.Floor(p.Y) - zoomSrc / 2 + 0.5, zoomSrc, zoomSrc);
        int ax = (int)p.X + _snap.VirtualBounds.X, ay = (int)p.Y + _snap.VirtualBounds.Y;
        MagnifierText.Text = $"X:{ax} Y:{ay}\n{_snap.Buffer.HexAt((int)p.X, (int)p.Y)}";
        Magnifier.Visibility = Visibility.Visible;

        Magnifier.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var size = Magnifier.DesiredSize;
        var mon = MonitorRectFor(p);
        double x = p.X + 20, y = p.Y + 20;
        if (x + size.Width > mon.Right) x = p.X - 20 - size.Width;
        if (y + size.Height > mon.Bottom) y = p.Y - 20 - size.Height;
        Canvas.SetLeft(Magnifier, x);
        Canvas.SetTop(Magnifier, y);
    }

    // ------------------------------------------------------------------ Alças de redimensionamento

    private static readonly string[] Handles = ["nw", "n", "ne", "e", "se", "s", "sw", "w"];

    private void LayoutHandles()
    {
        if (HandleLayer.Children.Count == 0)
        {
            foreach (var _ in Handles)
            {
                HandleLayer.Children.Add(new Rectangle
                {
                    Width = 9, Height = 9, Fill = Brushes.White,
                    Stroke = (Brush)FindResource("Accent"), StrokeThickness = 1.5, IsHitTestVisible = false,
                });
            }
        }
        for (int i = 0; i < Handles.Length; i++)
        {
            var pt = HandlePoint(_selection, Handles[i]);
            Canvas.SetLeft(HandleLayer.Children[i], pt.X - 4.5);
            Canvas.SetTop(HandleLayer.Children[i], pt.Y - 4.5);
        }
    }

    private static Point HandlePoint(Rect r, string h) => h switch
    {
        "nw" => r.TopLeft,
        "n" => new Point(r.X + r.Width / 2, r.Y),
        "ne" => r.TopRight,
        "e" => new Point(r.Right, r.Y + r.Height / 2),
        "se" => r.BottomRight,
        "s" => new Point(r.X + r.Width / 2, r.Bottom),
        "sw" => r.BottomLeft,
        _ => new Point(r.X, r.Y + r.Height / 2),
    };

    private string HitHandle(Point p)
    {
        if (!_hasSelection || HandleLayer.Visibility != Visibility.Visible) return "";
        foreach (string h in Handles)
            if (Distance(HandlePoint(_selection, h), p) <= 8) return h;
        return "";
    }

    private static Cursor? CursorForHandle(string h) => h switch
    {
        "nw" or "se" => Cursors.SizeNWSE,
        "ne" or "sw" => Cursors.SizeNESW,
        "n" or "s" => Cursors.SizeNS,
        "e" or "w" => Cursors.SizeWE,
        _ => null,
    };

    private static Rect ResizeRect(Rect r, string h, Point p)
    {
        double l = r.Left, t = r.Top, rr = r.Right, b = r.Bottom;
        if (h.Contains('w')) l = p.X;
        if (h.Contains('e')) rr = p.X;
        if (h.Contains('n')) t = p.Y;
        if (h.Contains('s')) b = p.Y;
        return new Rect(new Point(l, t), new Point(rr, b));
    }

    // ------------------------------------------------------------------ Desenho

    private void BeginDraw(Point p)
    {
        var style = _settings.Style;
        Brush brush = CurrentBrush();
        _drawStart = p;
        _drag = DragKind.Draw;
        _current = null;
        _currentPolyline = null;

        switch (_tool)
        {
            case Tool.Rect:
                _current = new Rectangle
                {
                    Stroke = brush, StrokeThickness = style.StrokeThickness,
                    Fill = style.Filled ? brush : null, RadiusX = 2, RadiusY = 2,
                };
                break;
            case Tool.Ellipse:
                _current = new Ellipse { Stroke = brush, StrokeThickness = style.StrokeThickness, Fill = style.Filled ? brush : null };
                break;
            case Tool.Line:
                _current = new Line
                {
                    Stroke = brush, StrokeThickness = style.StrokeThickness,
                    StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round,
                    X1 = p.X, Y1 = p.Y, X2 = p.X, Y2 = p.Y,
                };
                break;
            case Tool.Arrow:
                _current = new Path { Fill = brush, Stroke = brush, StrokeThickness = style.StrokeThickness, StrokeLineJoin = PenLineJoin.Round };
                break;
            case Tool.Pen:
            case Tool.Highlighter:
                bool hl = _tool == Tool.Highlighter;
                _currentPolyline = new Polyline
                {
                    Stroke = brush,
                    StrokeThickness = hl ? Math.Max(14, style.StrokeThickness * 4) : style.StrokeThickness,
                    StrokeLineJoin = PenLineJoin.Round,
                    StrokeStartLineCap = hl ? PenLineCap.Flat : PenLineCap.Round,
                    StrokeEndLineCap = hl ? PenLineCap.Flat : PenLineCap.Round,
                    Opacity = hl ? 0.4 : 1,
                };
                _currentPolyline.Points.Add(p);
                _current = _currentPolyline;
                break;
            case Tool.Mosaic:
            case Tool.Blur:
                // Pré-visualização tracejada enquanto arrasta.
                _current = new Rectangle { Stroke = Brushes.White, StrokeThickness = 1, StrokeDashArray = [4, 3], Fill = new SolidColorBrush(Color.FromArgb(40, 255, 255, 255)) };
                break;
            case Tool.Text:
                _drag = DragKind.None;
                BeginText(p);
                return;
            case Tool.Step:
                _drag = DragKind.None;
                AddStep(p);
                return;
        }

        if (_current != null)
        {
            AnnotationLayer.Children.Add(_current);
            UpdateDraw(p, false);
        }
        CaptureMouse();
    }

    private void UpdateDraw(Point p, bool constrain)
    {
        if (_current == null) return;
        p = ClampToSelection(p);
        Point s = _drawStart;

        switch (_current)
        {
            case Polyline pl:
                pl.Points.Add(p);
                break;
            case Line line:
                if (constrain) p = Snap45(s, p);
                line.X2 = p.X;
                line.Y2 = p.Y;
                break;
            case Path path:
                if (constrain) p = Snap45(s, p);
                path.Data = ArrowGeometry(s, p, path.StrokeThickness);
                break;
            default:
                if (constrain && _tool is Tool.Rect or Tool.Ellipse)
                {
                    double side = Math.Max(Math.Abs(p.X - s.X), Math.Abs(p.Y - s.Y));
                    p = new Point(s.X + Math.Sign(p.X - s.X) * side, s.Y + Math.Sign(p.Y - s.Y) * side);
                }
                var r = new Rect(s, p);
                Canvas.SetLeft(_current, r.X);
                Canvas.SetTop(_current, r.Y);
                _current.Width = r.Width;
                _current.Height = r.Height;
                break;
        }
    }

    private void EndDraw(Point p)
    {
        if (_current == null) return;
        var element = _current;
        _current = null;
        _currentPolyline = null;

        bool tiny = Distance(_drawStart, ClampToSelection(p)) < 3;

        if (_tool is Tool.Mosaic or Tool.Blur)
        {
            AnnotationLayer.Children.Remove(element);
            var r = new Rect(Canvas.GetLeft(element), Canvas.GetTop(element), element.Width, element.Height);
            if (r.Width < 3 || r.Height < 3) return;
            var img = CreateObscured(r, _tool == Tool.Blur);
            if (img != null) Push(img);
            return;
        }

        if (tiny && element is not Polyline)
        {
            AnnotationLayer.Children.Remove(element);
            return;
        }

        AnnotationLayer.Children.Remove(element);
        Push(element);
    }

    /// <summary>Gera uma imagem pixelada/desfocada a partir do instantâneo original.</summary>
    private Image? CreateObscured(Rect r, bool blur)
    {
        var region = new Int32Rect((int)r.X, (int)r.Y, (int)Math.Ceiling(r.Width), (int)Math.Ceiling(r.Height));
        var crop = ImageOps.Crop(_snap.Buffer, region);
        if (crop.Width == 0 || crop.Height == 0) return null;
        int block = _settings.Style.MosaicBlockSize;
        var processed = blur ? ImageOps.Blur(crop, Math.Max(3, block / 2)) : ImageOps.Pixelate(crop, block);
        var image = new Image
        {
            Source = processed.ToBitmapSource(),
            Width = crop.Width,
            Height = crop.Height,
            Stretch = Stretch.Fill,
        };
        RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.NearestNeighbor);
        Canvas.SetLeft(image, region.X);
        Canvas.SetTop(image, region.Y);
        return image;
    }

    private static Geometry ArrowGeometry(Point a, Point b, double thickness)
    {
        var v = b - a;
        double len = v.Length;
        if (len < 1) return Geometry.Empty;
        v /= len;
        var n = new Vector(-v.Y, v.X);
        double headLen = Math.Min(len, Math.Max(14, thickness * 4.5));
        double headWidth = headLen * 0.55;
        Point baseCenter = b - v * headLen;

        var g = new StreamGeometry();
        using (var ctx = g.Open())
        {
            // Haste (só traço).
            ctx.BeginFigure(a, false, false);
            ctx.LineTo(baseCenter + v * (headLen * 0.3), true, true);
            // Ponta (preenchida).
            ctx.BeginFigure(b, true, true);
            ctx.LineTo(baseCenter + n * headWidth, true, true);
            ctx.LineTo(baseCenter - n * headWidth, true, true);
        }
        g.Freeze();
        return g;
    }

    private void BeginText(Point p)
    {
        var tb = new TextBox
        {
            Background = Brushes.Transparent,
            Foreground = CurrentBrush(),
            BorderBrush = new SolidColorBrush(Color.FromArgb(180, 255, 255, 255)),
            BorderThickness = new Thickness(1),
            FontSize = _settings.Style.FontSize,
            FontFamily = new FontFamily("Segoe UI"),
            FontWeight = FontWeights.SemiBold,
            AcceptsReturn = true,
            MinWidth = 40,
            Padding = new Thickness(2),
            CaretBrush = CurrentBrush(),
        };
        Canvas.SetLeft(tb, p.X);
        Canvas.SetTop(tb, p.Y - _settings.Style.FontSize * 0.7);
        AnnotationLayer.IsHitTestVisible = true;
        AnnotationLayer.Children.Add(tb);
        _editingText = tb;
        tb.Loaded += (_, _) => { tb.Focus(); Keyboard.Focus(tb); };
    }

    /// <summary>Converte a caixa de texto em edição num TextBlock definitivo.</summary>
    private void CommitText()
    {
        if (_editingText == null) return;
        var tb = _editingText;
        _editingText = null;
        AnnotationLayer.Children.Remove(tb);
        AnnotationLayer.IsHitTestVisible = false;
        Keyboard.Focus(this);

        if (string.IsNullOrWhiteSpace(tb.Text)) return;
        var block = new TextBlock
        {
            Text = tb.Text,
            Foreground = tb.Foreground,
            FontSize = tb.FontSize,
            FontFamily = tb.FontFamily,
            FontWeight = tb.FontWeight,
            Padding = new Thickness(tb.Padding.Left + 1 + 2, tb.Padding.Top + 1, 0, 0), // compensa borda e margem interna do TextBox
        };
        Canvas.SetLeft(block, Canvas.GetLeft(tb));
        Canvas.SetTop(block, Canvas.GetTop(tb));
        Push(block);
    }

    private void AddStep(Point p)
    {
        double size = Math.Max(24, _settings.Style.FontSize * 1.4);
        var grid = new Grid { Width = size, Height = size };
        grid.Children.Add(new Ellipse { Fill = CurrentBrush(), Stroke = Brushes.White, StrokeThickness = 2 });
        grid.Children.Add(new TextBlock
        {
            Text = _stepCounter.ToString(),
            Foreground = IsLight(_settings.Style.Color) ? Brushes.Black : Brushes.White,
            FontWeight = FontWeights.Bold,
            FontFamily = new FontFamily("Segoe UI"),
            FontSize = size * 0.5,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        });
        Canvas.SetLeft(grid, p.X - size / 2);
        Canvas.SetTop(grid, p.Y - size / 2);
        _stepCounter++;
        Push(grid);
    }

    private static bool IsLight(string hex)
    {
        var c = (Color)ColorConverter.ConvertFromString(hex);
        return 0.299 * c.R + 0.587 * c.G + 0.114 * c.B > 170;
    }

    // ------------------------------------------------------------------ Desfazer / refazer

    private void Push(UIElement element)
    {
        AnnotationLayer.Children.Add(element);
        _undo.Push(element);
        _redo.Clear();
        AfterHistoryChange();
    }

    private void Undo()
    {
        CommitText();
        if (_undo.Count == 0) return;
        var e = _undo.Pop();
        AnnotationLayer.Children.Remove(e);
        _redo.Push(e);
        if (e is Grid) _stepCounter = Math.Max(1, _stepCounter - 1);
        AfterHistoryChange();
    }

    private void Redo()
    {
        if (_redo.Count == 0) return;
        var e = _redo.Pop();
        AnnotationLayer.Children.Add(e);
        _undo.Push(e);
        if (e is Grid) _stepCounter++;
        AfterHistoryChange();
    }

    private void AfterHistoryChange()
    {
        UpdateUndoButtons();
        HandleLayer.Visibility = _hasSelection && _tool == Tool.None && _undo.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void UpdateUndoButtons()
    {
        BtnUndo.IsEnabled = _undo.Count > 0;
        BtnRedo.IsEnabled = _redo.Count > 0;
    }

    // ------------------------------------------------------------------ Finalização

    private void Finish(OverlayOutcome outcome)
    {
        CommitText();
        if (outcome != OverlayOutcome.Cancelled && !_hasSelection) return;

        var abs = new Int32Rect(
            (int)_selection.X + _snap.VirtualBounds.X,
            (int)_selection.Y + _snap.VirtualBounds.Y,
            (int)_selection.Width,
            (int)_selection.Height);

        Result = outcome switch
        {
            OverlayOutcome.Cancelled => new OverlayResult { Outcome = outcome },
            // Rolagem captura a tela ao vivo; não precisa compor a imagem.
            OverlayOutcome.Scrolling => new OverlayResult { Outcome = outcome, ScreenRect = abs },
            _ => new OverlayResult { Outcome = outcome, Image = Compose(), ScreenRect = abs },
        };
        Close();
    }

    /// <summary>Recorta o instantâneo e desenha as anotações por cima (1:1 em pixels).</summary>
    private BitmapSource Compose()
    {
        var r = _selection;
        int w = (int)r.Width, h = (int)r.Height;
        var crop = new CroppedBitmap(_snap.Image, new Int32Rect((int)r.X, (int)r.Y, w, h));

        if (AnnotationLayer.Children.Count == 0)
        {
            crop.Freeze();
            return crop;
        }

        var dv = new DrawingVisual();
        using (var dc = dv.RenderOpen())
        {
            dc.DrawImage(crop, new Rect(0, 0, w, h));
            var brush = new VisualBrush(AnnotationLayer)
            {
                ViewboxUnits = BrushMappingMode.Absolute,
                Viewbox = r,
                ViewportUnits = BrushMappingMode.Absolute,
                Viewport = new Rect(0, 0, w, h),
                Stretch = Stretch.Fill,
            };
            // Remove o recorte temporariamente: VisualBrush já limita à Viewbox.
            dc.DrawRectangle(brush, null, new Rect(0, 0, w, h));
        }

        var clip = AnnotationLayer.Clip;
        AnnotationLayer.Clip = null;
        var rtb = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(dv);
        AnnotationLayer.Clip = clip;

        // Remove o canal alfa (fundo opaco) para PNG/JPG consistentes.
        var opaque = new FormatConvertedBitmap(rtb, PixelFormats.Bgr32, null, 0);
        opaque.Freeze();
        return opaque;
    }

    // ------------------------------------------------------------------ Utilitários

    private Point Clamp(Point p) => new(Math.Clamp(p.X, 0, Root.Width - 1), Math.Clamp(p.Y, 0, Root.Height - 1));

    private Point ClampToSelection(Point p) =>
        new(Math.Clamp(p.X, _selection.Left, _selection.Right), Math.Clamp(p.Y, _selection.Top, _selection.Bottom));

    private static double Distance(Point a, Point b) => (a - b).Length;

    private static Point Snap45(Point s, Point p)
    {
        var v = p - s;
        double angle = Math.Round(Math.Atan2(v.Y, v.X) / (Math.PI / 4)) * (Math.PI / 4);
        double len = v.Length;
        return new Point(s.X + Math.Cos(angle) * len, s.Y + Math.Sin(angle) * len);
    }
}
