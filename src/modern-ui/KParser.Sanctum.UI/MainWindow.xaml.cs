using System.ComponentModel;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Win32;
using KParser.Sanctum.UI.Bridge;
using KParser.Sanctum.UI.Models;
using KParser.Sanctum.UI.Services;
using KParser.Sanctum.UI.ViewModels;

namespace KParser.Sanctum.UI;

public partial class MainWindow : Window
{
    private readonly MainWindowViewModel viewModel;
    private readonly ParserBridgeClient bridgeClient = new();
    private readonly EngineProcessManager engineProcessManager = new();
    private readonly UiSettingsService settingsService = new();
    private readonly PlayerParseService playerParseService = new();
    private readonly PlayerInformationService playerInformationService = new();
    private readonly AppSettings settings;
    private readonly SemaphoreSlim refreshGate = new(1, 1);
    private readonly CancellationTokenSource lifetime = new();
    private static readonly TimeSpan MainRefreshInterval = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan MainRefreshWhileMonitorOpenInterval = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan LiveMonitorRefreshInterval = TimeSpan.FromSeconds(1);
    private DateTime nextScheduledMainRefreshUtc = DateTime.MinValue;
    private CurrentFightViewModel? currentFightViewModel;
    private CurrentFightWindow? currentFightWindow;
    private PlayerComparisonWindow? playerComparisonWindow;
    private bool shutdownInProgress;
    private bool shutdownComplete;
    private bool closingCurrentFightForShutdown;
    private string? autoCapturedStatsPlayer;
    private DateTime nextAutomaticStatCaptureUtc = DateTime.MinValue;

    public MainWindow()
    {
        settings = settingsService.Load();
        InitializeComponent();
        viewModel = new MainWindowViewModel();
        DataContext = viewModel;
        viewModel.RestorePreferences(
            settings.MainReport,
            settings.MainEncounterKey,
            settings.MainCombatantScope,
            settings.MainDisplayMode,
            settings.MainGroupMode);
        AutoDetectMemoryMenuItem.IsChecked = settings.AutoDetectMemoryOnStartup;
        LightModeMenuItem.IsChecked = settings.IsLightMode;
        ThemeService.Apply(this, settings.IsLightMode);
        ApplyWindowPlacement(this, settings.MainWindow);

        Loaded += MainWindow_Loaded;
        Closing += MainWindow_Closing;
        Closed += MainWindow_Closed;
        viewModel.RefreshRequested += ViewModel_RefreshRequested;
        viewModel.ReportFilterChanged += ViewModel_ReportFilterChanged;
        viewModel.ReportLayoutChanged += ViewModel_ReportLayoutChanged;
        viewModel.EngineCommandRequested += ViewModel_EngineCommandRequested;
        UpdateReportColumnHeaders();
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            var engineReady = await engineProcessManager.EnsureRunningAsync(
                bridgeClient,
                lifetime.Token);

            if (engineReady)
            {
                viewModel.SetEngineReady(engineProcessManager.OwnsEngineProcess);
                if (settings.AutoDetectMemoryOnStartup)
                    await RunStartupMemoryDetectionAsync();
            }
            else
            {
                var message = engineProcessManager.EnginePath is null
                    ? "Bundled engine files were not found."
                    : "The bundled engine did not start. Try running the application at the same privilege as Sanctum.";
                viewModel.SetEngineLaunchFailed(message);
            }
        }
        catch (OperationCanceledException)
        {
            return;
        }

        if (settings.CurrentFightOpen)
            OpenCurrentFightWindow();

