// Code authored by Dean Edis (DeanTheCoder).
// Anyone is free to copy, modify, use, compile, or distribute this software,
// either in source code form or as a compiled binary, for any purpose.
//
// If you modify the code, please retain this copyright header,
// and consider contributing back to the repository or letting us know
// about your modifications. Your contributions are valued!
//
// THE SOFTWARE IS PROVIDED AS IS, WITHOUT WARRANTY OF ANY KIND.

using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using MovieG33k.ViewModels;
#if DEBUG
using Avalonia.Diagnostics;
using Avalonia.Input;
#endif

namespace MovieG33k.Views;

/// <summary>
/// Main desktop shell for MovieG33k.
/// </summary>
/// <remarks>
/// The first pass focuses on discovery and product direction, not on the full library workflow yet.
/// </remarks>
public partial class MainWindow : Window
{
    /// <summary>
    /// Creates the window for XAML loading and designer support.
    /// </summary>
    public MainWindow()
    {
        InitializeComponent();
#if DEBUG
        DevToolsExtensions.AttachDevTools(this, new DevToolsOptions
        {
            Gesture = new KeyGesture(Key.F12)
        });
#endif
    }

    /// <summary>
    /// Creates the window using the supplied view model.
    /// </summary>
    public MainWindow(MainWindowViewModel viewModel)
        : this()
    {
        DataContext = viewModel;
    }

    private void OnSettingsButtonClick(object sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (sender is not Button button)
            return;

        button.ContextMenu?.Open(button);
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}
