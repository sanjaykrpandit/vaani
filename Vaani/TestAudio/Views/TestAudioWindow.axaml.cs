using Avalonia;
using Avalonia.Controls;
using Avalonia.Data.Converters;
using Avalonia.Markup.Xaml;
using System;
using System.Globalization;
using Vaani.TestAudio.ViewModels;

namespace Vaani.TestAudio.Views;

public partial class TestAudioWindow : Window
{
    public bool TestPassed { get; private set; }

    public TestAudioWindow(bool isFromLogin = false)
    {
        InitializeComponent();
        DataContext = new TestAudioViewModel(isFromLogin);
        
        // Auto-start test when window is loaded
        Loaded += OnWindowLoaded;
        
        // Subscribe to window closing to check test result
        Closing += OnWindowClosing;
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    private async void OnWindowLoaded(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        // Auto-start the test after window is shown
        if (DataContext is TestAudioViewModel vm)
        {
            // Small delay to ensure UI is ready
            await System.Threading.Tasks.Task.Delay(100);
            
            // Start test automatically
            vm.StartTestCommand.Execute(null);
        }
    }

    private void OnWindowClosing(object? sender, WindowClosingEventArgs e)
    {
        // Check if tests passed when window is closing
        if (DataContext is TestAudioViewModel vm)
        {
            TestPassed = vm.AllTestsPassed;
        }
    }

    private void OnContinueClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is TestAudioViewModel vm && vm.AllTestsPassed)
        {
            TestPassed = true;
            Close();
        }
    }
}

/// <summary>
/// Converter for bool to status icon (? / ?)
/// </summary>
public class BoolToStatusConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is bool boolValue)
        {
            return boolValue ? "?" : "?";
        }
        return "?";
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