        await PollBridgeAsync(lifetime.Token);
    }

    private async void MainWindow_Closing(object? sender, CancelEventArgs e)
    {
        if (shutdownComplete)
            return;

        e.Cancel = true;
        if (shutdownInProgress)
            return;

        shutdownInProgress = true;
        lifetime.Cancel();
        CaptureSettings();
        closingCurrentFightForShutdown = true;
        currentFightWindow?.Close();
        currentFightWindow = null;
        currentFightViewModel = null;
        playerComparisonWindow?.Close();
        playerComparisonWindow = null;
        settingsService.TrySave(settings, out _);
        viewModel.SetShuttingDown();

        await engineProcessManager.ShutdownAsync(bridgeClient);

        shutdownComplete = true;
        Close();
    }

    private void MainWindow_Closed(object? sender, EventArgs e)
    {
        viewModel.RefreshRequested -= ViewModel_RefreshRequested;
        viewModel.ReportFilterChanged -= ViewModel_ReportFilterChanged;
        viewModel.ReportLayoutChanged -= ViewModel_ReportLayoutChanged;
        viewModel.EngineCommandRequested -= ViewModel_EngineCommandRequested;
        lifetime.Cancel();
        engineProcessManager.Dispose();
        lifetime.Dispose();
    }

    private async void ViewModel_RefreshRequested(object? sender, EventArgs e)
    {
        await RefreshSnapshotAsync(lifetime.Token);
    }

    private async void ViewModel_ReportFilterChanged(object? sender, EventArgs e)
    {
        await RefreshSnapshotAsync(lifetime.Token);
    }

    private void ViewModel_ReportLayoutChanged(object? sender, EventArgs e)
    {
        UpdateReportColumnHeaders();
    }

    private async void ViewModel_EngineCommandRequested(
        object? sender,
        EngineCommandRequestedEventArgs e)
    {
        await ExecuteEngineCommandAsync(e.Command, this);
    }

    private async void CurrentFightViewModel_EngineCommandRequested(
        object? sender,
        EngineCommandRequestedEventArgs e)
    {
        await ExecuteEngineCommandAsync(e.Command, (Window?)currentFightWindow ?? this);
    }

    private async Task ExecuteEngineCommandAsync(
        string command,
        Window? promptOwner = null,
        string? targetPlayer = null)
    {
        if (command == "reset")
        {
            var confirmation = MessageBox.Show(
                promptOwner ?? this,
                "Reset archives the current parse and starts a fresh one. Continue?",
                "Reset parse",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning,
                MessageBoxResult.No);

            if (confirmation != MessageBoxResult.Yes)
                return;
        }

        try
        {
            await refreshGate.WaitAsync(lifetime.Token);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        try
        {
            viewModel.SetEngineCommandBusy(command);
            currentFightViewModel?.SetEngineCommandBusy(command);
            var result = await bridgeClient.SendCommandAsync(
                command,
                targetPlayer,
                lifetime.Token);
            viewModel.ApplyCommandResult(result);
            currentFightViewModel?.ApplyCommandResult(result);
            if (command == "capturestats")
            {
                if (result.Success && !string.IsNullOrWhiteSpace(targetPlayer))
                    autoCapturedStatsPlayer = targetPlayer;
                MessageBox.Show(
                    promptOwner ?? this,
                    result.Message,
                    result.Success ? "DoT stats captured" : "DoT stat capture failed",
                    MessageBoxButton.OK,
                    result.Success ? MessageBoxImage.Information : MessageBoxImage.Warning);
            }
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            viewModel.SetEngineLaunchFailed("Engine command failed: " + ex.Message);
            currentFightViewModel?.SetDisconnected();
        }
        finally
        {
            refreshGate.Release();
        }

        await RefreshSnapshotAsync(lifetime.Token);
    }

    private async void CaptureDotStats_Click(object sender, RoutedEventArgs e)
    {
        var selected = viewModel.SelectedCombatant;
        if (selected is null ||
            !string.Equals(selected.CombatantType, "Player", StringComparison.OrdinalIgnoreCase))
        {
            viewModel.SetUserNotice(
                "Select your player row, then press Capture DoT Stats again.");
            return;
        }

        await ExecuteEngineCommandAsync(
            "capturestats",
            this,
            selected.Name);
    }

    private async Task PollBridgeAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            var monitorVisible = currentFightWindow is { IsVisible: true } &&
                                 currentFightViewModel is not null;
            var now = DateTime.UtcNow;
            var refreshMain = !monitorVisible || now >= nextScheduledMainRefreshUtc;

            await RefreshSnapshotAsync(cancellationToken, refreshMain);
            if (refreshMain)
                nextScheduledMainRefreshUtc = DateTime.UtcNow +
                    (monitorVisible ? MainRefreshWhileMonitorOpenInterval : MainRefreshInterval);

            try
            {
                await Task.Delay(
                    monitorVisible ? LiveMonitorRefreshInterval : MainRefreshInterval,
                    cancellationToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private async Task RefreshSnapshotAsync(
        CancellationToken cancellationToken,
        bool refreshMain = true)
    {
        if (!refreshGate.Wait(0))
            return;

        try
        {
            if (currentFightWindow is { IsVisible: true } && currentFightViewModel is not null)
            {
                try
                {
                    var currentFightSnapshot = await bridgeClient.GetSnapshotAsync(
                        currentFightViewModel.SelectedFightViewKey,
                        0,
                        null,
                        "damageDealt",
                        currentFightViewModel.SelectedCombatantScopeKey,
                        "sources",
                        "player",
                        cancellationToken);
                    playerInformationService.ObserveAndApply(currentFightSnapshot);
                    currentFightViewModel.ApplySnapshot(currentFightSnapshot);
                    await TryCaptureLocalPlayerStatsAsync(currentFightSnapshot, cancellationToken);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception)
                {
                    currentFightViewModel.SetDisconnected();
                }
            }

            if (refreshMain)
            {
                var snapshot = await bridgeClient.GetSnapshotAsync(
                    viewModel.SelectedFilterScope,
                    viewModel.SelectedFilterBattleId,
                    viewModel.SelectedFilterMobName,
                    viewModel.SelectedReport,
                    viewModel.SelectedCombatantScopeKey,
                    viewModel.SelectedDisplayModeKey,
                    viewModel.SelectedGroupModeKey,
                    viewModel.ReportSearchText,
                    false,
                    cancellationToken);
                playerInformationService.ObserveAndApply(snapshot);
                viewModel.ApplySnapshot(snapshot);
                await TryCaptureLocalPlayerStatsAsync(snapshot, cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception)
        {
            viewModel.SetDisconnected();
            currentFightViewModel?.SetDisconnected();
        }
        finally
        {
            refreshGate.Release();
        }
    }

    private async Task TryCaptureLocalPlayerStatsAsync(
        BridgeSnapshot snapshot,
        CancellationToken cancellationToken)
    {
        if (DateTime.UtcNow < nextAutomaticStatCaptureUtc)
            return;

        var localPlayer = snapshot.Combatants.FirstOrDefault(row =>
            row.IsLocalPlayer &&
            string.Equals(row.CombatantType, "Player", StringComparison.OrdinalIgnoreCase));
        if (localPlayer is null ||
            string.Equals(autoCapturedStatsPlayer, localPlayer.Name, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        // A failed scan is harmless but can be expensive, so retry at a calm pace.
        nextAutomaticStatCaptureUtc = DateTime.UtcNow.AddSeconds(30);
        try
        {
            var result = await bridgeClient.SendCommandAsync(
                "capturestats",
                localPlayer.Name,
                cancellationToken);
            if (result.Success)
            {
                autoCapturedStatsPlayer = localPlayer.Name;
                viewModel.SetUserNotice(
                    $"Detected {localPlayer.Name}'s job and DoT calculation stats automatically.");
                currentFightViewModel?.SetUserNotice(
                    $"Detected {localPlayer.Name}'s job and DoT stats automatically.");
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            // Manual Capture DoT Stats remains available with a detailed error.
        }
    }

    private void UpdateReportColumnHeaders()
    {
        NameColumn.Header = viewModel.NameColumnLabel;
        SecondaryColumn.Header = viewModel.SecondaryColumnLabel;
        PrimaryColumn.Header = viewModel.PrimaryColumnLabel;
        ShareColumn.Header = viewModel.ShareColumnLabel;
        RateColumn.Header = viewModel.RateColumnLabel;
        Detail1Column.Header = viewModel.Detail1ColumnLabel;
        Detail2Column.Header = viewModel.Detail2ColumnLabel;
        Detail3Column.Header = viewModel.Detail3ColumnLabel;
        Detail4Column.Header = viewModel.Detail4ColumnLabel;
        TotalRowDefinition.Height = viewModel.ShowTotalRow ? new GridLength(40) : new GridLength(0);
        FooterRowDefinition.Height = viewModel.ShowSelectedFooter ? new GridLength(56) : new GridLength(0);
        RankColumn.Visibility = viewModel.IsChatSelected ? Visibility.Collapsed : Visibility.Visible;
        SecondaryColumn.Visibility = ColumnVisibility(viewModel.SecondaryColumnLabel);
        PrimaryColumn.Visibility = ColumnVisibility(viewModel.PrimaryColumnLabel);
        ShareColumn.Visibility = ColumnVisibility(viewModel.ShareColumnLabel);
        RateColumn.Visibility = ColumnVisibility(viewModel.RateColumnLabel);
        Detail1Column.Visibility = ColumnVisibility(viewModel.Detail1ColumnLabel);
        Detail2Column.Visibility = ColumnVisibility(viewModel.Detail2ColumnLabel);
        Detail3Column.Visibility = ColumnVisibility(viewModel.Detail3ColumnLabel);
        Detail4Column.Visibility = ColumnVisibility(viewModel.Detail4ColumnLabel);
        AccuracyColumn.Header = viewModel.IsCraftingSelected
            ? "Details"
            : viewModel.IsActionGrouping ? "Result" : "Accuracy";
        AccuracyColumn.Visibility = viewModel.SelectedReport == "damageDealt" ||
                                    viewModel.IsActionGrouping ||
                                    viewModel.IsCraftingSelected ||
                                    (viewModel.SelectedReport == "fights" &&
                                     viewModel.SelectedDisplayModeKey == "performance")
            ? Visibility.Visible
            : Visibility.Collapsed;

        if (viewModel.IsChatSelected)
        {
            NameColumn.Width = new DataGridLength(1.15, DataGridLengthUnitType.Star);
            SecondaryColumn.Width = new DataGridLength(0.85, DataGridLengthUnitType.Star);
            PrimaryColumn.Width = new DataGridLength(1.15, DataGridLengthUnitType.Star);
            Detail1Column.Width = new DataGridLength(5.6, DataGridLengthUnitType.Star);
        }
        else
        {
            NameColumn.Width = new DataGridLength(1.5, DataGridLengthUnitType.Star);
            SecondaryColumn.Width = new DataGridLength(1.15, DataGridLengthUnitType.Star);
            PrimaryColumn.Width = new DataGridLength(1.05, DataGridLengthUnitType.Star);
            Detail1Column.Width = new DataGridLength(1.05, DataGridLengthUnitType.Star);
        }
    }

    private static Visibility ColumnVisibility(string label) =>
        string.IsNullOrWhiteSpace(label) ? Visibility.Collapsed : Visibility.Visible;

    private async Task RunStartupMemoryDetectionAsync()
    {
        try
        {
            var startupState = await bridgeClient.GetSnapshotAsync(lifetime.Token);
            if (startupState.ParserRunning)
            {
                viewModel.SetUserNotice("Automatic memory detection skipped because the parser is already running.");
                return;
            }

            await ExecuteEngineCommandAsync("detect");
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            viewModel.SetUserNotice("Automatic memory detection could not run: " + ex.Message);
        }
    }

    private async void OpenCurrentFight_Click(object sender, RoutedEventArgs e)
    {
        OpenCurrentFightWindow();
        await RefreshSnapshotAsync(lifetime.Token);
    }

    private void OpenCurrentFightWindow()
    {
        if (currentFightWindow is not null)
        {
            if (!currentFightWindow.IsVisible)
                currentFightWindow.Show();

            if (currentFightWindow.WindowState == WindowState.Minimized)
                currentFightWindow.WindowState = WindowState.Normal;

            currentFightWindow.Activate();
            return;
        }

        currentFightViewModel = new CurrentFightViewModel(
            settings.CurrentFightCombatantScope,
            settings.CurrentFightView,
            settings.CurrentFightAlwaysOnTop,
            settings.CurrentFightCompactMode,
            settings.CurrentFightBackgroundTransparencyPercent);
        currentFightViewModel.ScopeChanged += CurrentFightViewModel_ScopeChanged;
        currentFightViewModel.CompactModeChanged += CurrentFightViewModel_CompactModeChanged;
        currentFightViewModel.EngineCommandRequested += CurrentFightViewModel_EngineCommandRequested;

        currentFightWindow = new CurrentFightWindow(currentFightViewModel);
        ThemeService.Apply(currentFightWindow, settings.IsLightMode);
        currentFightWindow.SaveBuildRequested += CurrentFightWindow_SaveBuildRequested;
        currentFightWindow.Closed += CurrentFightWindow_Closed;
        ConfigureMonitorWindow(currentFightWindow, currentFightViewModel.IsCompactMode);
        var placement = currentFightViewModel.IsCompactMode
            ? settings.CompactCurrentFightWindow
            : settings.CurrentFightWindow;
        if (!ApplyWindowPlacement(currentFightWindow, placement))
            currentFightWindow.WindowStartupLocation = WindowStartupLocation.CenterScreen;

        settings.CurrentFightOpen = true;
        currentFightWindow.Show();
        settingsService.TrySave(settings, out _);
    }

    private async void CurrentFightViewModel_ScopeChanged(object? sender, EventArgs e)
    {
        if (currentFightViewModel is not null)
        {
            settings.CurrentFightCombatantScope = currentFightViewModel.SelectedCombatantScopeKey;
            settings.CurrentFightView = currentFightViewModel.SelectedFightViewKey;
        }

        settingsService.TrySave(settings, out _);
        await RefreshSnapshotAsync(lifetime.Token);
    }

    private void CurrentFightWindow_Closed(object? sender, EventArgs e)
    {
        if (sender is CurrentFightWindow window && currentFightViewModel is not null)
            CaptureMonitorWindowPlacement(window, currentFightViewModel.IsCompactMode);

        if (currentFightViewModel is not null)
        {
            settings.CurrentFightCombatantScope = currentFightViewModel.SelectedCombatantScopeKey;
            settings.CurrentFightView = currentFightViewModel.SelectedFightViewKey;
            settings.CurrentFightAlwaysOnTop = currentFightViewModel.IsAlwaysOnTop;
            settings.CurrentFightCompactMode = currentFightViewModel.IsCompactMode;
            settings.CurrentFightBackgroundTransparencyPercent = currentFightViewModel.BackgroundTransparencyPercent;
            currentFightViewModel.ScopeChanged -= CurrentFightViewModel_ScopeChanged;
            currentFightViewModel.CompactModeChanged -= CurrentFightViewModel_CompactModeChanged;
            currentFightViewModel.EngineCommandRequested -= CurrentFightViewModel_EngineCommandRequested;
        }

        if (currentFightWindow is not null)
        {
            currentFightWindow.SaveBuildRequested -= CurrentFightWindow_SaveBuildRequested;
            currentFightWindow.Closed -= CurrentFightWindow_Closed;
        }

        if (!closingCurrentFightForShutdown)
            settings.CurrentFightOpen = false;

        currentFightWindow = null;
        currentFightViewModel = null;
        settingsService.TrySave(settings, out _);
    }

    private void CurrentFightViewModel_CompactModeChanged(object? sender, EventArgs e)
    {
        if (currentFightWindow is null || currentFightViewModel is null)
            return;

        var oldBounds = currentFightWindow.WindowState == WindowState.Normal
            ? new Rect(
                currentFightWindow.Left,
                currentFightWindow.Top,
                currentFightWindow.Width,
                currentFightWindow.Height)
            : currentFightWindow.RestoreBounds;

        CaptureMonitorWindowPlacement(currentFightWindow, !currentFightViewModel.IsCompactMode);
        currentFightWindow.WindowState = WindowState.Normal;
        ConfigureMonitorWindow(currentFightWindow, currentFightViewModel.IsCompactMode);

        var placement = currentFightViewModel.IsCompactMode
            ? settings.CompactCurrentFightWindow
            : settings.CurrentFightWindow;
        if (!ApplyWindowPlacement(currentFightWindow, placement))
        {
            currentFightWindow.WindowStartupLocation = WindowStartupLocation.Manual;
            currentFightWindow.Left = oldBounds.Left;
            currentFightWindow.Top = oldBounds.Top;
            KeepWindowVisible(currentFightWindow);
        }

        settings.CurrentFightCompactMode = currentFightViewModel.IsCompactMode;
        settingsService.TrySave(settings, out _);
    }

    private void AutoDetectMemory_Click(object sender, RoutedEventArgs e)
    {
        settings.AutoDetectMemoryOnStartup = AutoDetectMemoryMenuItem.IsChecked;
        settingsService.TrySave(settings, out _);
        viewModel.SetUserNotice(settings.AutoDetectMemoryOnStartup
            ? "Memory detection will run automatically the next time KParser starts."
            : "Automatic memory detection is turned off.");
    }

    private async void PlayerInformation_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var players = await bridgeClient.GetSnapshotAsync(
                "all", 0, null, "damageDealt", "players", "sources", "player", lifetime.Token);
            playerInformationService.ObserveAndApply(players);
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested)
        {
            return;
        }
        catch
        {
            // The editor still opens with the player information already observed this session.
        }

        var editor = new PlayerInformationWindow(playerInformationService.GetEntries())
        {
            Owner = this
        };
        ThemeService.Apply(editor, settings.IsLightMode);
        if (editor.ShowDialog() != true)
            return;

        if (!playerInformationService.TrySave(editor.Entries, out var error))
        {
            MessageBox.Show(
                this,
                "Player information could not be saved: " + error,
                "Player Information",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        viewModel.SetUserNotice("Player information saved. Job labels will use your overrides immediately.");
        await RefreshSnapshotAsync(lifetime.Token);
    }

    private void LightMode_Click(object sender, RoutedEventArgs e)
    {
        settings.IsLightMode = LightModeMenuItem.IsChecked;
        ApplyThemeToOpenWindows();
        settingsService.TrySave(settings, out _);
        viewModel.SetUserNotice(settings.IsLightMode
            ? "Light Mode is active."
            : "Dark Mode is active.");
    }

    private void ApplyThemeToOpenWindows()
    {
        ThemeService.Apply(this, settings.IsLightMode);
        if (currentFightWindow is not null)
            ThemeService.Apply(currentFightWindow, settings.IsLightMode);
        if (playerComparisonWindow is not null)
            ThemeService.Apply(playerComparisonWindow, settings.IsLightMode);
    }

    private void CaptureSettings()
    {
        CaptureWindowPlacement(this, settings.MainWindow);
        settings.MainReport = viewModel.SelectedReport;
        settings.MainEncounterKey = viewModel.SelectedEncounterKey;
        settings.MainCombatantScope = viewModel.SelectedCombatantScopeKey;
        settings.MainDisplayMode = viewModel.SelectedDisplayModeKey;
        settings.MainGroupMode = viewModel.SelectedGroupModeKey;
        settings.AutoDetectMemoryOnStartup = AutoDetectMemoryMenuItem.IsChecked;
        settings.IsLightMode = LightModeMenuItem.IsChecked;
        settings.CurrentFightOpen = currentFightWindow is { IsVisible: true };

        if (currentFightWindow is not null && currentFightViewModel is not null)
            CaptureMonitorWindowPlacement(currentFightWindow, currentFightViewModel.IsCompactMode);

        if (currentFightViewModel is not null)
        {
            settings.CurrentFightCombatantScope = currentFightViewModel.SelectedCombatantScopeKey;
            settings.CurrentFightView = currentFightViewModel.SelectedFightViewKey;
            settings.CurrentFightAlwaysOnTop = currentFightViewModel.IsAlwaysOnTop;
            settings.CurrentFightCompactMode = currentFightViewModel.IsCompactMode;
            settings.CurrentFightBackgroundTransparencyPercent = currentFightViewModel.BackgroundTransparencyPercent;
        }
    }

    private void CaptureMonitorWindowPlacement(CurrentFightWindow window, bool compactMode)
    {
        CaptureWindowPlacement(
            window,
            compactMode ? settings.CompactCurrentFightWindow : settings.CurrentFightWindow);
    }

    private static void ConfigureMonitorWindow(CurrentFightWindow window, bool compactMode)
    {
        window.MinWidth = compactMode ? 360 : 720;
        window.MinHeight = compactMode ? 205 : 360;
        window.Title = compactMode
            ? "KParser - Live Monitor (Compact)"
            : "KParser - Live Monitor";
    }

    private static void KeepWindowVisible(Window window)
    {
        var desktopLeft = SystemParameters.VirtualScreenLeft;
        var desktopTop = SystemParameters.VirtualScreenTop;
        var desktopRight = desktopLeft + SystemParameters.VirtualScreenWidth;
        var desktopBottom = desktopTop + SystemParameters.VirtualScreenHeight;

        window.Left = Math.Max(desktopLeft, Math.Min(window.Left, desktopRight - window.Width));
        window.Top = Math.Max(desktopTop, Math.Min(window.Top, desktopBottom - window.Height));
    }

    private static bool ApplyWindowPlacement(Window window, WindowPlacementSettings placement)
    {
        if (double.IsFinite(placement.Width) && placement.Width >= window.MinWidth)
            window.Width = placement.Width;

        if (double.IsFinite(placement.Height) && placement.Height >= window.MinHeight)
            window.Height = placement.Height;

        var hasPosition = placement.Left.HasValue && placement.Top.HasValue &&
                          double.IsFinite(placement.Left.Value) &&
                          double.IsFinite(placement.Top.Value);
        if (hasPosition)
        {
            var requestedBounds = new Rect(
                placement.Left!.Value,
                placement.Top!.Value,
                window.Width,
                window.Height);
            var desktopBounds = new Rect(
                SystemParameters.VirtualScreenLeft,
                SystemParameters.VirtualScreenTop,
                SystemParameters.VirtualScreenWidth,
                SystemParameters.VirtualScreenHeight);

            var visibleBounds = Rect.Intersect(desktopBounds, requestedBounds);
            if (!visibleBounds.IsEmpty && visibleBounds.Width >= 80 && visibleBounds.Height >= 80)
            {
                window.WindowStartupLocation = WindowStartupLocation.Manual;
                window.Left = requestedBounds.Left;
                window.Top = requestedBounds.Top;
            }
            else
            {
                hasPosition = false;
            }
        }

        if (placement.Maximized)
            window.WindowState = WindowState.Maximized;

        return hasPosition;
    }

    private static void CaptureWindowPlacement(Window window, WindowPlacementSettings placement)
    {
        var bounds = window.WindowState == WindowState.Normal
            ? new Rect(window.Left, window.Top, window.Width, window.Height)
            : window.RestoreBounds;

        if (!bounds.IsEmpty &&
            double.IsFinite(bounds.Left) &&
            double.IsFinite(bounds.Top) &&
            double.IsFinite(bounds.Width) &&
            double.IsFinite(bounds.Height))
        {
            placement.Left = bounds.Left;
            placement.Top = bounds.Top;
            placement.Width = bounds.Width;
            placement.Height = bounds.Height;
        }

        placement.Maximized = window.WindowState == WindowState.Maximized;
    }

    private void CopyReport_Click(object sender, RoutedEventArgs e)
    {
        if (viewModel.Combatants.Count == 0)
        {
            viewModel.SetUserNotice("There is no report data to copy yet.");
            return;
        }

        try
        {
            Clipboard.SetText(ReportExportService.BuildClipboardReport(viewModel));
            viewModel.SetUserNotice("Current report copied to the clipboard.");
        }
        catch (Exception ex)
        {
            viewModel.SetUserNotice("The report could not be copied: " + ex.Message);
        }
    }

    private void CopySelectedCombatant_Click(object sender, RoutedEventArgs e)
    {
        var text = ReportExportService.BuildSelectedCombatant(viewModel);
        if (string.IsNullOrWhiteSpace(text))
        {
            viewModel.SetUserNotice("Select a combatant before copying.");
            return;
        }

        try
        {
            Clipboard.SetText(text);
            viewModel.SetUserNotice("Selected combatant copied to the clipboard.");
        }
        catch (Exception ex)
        {
            viewModel.SetUserNotice("The combatant summary could not be copied: " + ex.Message);
        }
    }

    private async void SavePlayerSnapshot_Click(object sender, RoutedEventArgs e)
    {
        var selected = viewModel.SelectedCombatant;
        if (!viewModel.CanSavePlayerSnapshot || selected is null)
        {
            viewModel.SetUserNotice("Select a player in a Damage Dealt player-summary view first.");
            return;
        }

        await SavePlayerSnapshotAsync(
            selected,
            this,
            viewModel.SelectedFilterScope,
            viewModel.SelectedFilterBattleId,
            viewModel.SelectedFilterMobName,
            viewModel.SelectedCombatantScopeKey,
            viewModel.CurrentEncounterName);
    }

    private async void CurrentFightWindow_SaveBuildRequested(object? sender, EventArgs e)
    {
        if (currentFightViewModel?.SelectedCombatant is not { } selected)
        {
            currentFightViewModel?.SetUserNotice("Select a player row to save a build snapshot.");
            return;
        }

        await SavePlayerSnapshotAsync(
            selected,
            (Window?)currentFightWindow ?? this,
            currentFightViewModel.SelectedFightViewKey,
            0,
            null,
            currentFightViewModel.SelectedCombatantScopeKey,
            currentFightViewModel.EncounterName);
    }

    private async Task SavePlayerSnapshotAsync(
        CombatantRow selected,
        Window owner,
        string scope,
        int battleId,
        string? mobName,
        string combatantScope,
        string encounterName)
    {
        var suggestedLabel = encounterName == "All Encounters" || encounterName == "All Mob Fights"
            ? "Build test " + DateTime.Now.ToString("yyyy-MM-dd HHmm")
            : encounterName + " " + DateTime.Now.ToString("yyyy-MM-dd HHmm");
        if (owner.WindowState == WindowState.Minimized)
            owner.WindowState = WindowState.Normal;
        owner.Activate();

        var nameWindow = new PlayerSnapshotNameWindow(suggestedLabel)
        {
            Owner = owner
        };
        ThemeService.Apply(nameWindow, settings.IsLightMode);
        if (nameWindow.ShowDialog() != true)
            return;

        try
        {
            viewModel.SetUserNotice("Saving a complete player parse snapshot...");
            currentFightViewModel?.SetUserNotice("Saving a complete player parse snapshot...");
            var sourceSnapshot = await bridgeClient.GetSnapshotAsync(
                scope,
                battleId,
                mobName,
                "damageDealt",
                combatantScope,
                "sources",
                "player",
                lifetime.Token);
            var combatant = sourceSnapshot.Combatants.FirstOrDefault(row =>
                string.Equals(row.Name, selected.Name, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(row.CombatantType, "Player", StringComparison.OrdinalIgnoreCase));
            if (sourceSnapshot.Encounter is null || combatant is null)
            {
                viewModel.SetUserNotice("That player no longer has parse data in the selected fight view.");
                currentFightViewModel?.SetUserNotice("That player no longer has parse data in this fight view.");
                return;
            }

            var encounter = sourceSnapshot.Encounter;
            var durationSeconds = encounter.DurationSeconds;
            if (encounter.IsActive && sourceSnapshot.ParserRunning &&
                DateTime.TryParse(sourceSnapshot.GeneratedUtc, out var generatedUtc))
            {
                durationSeconds += Math.Max(0, (DateTime.UtcNow - generatedUtc.ToUniversalTime()).TotalSeconds);
            }

            var savedSnapshot = new PlayerParseSnapshot
            {
                Label = nameWindow.SnapshotLabel,
                PlayerName = combatant.Name,
                Job = combatant.Job,
                EncounterName = encounter.Name,
                EncounterScope = encounter.Scope,
                FightCount = encounter.FightCount,
                EventCount = encounter.EventCount,
                DurationSeconds = durationSeconds,
                EngineVersion = sourceSnapshot.EngineVersion,
                TotalDamage = combatant.Damage,
                Dps = combatant.Damage / Math.Max(1.0, durationSeconds),
                SharePercent = combatant.SharePercent,
                MeleeDamage = combatant.MeleeDamage,
                RangedDamage = combatant.Ranged,
                WeaponSkillDamage = combatant.WeaponSkillDamage,
                AbilityDamage = combatant.Abilities,
                MagicDamage = combatant.MagicDamage,
                SkillchainDamage = combatant.Skillchains,
                AdditionalEffectDamage = combatant.AdditionalEffects,
                ReactiveDamage = combatant.Counters + combatant.Retaliation + combatant.Spikes,
                PhysicalAttempts = combatant.PhysicalAttempts,
                PhysicalHits = combatant.PhysicalHits,
                PhysicalMisses = combatant.PhysicalMisses,
                CriticalHits = combatant.CriticalHits,
                TopAction = combatant.TopAction
            };
            playerParseService.Save(savedSnapshot);
            playerComparisonWindow?.RefreshSnapshots(savedSnapshot.PlayerName);
            viewModel.SetUserNotice($"Saved '{savedSnapshot.Label}' for {savedSnapshot.PlayerName}. Open Player Build Comparison when ready.");
            currentFightViewModel?.SetUserNotice($"Saved '{savedSnapshot.Label}' for {savedSnapshot.PlayerName}.");
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            viewModel.SetUserNotice("The player parse could not be saved: " + ex.Message);
            currentFightViewModel?.SetUserNotice("The player parse could not be saved: " + ex.Message);
        }
    }

    private void OpenPlayerComparison_Click(object sender, RoutedEventArgs e)
    {
        if (playerComparisonWindow is not null)
        {
            if (playerComparisonWindow.WindowState == WindowState.Minimized)
                playerComparisonWindow.WindowState = WindowState.Normal;
            playerComparisonWindow.RefreshSnapshots(viewModel.SelectedCombatant?.Name);
            playerComparisonWindow.Activate();
            return;
        }

        playerComparisonWindow = new PlayerComparisonWindow(playerParseService)
        {
            Owner = this
        };
        ThemeService.Apply(playerComparisonWindow, settings.IsLightMode);
        playerComparisonWindow.Closed += (_, _) => playerComparisonWindow = null;
        playerComparisonWindow.RefreshSnapshots(viewModel.SelectedCombatant?.Name);
        playerComparisonWindow.Show();
    }

    private void ExportReport_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var dialog = new SaveFileDialog
            {
                Title = "Export KParser report",
                Filter = "CSV files (*.csv)|*.csv|All files (*.*)|*.*",
                DefaultExt = ".csv",
                AddExtension = true,
                OverwritePrompt = true,
                RestoreDirectory = true,
                InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                FileName = ReportExportService.CreateDefaultFileName(viewModel)
            };

            Activate();
            if (dialog.ShowDialog() != true)
                return;

            File.WriteAllText(
                dialog.FileName,
                ReportExportService.BuildCsv(viewModel),
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
            viewModel.SetUserNotice("Report exported to " + dialog.FileName);
        }
        catch (Exception ex)
        {
            viewModel.SetUserNotice("The report could not be exported: " + ex.Message);
            MessageBox.Show(
                this,
                "The report could not be exported.\n\n" + ex.Message,
                "KParser export failed",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private void Exit_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void MainWindowDragArea_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            ToggleMaximizedState();
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
            // The mouse button can be released before WPF begins the drag.
        }
    }

    private void MainMinimize_Click(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState.Minimized;
    }

    private void MainMaximize_Click(object sender, RoutedEventArgs e)
    {
        ToggleMaximizedState();
    }

    private void MainClose_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void ToggleMaximizedState()
    {
        WindowState = WindowState == WindowState.Maximized
            ? WindowState.Normal
            : WindowState.Maximized;
    }
}
