using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform;
using Avalonia.Threading;
using Avalonia;
using Avalonia.Media;
using Avalonia.VisualTree;
using Avalonia.Visuals;
using Lipi.ViewModels;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;

namespace Lipi.Views;

public partial class MainWindow : Window
{
    private const int BottomScreenMarginPx = 50;
    private MainViewModel? _vm;
    private ScrollViewer? _bubbleScrollViewer;
    private Border? _headerPanel;
    private Grid? _headerDragArea;
    private Border? _statusRow;
    private bool _lastIsSubtitleMode;
    private bool _isApplyingWindowLayout;
    private (double Width, double Height)? _normalWindowSize;
    private (double Width, double Height)? _subtitleWindowSize;

    public MainWindow()
    {
        InitializeComponent();
        _bubbleScrollViewer = this.FindControl<ScrollViewer>("BubbleScrollViewer");
        _headerPanel = this.FindControl<Border>("HeaderPanel");
        _headerDragArea = this.FindControl<Grid>("HeaderDragArea");
        _statusRow = this.FindControl<Border>("StatusRow");

        Opened += (_, _) =>
        {
            AttachViewModelHandlers();
            RefreshWindowLayout();
        };

        DataContextChanged += (_, _) =>
        {
            AttachViewModelHandlers();
            RefreshWindowLayout();
        };

        SizeChanged += OnWindowSizeChanged;
        PointerMoved += OnWindowPointerMoved;
        PointerExited += OnWindowPointerExited;

        Closed += (_, _) =>
        {
            if (_vm != null)
            {
                _vm.PropertyChanged -= OnViewModelPropertyChanged;
                _vm.Bubbles.CollectionChanged -= OnBubblesCollectionChanged;
            }

            if (DataContext is MainViewModel vm)
                vm.Dispose();
        };
    }

