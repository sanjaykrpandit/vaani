using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using System;

namespace Vaani.Views;

/// <summary>
/// Simple message window to show when application is already running
/// </summary>
public partial class AlreadyRunningWindow : Window
{
    public AlreadyRunningWindow()
    {
        InitializeComponent();
        Width = 450;
        Height = 200;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Title = "Vaani - Already Running";
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    private void OnOkClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        Close();
    }
}
