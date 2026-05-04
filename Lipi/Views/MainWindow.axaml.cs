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
    private MainViewModel? _vm;
    private ScrollViewer? _bubbleScrollViewer;
    private Grid? _headerDragArea;
    private Border? _statusRow;
    private bool _hasLastOrientation;
    private bool _lastIsHorizontal;
    private bool _lastIsSubtitleMode;
    private (double Width, double Height)? _horizontalWindowSize;
    private (double Width, double Height)? _verticalWindowSize;
    private (double Width, double Height)? _subtitleWindowSize;
    private (double Width, double Height)? _normalWindowSizeBeforeSubtitle;

    public MainWindow()
    {
        InitializeComponent();
        _bubbleScrollViewer = this.FindControl<ScrollViewer>("BubbleScrollViewer");
        _headerDragArea = this.FindControl<Grid>("HeaderDragArea");
        _statusRow = this.FindControl<Border>("StatusRow");

        Opened += (_, _) =>
        {
            AttachViewModelHandlers();
            ApplyOrientationSize();
        };

        DataContextChanged += (_, _) =>
        {
            AttachViewModelHandlers();
            ApplyOrientationSize();
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

            _hasLastOrientation = true;
            _lastIsHorizontal = _vm.IsHorizontal;
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(MainViewModel.IsHorizontal) or nameof(MainViewModel.IsSettingsVisible))
        {
            ApplyOrientationSize();
            Dispatcher.UIThread.Post(RefreshResponsiveLayout, DispatcherPriority.Render);
            Dispatcher.UIThread.Post(() =>
            {
                RefreshResponsiveLayout();
                UpdateLayout();
            }, DispatcherPriority.Loaded);
        }

        if (e.PropertyName == nameof(MainViewModel.IsSubtitleMode))
        {
            ApplyOrientationSize();
            RefreshResponsiveLayout();
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

        if (_vm.IsSubtitleMode)
        {
            MinWidth = 620;
            MinHeight = 250;
            MaxHeight = 250;

            if (!_lastIsSubtitleMode)
                _normalWindowSizeBeforeSubtitle = (Width, Height);

            var target = _subtitleWindowSize ?? GetDefaultSubtitleSize();
            Width = Math.Max(target.Width, MinWidth);
            Height = 250;
            SystemDecorations = SystemDecorations.None;
            Background = Brushes.Transparent;
            MoveToBottomCenter();

            _lastIsSubtitleMode = true;
            _hasLastOrientation = true;
            _lastIsHorizontal = _vm.IsHorizontal;
            return;
        }

        if (_lastIsSubtitleMode && _normalWindowSizeBeforeSubtitle.HasValue)
        {
            Width = _normalWindowSizeBeforeSubtitle.Value.Width;
            Height = _normalWindowSizeBeforeSubtitle.Value.Height;
        }

        SystemDecorations = SystemDecorations.BorderOnly;
        Background = new SolidColorBrush(Color.Parse("#AA000000"));
        MaxHeight = double.PositiveInfinity;

        var shouldMoveToDefaultPosition = false;

        if (_vm.IsHorizontal)
        {
            MinWidth = 520;
            MinHeight = 220;

            var target = _horizontalWindowSize ?? GetDefaultHorizontalSize();

            if (!_hasLastOrientation || !_lastIsHorizontal || _horizontalWindowSize == null)
            {
                Width = Math.Max(target.Width, MinWidth);
                Height = Math.Max(target.Height, MinHeight);
                shouldMoveToDefaultPosition = true;
            }
        }
        else
        {
            MinWidth = 320;
            MinHeight = 220;

            var target = _verticalWindowSize ?? GetDefaultVerticalSize();

            if (!_hasLastOrientation || _lastIsHorizontal || _verticalWindowSize == null)
            {
                Width = Math.Max(target.Width, MinWidth);
                Height = Math.Max(target.Height, MinHeight);
                shouldMoveToDefaultPosition = true;
            }
        }

        if (shouldMoveToDefaultPosition)
        {
            if (_vm.IsHorizontal)
                MoveToBottomCenter();
            else
                MoveToBottomRight();
        }

        _hasLastOrientation = true;
        _lastIsHorizontal = _vm.IsHorizontal;
        _lastIsSubtitleMode = false;
    }

    private void RefreshResponsiveLayout()
    {
        InvalidateMeasure();
        InvalidateArrange();
        InvalidateVisual();

        _bubbleScrollViewer?.InvalidateMeasure();
        _bubbleScrollViewer?.InvalidateArrange();
        _bubbleScrollViewer?.InvalidateVisual();

        _headerDragArea?.InvalidateMeasure();
        _headerDragArea?.InvalidateArrange();
        _headerDragArea?.InvalidateVisual();

    }

    private void OnWindowSizeChanged(object? sender, SizeChangedEventArgs e)
    {
        if (_vm == null || (!e.WidthChanged && !e.HeightChanged))
            return;

        if (_vm.IsSubtitleMode)
        {
            _subtitleWindowSize = (Width, Height);
            MoveToBottomCenter();
            return;
        }

        if (_vm.IsHorizontal)
            _horizontalWindowSize = (Width, Height);
        else
            _verticalWindowSize = (Width, Height);
    }

    private (double Width, double Height) GetDefaultHorizontalSize()
    {
        return (620, 300);
    }

    private (double Width, double Height) GetDefaultVerticalSize()
    {
        return (300, 620);
    }

    private (double Width, double Height) GetDefaultSubtitleSize()
    {
        var screen = Screens?.ScreenFromWindow(this) ?? Screens?.Primary;
        if (screen == null)
            return (900, 250);

        var scaling = screen.Scaling <= 0 ? 1 : screen.Scaling;
        var workWidthDip = screen.WorkingArea.Width / scaling;
        return (Math.Clamp(workWidthDip * 0.78, 700, workWidthDip * 0.92), 250);
    }

    private void MoveToBottomCenter()
    {
        var screen = Screens?.ScreenFromWindow(this) ?? Screens?.Primary;
        if (screen == null)
            return;

        var scaling = screen.Scaling <= 0 ? 1 : screen.Scaling;
        var widthPx = (int)Math.Round(Width * scaling);
        var heightPx = (int)Math.Round(Height * scaling);
        var marginPx = (int)Math.Round(20 * scaling);
        var x = screen.WorkingArea.X + Math.Max(0, (screen.WorkingArea.Width - widthPx) / 2);
        var y = screen.WorkingArea.Y + Math.Max(0, screen.WorkingArea.Height - heightPx - marginPx);
        Position = new PixelPoint(x, y);
    }

    private void MoveToBottomRight()
    {
        var screen = Screens?.ScreenFromWindow(this) ?? Screens?.Primary;
        if (screen == null)
            return;

        var scaling = screen.Scaling <= 0 ? 1 : screen.Scaling;
        var widthPx = (int)Math.Round(Width * scaling);
        var heightPx = (int)Math.Round(Height * scaling);
        var marginPx = (int)Math.Round(20 * scaling);
        var x = screen.WorkingArea.X + Math.Max(0, screen.WorkingArea.Width - widthPx - marginPx);
        var y = screen.WorkingArea.Y + Math.Max(0, screen.WorkingArea.Height - heightPx - marginPx);
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