    private void DragArea_OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.Source is Visual sourceVisual && sourceVisual.GetSelfAndVisualAncestors().OfType<Button>().Any())
            return;

        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            BeginMoveDrag(e);
            e.Handled = true;
        }
    }

    private void CloseButton_OnClick(object? sender, RoutedEventArgs e)
    {
        Close();
    }

    private void MinimizeButton_OnClick(object? sender, RoutedEventArgs e)
    {
        WindowState = WindowState.Minimized;
    }

    private void SubtitleNarrowButton_OnClick(object? sender, RoutedEventArgs e)
        => AdjustSubtitleWidth(-0.05);

    private void SubtitleWideButton_OnClick(object? sender, RoutedEventArgs e)
        => AdjustSubtitleWidth(+0.05);

    private void AdjustSubtitleWidth(double stepFraction)
    {
        if (_vm?.IsSubtitleMode != true)
            return;

        var screen = Screens?.ScreenFromWindow(this) ?? Screens?.Primary;
        if (screen == null)
            return;

        var scaling = screen.Scaling <= 0 ? 1 : screen.Scaling;
        var workWidthDip = screen.WorkingArea.Width / scaling;
        var step = workWidthDip * stepFraction;
        var minW = 300d;
        var maxW = workWidthDip * 0.80;

        Width = Math.Clamp(Width + step, Math.Max(MinWidth, minW), maxW);
        _subtitleWindowSize = (Width, Height);
    }

    private void AttachViewModelHandlers()
    {
        if (_vm != null)
        {
            _vm.PropertyChanged -= OnViewModelPropertyChanged;
            _vm.Bubbles.CollectionChanged -= OnBubblesCollectionChanged;
        }

        _vm = DataContext as MainViewModel;

        if (_vm != null)
        {
            _vm.PropertyChanged += OnViewModelPropertyChanged;
            _vm.Bubbles.CollectionChanged += OnBubblesCollectionChanged;
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.IsSettingsVisible))
        {
            RefreshWindowLayout();
        }

        if (e.PropertyName == nameof(MainViewModel.IsSubtitleMode))
        {
            RefreshWindowLayout();
        }

        if (e.PropertyName == nameof(MainViewModel.IsRunning) && _vm?.IsRunning == false)
        {
            _normalWindowSize = GetDefaultWindowSize();
            RefreshWindowLayout();
        }
    }

    private void OnBubblesCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action is NotifyCollectionChangedAction.Add or NotifyCollectionChangedAction.Replace or NotifyCollectionChangedAction.Reset)
        {
            Dispatcher.UIThread.Post(ScrollBubblesToBottom, DispatcherPriority.Background);
        }
    }

    private void ScrollBubblesToBottom()
    {
        if (_bubbleScrollViewer == null)
            return;

        _bubbleScrollViewer.Offset = new Vector(_bubbleScrollViewer.Offset.X, _bubbleScrollViewer.Extent.Height);
    }

    private void ApplyOrientationSize()
    {
        if (_vm == null)
            return;

        _isApplyingWindowLayout = true;

        try
        {
            if (_vm.IsSubtitleMode)
            {
                MinWidth = 370;
                MinHeight = 250;
                MaxHeight = 350;

                if (!_lastIsSubtitleMode)
                    _subtitleWindowSize = null;

                var subtitleTarget = _subtitleWindowSize ?? GetDefaultSubtitleSize();
                Width = Math.Max(subtitleTarget.Width, MinWidth);
                Height = 200;
                SystemDecorations = SystemDecorations.None;
                Background = Brushes.Transparent;
                MoveToBottomCenter();

                _lastIsSubtitleMode = true;
                return;
            }

            SystemDecorations = SystemDecorations.BorderOnly;
            Background = new SolidColorBrush(Color.Parse("#AA000000"));
            MaxHeight = double.PositiveInfinity;
            MinWidth = 250;
            MinHeight = 600;

            var normalTarget = _normalWindowSize ?? GetDefaultWindowSize();
            Width = Math.Max(normalTarget.Width, MinWidth);
            Height = Math.Max(normalTarget.Height, MinHeight);
            MoveToBottomCenter(BottomScreenMarginPx);

            _lastIsSubtitleMode = false;
        }
        finally
        {
            _isApplyingWindowLayout = false;
        }
    }

    private void RefreshResponsiveLayout()
    {
        InvalidateMeasure();
        InvalidateArrange();
        InvalidateVisual();

        _headerPanel?.InvalidateMeasure();
        _headerPanel?.InvalidateArrange();
        _headerPanel?.InvalidateVisual();

        _bubbleScrollViewer?.InvalidateMeasure();
        _bubbleScrollViewer?.InvalidateArrange();
        _bubbleScrollViewer?.InvalidateVisual();

        _headerDragArea?.InvalidateMeasure();
        _headerDragArea?.InvalidateArrange();
        _headerDragArea?.InvalidateVisual();

    }

    private void RefreshWindowLayout()
    {
        ApplyOrientationSize();
        RefreshResponsiveLayout();
        UpdateLayout();

        Dispatcher.UIThread.Post(() =>
        {
            ApplyOrientationSize();
            RefreshResponsiveLayout();
            UpdateLayout();
        }, DispatcherPriority.Render);

        Dispatcher.UIThread.Post(() =>
        {
            ApplyOrientationSize();
            RefreshResponsiveLayout();
            UpdateLayout();
        }, DispatcherPriority.Loaded);

        Dispatcher.UIThread.Post(() =>
        {
            if (_vm?.IsSubtitleMode == false)
            {
                _normalWindowSize = GetDefaultWindowSize();
                ApplyOrientationSize();
                RefreshResponsiveLayout();
                UpdateLayout();
            }
        }, DispatcherPriority.ApplicationIdle);
    }

    private void OnWindowSizeChanged(object? sender, SizeChangedEventArgs e)
    {
        if (_vm == null || _isApplyingWindowLayout || (!e.WidthChanged && !e.HeightChanged))
            return;

        if (_vm.IsSubtitleMode)
        {
            _subtitleWindowSize = (Width, 200);
            if (Math.Abs(Height - 200) > 0.5)
                Height = 200;
            return;
        }

        _normalWindowSize = (Width, Height);
    }

    private (double Width, double Height) GetDefaultWindowSize()
    {
        return (350, 600);
    }

    private (double Width, double Height) GetDefaultSubtitleSize()
    {
        return (600, 200);
    }

    private void MoveToBottomCenter(double bottomMarginDip = 0)
    {
        var screen = Screens?.ScreenFromWindow(this) ?? Screens?.Primary;
        if (screen == null)
            return;

        var scaling = screen.Scaling <= 0 ? 1 : screen.Scaling;
        var widthPx = (int)Math.Round(Width * scaling);
        var heightPx = (int)Math.Round(Height * scaling);
        var bottomMarginPx = (int)Math.Round(bottomMarginDip * scaling);
        var x = screen.WorkingArea.X + Math.Max(0, (screen.WorkingArea.Width - widthPx) / 2);
        var y = screen.WorkingArea.Y + Math.Max(0, screen.WorkingArea.Height - heightPx - bottomMarginPx);
        Position = new PixelPoint(x, y);
    }

    private void OnWindowPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_vm?.IsSubtitleMode != true)
            return;

        _vm.IsSubtitleChromeVisible = e.GetPosition(this).Y <= 56;
    }

    private void OnWindowPointerExited(object? sender, PointerEventArgs e)
    {
        if (_vm?.IsSubtitleMode == true)
            _vm.IsSubtitleChromeVisible = false;
    }
}