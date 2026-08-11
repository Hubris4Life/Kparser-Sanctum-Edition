using System.Windows;
using System.Windows.Input;
using KParser.Sanctum.UI.Models;
using KParser.Sanctum.UI.Services;

namespace KParser.Sanctum.UI;

public partial class DiagnosticsWindow : Window
{
    private readonly Func<Task<ApplicationDiagnosticReport>> reportProvider;
    private ApplicationDiagnosticReport? currentReport;

    internal DiagnosticsWindow(Func<Task<ApplicationDiagnosticReport>> reportProvider)
    {
        InitializeComponent();
        this.reportProvider = reportProvider;
        Loaded += async (_, _) => await RefreshReportAsync();
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e) =>
        await RefreshReportAsync();

    private async Task RefreshReportAsync()
    {
        RefreshButton.IsEnabled = false;
        StatusText.Text = "Collecting current diagnostics…";
        try
        {
            currentReport = await reportProvider();
            DiagnosticsGrid.ItemsSource = currentReport.Items;
            StatusText.Text = $"Updated {DateTime.Now:t}";
        }
        catch (Exception ex)
        {
            ApplicationDiagnostics.LogHandledException("Build diagnostic report", ex);
            StatusText.Text = "Diagnostics could not be refreshed: " + ex.Message;
        }
        finally
        {
            RefreshButton.IsEnabled = true;
        }
    }

    private void CopyReport_Click(object sender, RoutedEventArgs e)
    {
        if (currentReport is null)
        {
            StatusText.Text = "Refresh the report before copying it.";
            return;
        }

        try
        {
            Clipboard.SetText(currentReport.Text);
            StatusText.Text = "Diagnostic report copied to the clipboard.";
        }
        catch (Exception ex)
        {
            ApplicationDiagnostics.LogHandledException("Copy diagnostic report", ex);
            StatusText.Text = "The report could not be copied: " + ex.Message;
        }
    }

    private void OpenLogs_Click(object sender, RoutedEventArgs e)
    {
        StatusText.Text = DiagnosticReportService.TryOpenLogDirectory(out var error)
            ? "Opened the KParser log folder."
            : "The log folder could not be opened: " + error;
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed)
            return;
        try { DragMove(); } catch (InvalidOperationException) { }
    }
}
