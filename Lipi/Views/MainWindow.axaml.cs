using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform;
using Avalonia.Threading;
using Avalonia;
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
    private bool _hasLastOrientation;
    private bool _lastIsHorizontal;
    private (double Width, double Height)? _horizontalWindowSize;
    private (double Width, double Height)? _verticalWindowSize;

    public MainWindow()
    {
        InitializeComponent();
        _bubbleScrollViewer = this.FindControl<ScrollViewer>("BubbleScrollViewer");
        _headerDragArea = this.FindControl<Grid>("HeaderDragArea");

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

        if (_vm.IsHorizontal)
        {
            MinWidth = 520;
            MinHeight = 220;

            if (_hasLastOrientation && !_lastIsHorizontal)
            {
                var target = _horizontalWindowSize ?? GetDefaultHorizontalSize();
                Width = Math.Max(target.Width, MinWidth);
                Height = Math.Max(target.Height, MinHeight);
            }
        }
        else
        {
            MinWidth = 320;
            MinHeight = 220;

            if (_hasLastOrientation && _lastIsHorizontal)
            {
                var target = _verticalWindowSize ?? GetDefaultVerticalSize();
                Width = Math.Max(target.Width, MinWidth);
                Height = Math.Max(target.Height, MinHeight);
            }
        }

        _hasLastOrientation = true;
        _lastIsHorizontal = _vm.IsHorizontal;
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

        if (_vm.IsHorizontal)
            _horizontalWindowSize = (Width, Height);
        else
            _verticalWindowSize = (Width, Height);
    }

    private (double Width, double Height) GetDefaultHorizontalSize()
    {
        return (720, 320);
    }

    private (double Width, double Height) GetDefaultVerticalSize()
    {
        var screen = Screens?.ScreenFromWindow(this) ?? Screens?.Primary;
        if (screen == null)
            return (380, 560);

        var scaling = screen.Scaling <= 0 ? 1 : screen.Scaling;
        var workWidthDip = screen.WorkingArea.Width / scaling;
        var workHeightDip = screen.WorkingArea.Height / scaling;
        return (Math.Clamp(workWidthDip * 0.40, 380, workWidthDip * 0.62), Math.Clamp(workHeightDip * 0.80, 460, workHeightDip * 0.95));
    }
}