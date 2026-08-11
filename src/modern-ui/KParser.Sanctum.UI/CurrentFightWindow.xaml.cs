using System.IO;
using System.Windows;
using System.Windows.Input;
using KParser.Sanctum.UI.Services;
using KParser.Sanctum.UI.ViewModels;

namespace KParser.Sanctum.UI;

public partial class CurrentFightWindow : Window
{
    private readonly CurrentFightViewModel viewModel;

    internal CurrentFightWindow(CurrentFightViewModel viewModel)
    {
        InitializeComponent();
        this.viewModel = viewModel;
        DataContext = viewModel;
    }

    internal event EventHandler? SaveBuildRequested;

    private void CopySummary_Click(object sender, RoutedEventArgs e)
    {
        if (viewModel.Combatants.Count == 0)
        {
            viewModel.SetUserNotice("There is no current-fight data to copy yet.");
            return;
        }

        try
        {
            Clipboard.SetText(ReportExportService.BuildCurrentFightSummary(viewModel));
            viewModel.SetUserNotice("Current fight summary copied to the clipboard.");
        }
        catch (Exception ex)
        {
            viewModel.SetUserNotice("The summary could not be copied: " + ex.Message);
        }
    }

    private void SaveBuild_Click(object sender, RoutedEventArgs e)
    {
        SaveBuildRequested?.Invoke(this, EventArgs.Empty);
    }

    private async void SendParty_Click(object sender, RoutedEventArgs e)
    {
        var selected = viewModel.SelectedCombatant;
        if (selected is null)
        {
            viewModel.SetUserNotice("Select a player row to send its summary.");
            return;
        }

        var result = await GameChatService.SendPartySummaryAsync(selected);
        if (!result.Success)
        {
            try
            {
                Clipboard.SetText(result.Command);
            }
            catch
            {
            }
        }
        viewModel.SetUserNotice(result.Message);
    }

    private void Export_Click(object sender, RoutedEventArgs e)
    {
        if (viewModel.Combatants.Count == 0)
        {
            viewModel.SetUserNotice("There is no live-monitor data to export yet.");
            return;
        }

        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title = "Export live monitor data",
            Filter = "CSV files (*.csv)|*.csv|All files (*.*)|*.*",
            FileName = ReportExportService.CreateCurrentFightFileName(viewModel),
            DefaultExt = ".csv",
            AddExtension = true
        };
        if (dialog.ShowDialog(this) != true)
            return;

        try
        {
            File.WriteAllText(
                dialog.FileName,
                ReportExportService.BuildCurrentFightCsv(viewModel),
                new System.Text.UTF8Encoding(true));
            viewModel.SetUserNotice("Live-monitor data exported successfully.");
        }
        catch (Exception ex)
        {
            viewModel.SetUserNotice("The live-monitor export failed: " + ex.Message);
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void WindowDragArea_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2 && viewModel.IsFullMode)
        {
            WindowState = WindowState == WindowState.Maximized
                ? WindowState.Normal
                : WindowState.Maximized;
            return;
        }

        if (e.LeftButton != MouseButtonState.Pressed)
            return;

        if (WindowState == WindowState.Maximized)
            WindowState = WindowState.Normal;

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

    private void TitleClose_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
