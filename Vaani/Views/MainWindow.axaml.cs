using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.Media;
using Avalonia.LogicalTree;
using Avalonia.VisualTree;
using ReactiveUI;
using System;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using Vaani.ViewModels;

namespace Vaani.Views;

public partial class MainWindow : Window
{
    private ScrollViewer? _messageScroller;
    private ListBox? _messageListBox;
    private bool _isNearBottom = true;
    private bool _isScrolling = false;
    private DispatcherTimer? _smoothScrollTimer;
    private bool _isClosing = false;

    public MainWindow()
    {
        InitializeComponent();
        var viewModel = new MainViewModel();
        DataContext = viewModel;

        // Subscribe to Messages collection changes
        viewModel.Messages.CollectionChanged += Messages_CollectionChanged;

        // Setup smooth scroll timer for real-time updates
        _smoothScrollTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(200)
        };
        _smoothScrollTimer.Tick += (s, e) =>
        {
            if (_isNearBottom && !_isScrolling)
            {
                ScrollToBottom();
            }
        };

        // Start/stop timer based on IsRunning
        viewModel.WhenAnyValue(x => x.IsRunning)
            .Subscribe(isRunning =>
            {
                if (isRunning)
                    _smoothScrollTimer?.Start();
                else
                    _smoothScrollTimer?.Stop();
            });

        // Get reference to scroller when window opens
        this.Opened += (s, e) =>
        {
            _messageScroller = this.FindControl<ScrollViewer>("MessageScroller");
            _messageListBox = _messageScroller?.GetVisualDescendants().OfType<ListBox>().FirstOrDefault();

            if (_messageScroller != null)
            {
                // Monitor scroll position changes
                _messageScroller.PropertyChanged += (sender, args) =>
                {
                    if (args.Property.Name == nameof(ScrollViewer.Offset) && _messageScroller != null)
                    {
                        var maxScroll = _messageScroller.Extent.Height - _messageScroller.Viewport.Height;
                        var currentScroll = _messageScroller.Offset.Y;

                        // Consider "near bottom" if within 150px
                        _isNearBottom = maxScroll <= 0 || (maxScroll - currentScroll) < 150;
                    }
                };
            }
        };

        // Handle window closing event
        this.Closing += OnWindowClosing;
    }

    private async void OnWindowClosing(object? sender, WindowClosingEventArgs e)
    {
        // Prevent multiple close attempts
        if (_isClosing)
            return;

        if (DataContext is MainViewModel viewModel)
        {
            // Check if translation is running or synthesis is active
            if (viewModel.IsRunning || viewModel.IsSynthesizing)
            {
                // Cancel the close event to perform cleanup first
                e.Cancel = true;
                _isClosing = true;

                try
                {
                    // Show a message to the user
                    viewModel.Toast.Show("Closing application - stopping translation...");

                    // Wait for synthesis to complete with timeout
                    if (viewModel.IsSynthesizing)
                    {
                        var timeout = TimeSpan.FromSeconds(5);
                        var startTime = DateTime.UtcNow;

                        while (viewModel.IsSynthesizing && (DateTime.UtcNow - startTime) < timeout)
                        {
                            await Task.Delay(100);
                        }

                        if (viewModel.IsSynthesizing)
                        {
                            System.Diagnostics.Debug.WriteLine("⚠️ Synthesis timeout - forcing shutdown");
                        }
                    }

                    // Stop translation and end session
                    await viewModel.CleanupAsync();

                    System.Diagnostics.Debug.WriteLine("✅ Cleanup completed successfully");
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"❌ Error during cleanup: {ex.Message}");
                }
                finally
                {
                    // Dispose ViewModel resources
                    viewModel.Dispose();

                    // Stop smooth scroll timer
                    _smoothScrollTimer?.Stop();

                    // Remove the closing event handler to prevent infinite loop
                    this.Closing -= OnWindowClosing;

                    // Now close the window
                    this.Close();
                }
            }
            else
            {
                // Not running, just cleanup
                try
                {
                    viewModel.Dispose();
                    _smoothScrollTimer?.Stop();
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"⚠️ Error during disposal: {ex.Message}");
                }
            }
        }
    }

    private void Messages_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        // Animate new items
        if (e.Action == NotifyCollectionChangedAction.Add && e.NewItems != null)
        {
            // Use multiple retries to handle virtualization delays
            Dispatcher.UIThread.Post(() =>
            {
                AnimateNewItems(e.NewStartingIndex, e.NewItems.Count, retryCount: 0);
            }, DispatcherPriority.Render);
        }

        // Immediate scroll on Add (new message bubble)
        if (_isNearBottom && e.Action == NotifyCollectionChangedAction.Add)
        {
            ScheduleScroll(0);
            ScheduleScroll(100);
        }
        // For Replace (text updates), let the timer handle it smoothly
    }

    private void AnimateNewItems(int startIndex, int count, int retryCount)
    {
        if (_messageListBox == null || retryCount > 3) return;

        int animatedCount = 0;

        for (int i = 0; i < count; i++)
        {
            var index = startIndex + i;
            var container = _messageListBox.ContainerFromIndex(index) as ListBoxItem;

            if (container != null)
            {
                // Start with invisible and offset
                container.Opacity = 0;
                container.RenderTransform = new TranslateTransform(0, 20);

                // Trigger animation by setting final values after a tiny delay
                DispatcherTimer.RunOnce(() =>
                {
                    container.Opacity = 1;
                    container.RenderTransform = new TranslateTransform(0, 0);
                }, TimeSpan.FromMilliseconds(50));

                animatedCount++;
            }
        }

        // If containers weren't ready yet, retry after a short delay
        if (animatedCount < count)
        {
            DispatcherTimer.RunOnce(() =>
            {
                AnimateNewItems(startIndex, count, retryCount + 1);
            }, TimeSpan.FromMilliseconds(100));
        }
    }

    private void ScheduleScroll(int delayMs)
    {
        if (_isScrolling) return;

        if (delayMs > 0)
        {
            DispatcherTimer.RunOnce(() =>
            {
                ScrollToBottom();
            }, TimeSpan.FromMilliseconds(delayMs));
        }
        else
        {
            Dispatcher.UIThread.Post(ScrollToBottom, DispatcherPriority.Render);
        }
    }

    private void ScrollToBottom()
    {
        if (_messageScroller != null && _isNearBottom && !_isScrolling)
        {
            _isScrolling = true;

            try
            {
                // Smooth scroll to bottom
                _messageScroller.Offset = new Vector(_messageScroller.Offset.X, double.MaxValue);
            }
            finally
            {
                DispatcherTimer.RunOnce(() => { _isScrolling = false; }, TimeSpan.FromMilliseconds(50));
            }
        }
    }
}