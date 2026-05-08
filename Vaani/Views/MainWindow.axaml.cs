using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using System;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using Vaani.Models;
using Vaani.ViewModels;

namespace Vaani.Views;

public partial class MainWindow : Window
{
    private ScrollViewer? _messageScroller;
    private ListBox? _messageListBox;
    private bool _isNearBottom = true;
    private bool _isScrolling = false;
    private bool _isClosing = false;

    public MainWindow()
    {
        InitializeComponent();
        var viewModel = new MainViewModel();
        DataContext = viewModel;

        foreach (var bubble in viewModel.Messages)
        {
            SubscribeToBubble(bubble);
        }

        // Subscribe to Messages collection changes
        viewModel.Messages.CollectionChanged += Messages_CollectionChanged;

        // Get reference to scroller when window opens
        this.Opened += (s, e) =>
        {
            _messageListBox = this.FindControl<ListBox>("MessageListBox");
            _messageScroller = _messageListBox?.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault();

            if (_messageScroller != null)
            {
                // Monitor scroll position changes
                _messageScroller.PropertyChanged += (sender, args) =>
                {
                    if (args.Property.Name == nameof(ScrollViewer.Offset) && _messageScroller != null)
                    {
                        var maxScroll = _messageScroller.Extent.Height - _messageScroller.Viewport.Height;
                        var currentScroll = _messageScroller.Offset.Y;

                        // Consider "near bottom" if within 48px
                        _isNearBottom = maxScroll <= 0 || (maxScroll - currentScroll) < 48;
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
                    await viewModel.CleanupAsync();
                }              
                finally
                {
                    // Dispose ViewModel resources
                    viewModel.Dispose();

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
                    await viewModel.CleanupAsync();
                    viewModel.Dispose();
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
        if (e.OldItems != null)
        {
            foreach (var item in e.OldItems.OfType<MessageBubble>())
            {
                UnsubscribeFromBubble(item);
            }
        }

        if (e.Action == NotifyCollectionChangedAction.Add && e.NewItems != null)
        {
            foreach (var item in e.NewItems.OfType<MessageBubble>())
            {
                SubscribeToBubble(item);
            }

            Dispatcher.UIThread.Post(() =>
            {
                AnimateNewItems(e.NewStartingIndex, e.NewItems.Count, retryCount: 0);
            }, DispatcherPriority.Background);
        }

        if (e.Action == NotifyCollectionChangedAction.Reset && DataContext is MainViewModel viewModel)
        {
            foreach (var bubble in viewModel.Messages)
            {
                SubscribeToBubble(bubble);
            }
        }

        if (_isNearBottom && e.Action == NotifyCollectionChangedAction.Add)
        {
            ScheduleScroll(60);
        }
    }

    private void SubscribeToBubble(MessageBubble bubble)
    {
        bubble.PropertyChanged -= Bubble_PropertyChanged;
        bubble.PropertyChanged += Bubble_PropertyChanged;
    }

    private void UnsubscribeFromBubble(MessageBubble bubble)
    {
        bubble.PropertyChanged -= Bubble_PropertyChanged;
    }

    private void Bubble_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (!_isNearBottom || sender is not MessageBubble bubble || DataContext is not MainViewModel viewModel)
        {
            return;
        }

        if (viewModel.Messages.Count == 0 || !ReferenceEquals(viewModel.Messages[^1], bubble))
        {
            return;
        }

        if (e.PropertyName is nameof(MessageBubble.OriginalText)
            or nameof(MessageBubble.TranslatedText)
            or nameof(MessageBubble.IsRecognizing))
        {
            ScheduleScroll(35);
        }
    }

    private void AnimateNewItems(int startIndex, int count, int retryCount)
    {
        if (_messageListBox == null || retryCount > 2) return;

        int animatedCount = 0;

        for (int i = 0; i < count; i++)
        {
            var index = startIndex + i;
            var container = _messageListBox.ContainerFromIndex(index) as ListBoxItem;

            if (container != null)
            {
                container.Opacity = 0;

                DispatcherTimer.RunOnce(() =>
                {
                    container.Opacity = 1;
                }, TimeSpan.FromMilliseconds(20));

                animatedCount++;
            }
        }

        if (animatedCount < count)
        {
            DispatcherTimer.RunOnce(() =>
            {
                AnimateNewItems(startIndex, count, retryCount + 1);
            }, TimeSpan.FromMilliseconds(40));
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