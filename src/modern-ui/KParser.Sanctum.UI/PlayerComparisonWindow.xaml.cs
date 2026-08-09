using System.Windows;
using System.Windows.Input;
using KParser.Sanctum.UI.Models;
using KParser.Sanctum.UI.Services;
using KParser.Sanctum.UI.ViewModels;

namespace KParser.Sanctum.UI;

public partial class PlayerComparisonWindow : Window
{
    private readonly PlayerComparisonViewModel viewModel;

    internal PlayerComparisonWindow(PlayerParseService service)
    {
        InitializeComponent();
        viewModel = new PlayerComparisonViewModel(service);
        DataContext = viewModel;
    }

    public void RefreshSnapshots(string? preferPlayer = null)
    {
        viewModel.Refresh(preferPlayer);
    }

    private void Refresh_Click(object sender, RoutedEventArgs e)
    {
        viewModel.Refresh();
    }

    private void Swap_Click(object sender, RoutedEventArgs e)
    {
        viewModel.Swap();
    }

    private void DeleteFirst_Click(object sender, RoutedEventArgs e)
    {
        DeleteSnapshot(viewModel.SelectedFirst);
    }

    private void DeleteSecond_Click(object sender, RoutedEventArgs e)
    {
        DeleteSnapshot(viewModel.SelectedSecond);
    }

    private void DeleteSnapshot(PlayerParseSnapshot? snapshot)
    {
        if (snapshot is null)
            return;

        var answer = MessageBox.Show(
            this,
            $"Delete the saved parse '{snapshot.Label}' for {snapshot.PlayerName}?",
            "Delete Saved Player Parse",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning,
            MessageBoxResult.No);
        if (answer == MessageBoxResult.Yes)
            viewModel.Delete(snapshot);
    }

    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            WindowState = WindowState == WindowState.Maximized
                ? WindowState.Normal
                : WindowState.Maximized;
            return;
        }

        if (e.LeftButton != MouseButtonState.Pressed)
            return;
        try
        {
            DragMove();
        }
        catch (InvalidOperationException)
        {
        }
    }

    private void Minimize_Click(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState.Minimized;
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
