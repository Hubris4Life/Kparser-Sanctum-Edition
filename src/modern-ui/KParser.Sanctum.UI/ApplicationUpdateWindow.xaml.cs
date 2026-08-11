using System.Globalization;
using System.Windows;
using System.Windows.Input;
using KParser.Sanctum.UI.Models;
using KParser.Sanctum.UI.Services;

namespace KParser.Sanctum.UI;

public partial class ApplicationUpdateWindow : Window
{
    private readonly ApplicationUpdateCheckResult update;
    private readonly ApplicationUpdateService updateService;
    private readonly CancellationTokenSource downloadCancellation = new();
    private int pageIndex;
    private bool downloading;

    internal ApplicationUpdateWindow(
        ApplicationUpdateCheckResult update,
        ApplicationUpdateService updateService)
    {
        this.update = update;
        this.updateService = updateService;
        InitializeComponent();
        VersionSummaryText.Text =
            $"Installed: {update.CurrentVersion}   →   Available: {update.LatestRelease.Tag}";
        InstallButton.Content = update.IsPortableInstallation
            ? "Update Portable Copy"
            : "Install Update";

        var package = updateService.SelectPackageAsset(update);
        InstallButton.IsEnabled = package is not null;
        PackageSummaryText.Text = package is null
            ? "The expected update package is not attached to this release."
            : $"{(update.IsPortableInstallation ? "Portable ZIP" : "Setup package")} · {FormatSize(package.Size)} · SHA-256 verified before installation";
        StatusText.Text = package is null
            ? "Open the GitHub Releases page to install this version manually."
            : "KParser will keep running until you choose to install the update.";
        ShowPage(0);
        Closing += (_, eventArgs) =>
        {
            if (downloading)
                eventArgs.Cancel = true;
        };
        Closed += (_, _) => downloadCancellation.Dispose();
    }

    internal ApplicationUpdateWindowOutcome Outcome { get; private set; } =
        ApplicationUpdateWindowOutcome.RemindLater;

    private void ShowPage(int index)
    {
        pageIndex = Math.Clamp(index, 0, update.AvailableReleases.Count - 1);
        var release = update.AvailableReleases[pageIndex];
        ReleaseNameText.Text = release.Name;
        ReleaseDateText.Text = release.PublishedAt is null
            ? release.Tag
            : $"{release.Tag} · published {release.PublishedAt.Value.ToLocalTime():MMMM d, yyyy}";
        PageIndicatorText.Text =
            $"Update {pageIndex + 1} of {update.AvailableReleases.Count}";
        ReleaseNotesText.Text = ApplicationReleaseNotesFormatter.ToDisplayText(release.Notes);
        ReleaseNotesText.ScrollToHome();
        PreviousPageButton.IsEnabled = !downloading && pageIndex > 0;
        NextPageButton.IsEnabled = !downloading && pageIndex + 1 < update.AvailableReleases.Count;
    }

    private async void InstallUpdate_Click(object sender, RoutedEventArgs e)
    {
        if (downloading)
            return;

        downloading = true;
        SetActionButtonsEnabled(false);
        DownloadProgressBar.Visibility = Visibility.Visible;
        DownloadProgressBar.IsIndeterminate = true;
        var progress = new Progress<ApplicationUpdateProgress>(value =>
        {
            StatusText.Text = value.Status;
            if (value.Percent is { } percent)
            {
                DownloadProgressBar.IsIndeterminate = false;
                DownloadProgressBar.Value = percent;
            }
        });

        try
        {
            await updateService.PrepareAndLaunchAsync(
                update,
                progress,
                downloadCancellation.Token);
            Outcome = ApplicationUpdateWindowOutcome.UpdateLaunched;
            StatusText.Text = "The verified update is ready. KParser is closing safely.";
            DialogResult = true;
        }
        catch (OperationCanceledException)
        {
            StatusText.Text = "The update download was cancelled.";
            SetActionButtonsEnabled(true);
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                this,
                ex.Message,
                "KParser update failed",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            StatusText.Text = "The update was not installed. KParser can continue running normally.";
            SetActionButtonsEnabled(true);
        }
        finally
        {
            downloading = false;
            if (Outcome != ApplicationUpdateWindowOutcome.UpdateLaunched)
            {
                DownloadProgressBar.Visibility = Visibility.Collapsed;
                ShowPage(pageIndex);
            }
        }
    }

    private void SetActionButtonsEnabled(bool enabled)
    {
        var hasPackage = updateService.SelectPackageAsset(update) is not null;
        InstallButton.IsEnabled = enabled && hasPackage;
        SkipButton.IsEnabled = enabled;
        RemindButton.IsEnabled = enabled;
        TitleCloseButton.IsEnabled = enabled;
        PreviousPageButton.IsEnabled = enabled && pageIndex > 0;
        NextPageButton.IsEnabled = enabled && pageIndex + 1 < update.AvailableReleases.Count;
    }

    private void PreviousPage_Click(object sender, RoutedEventArgs e) =>
        ShowPage(pageIndex - 1);

    private void NextPage_Click(object sender, RoutedEventArgs e) =>
        ShowPage(pageIndex + 1);

    private void SkipVersion_Click(object sender, RoutedEventArgs e)
    {
        Outcome = ApplicationUpdateWindowOutcome.SkipVersion;
        Close();
    }

    private void RemindLater_Click(object sender, RoutedEventArgs e)
    {
        if (!downloading)
            Close();
    }

    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
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

    private static string FormatSize(long bytes)
    {
        if (bytes <= 0)
            return "size unavailable";
        var megabytes = bytes / 1024.0 / 1024.0;
        return string.Format(CultureInfo.CurrentCulture, "{0:N1} MB", megabytes);
    }
}
