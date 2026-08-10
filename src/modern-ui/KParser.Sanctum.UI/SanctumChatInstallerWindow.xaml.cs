using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using KParser.Sanctum.UI.Services;
using Microsoft.Win32;

namespace KParser.Sanctum.UI;

public partial class SanctumChatInstallerWindow : Window
{
    private readonly SanctumChatInstallerService installerService;
    private readonly ObservableCollection<SanctumChatInstallLocation> locations = new();
    private string? preferredPath;

    internal SanctumChatInstallerWindow(
        SanctumChatInstallerService installerService,
        string? preferredPath)
    {
        this.installerService = installerService;
        this.preferredPath = preferredPath;
        InitializeComponent();
        LocationComboBox.ItemsSource = locations;
        BundledVersionText.Text = installerService.IsBundledAddonAvailable
            ? installerService.BundledVersion
            : "missing";
        Loaded += (_, _) => RefreshLocations(preferredPath);
    }

    internal string? SelectedAshitaRoot =>
        (LocationComboBox.SelectedItem as SanctumChatInstallLocation)?.AshitaRoot;

    private void RefreshLocations(string? selectPath = null)
    {
        try
        {
            var detected = installerService.DetectInstallations(selectPath ?? preferredPath);
            locations.Clear();
            foreach (var location in detected)
                locations.Add(location);

            var selected = string.IsNullOrWhiteSpace(selectPath)
                ? locations.FirstOrDefault()
                : locations.FirstOrDefault(location =>
                    string.Equals(
                        location.AshitaRoot,
                        TryNormalizeRoot(selectPath),
                        StringComparison.OrdinalIgnoreCase)) ?? locations.FirstOrDefault();
            LocationComboBox.SelectedItem = selected;
            UpdateSelectedLocation();

            if (locations.Count == 0)
                StatusText.Text = "No Ashita v4 location was detected. Click Browse and select its main folder or addons folder.";
        }
        catch (Exception ex)
        {
            StatusText.Text = "Ashita detection failed: " + ex.Message;
        }
    }

    private void AddOrSelectLocation(SanctumChatInstallLocation location)
    {
        var existing = locations.FirstOrDefault(item =>
            string.Equals(item.AshitaRoot, location.AshitaRoot, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
            locations.Remove(existing);
        locations.Insert(0, location);
        LocationComboBox.SelectedItem = location;
        preferredPath = location.AshitaRoot;
        UpdateSelectedLocation();
    }

    private void UpdateSelectedLocation()
    {
        if (LocationComboBox.SelectedItem is not SanctumChatInstallLocation location)
        {
            InstalledStatusText.Text = "No location selected";
            InstallFolderText.Text = "—";
            InstallButton.IsEnabled = false;
            RemoveButton.IsEnabled = false;
            return;
        }

        InstalledStatusText.Text = location.IsInstalled
            ? $"Installed version {location.InstalledVersion}"
            : "Not installed";
        InstallFolderText.Text = location.AddonDirectory;
        InstallButton.IsEnabled = installerService.IsBundledAddonAvailable;
        RemoveButton.IsEnabled = location.IsInstalled;
        preferredPath = location.AshitaRoot;
    }

    private void Browse_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog
        {
            Title = "Select the Ashita v4 folder or its addons folder",
            Multiselect = false
        };
        if (!string.IsNullOrWhiteSpace(preferredPath) && Directory.Exists(preferredPath))
            dialog.InitialDirectory = preferredPath;

        if (dialog.ShowDialog(this) != true)
            return;

        try
        {
            AddOrSelectLocation(installerService.InspectPath(dialog.FolderName));
            StatusText.Text = "Ashita location selected. Nothing has been installed yet.";
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                this,
                ex.Message,
                "Ashita location not recognized",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private void Refresh_Click(object sender, RoutedEventArgs e) =>
        RefreshLocations(SelectedAshitaRoot);

    private void LocationComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e) =>
        UpdateSelectedLocation();

    private void Install_Click(object sender, RoutedEventArgs e)
    {
        if (LocationComboBox.SelectedItem is not SanctumChatInstallLocation location)
            return;

        try
        {
            var result = installerService.InstallOrUpdate(location.AshitaRoot);
            AddOrSelectLocation(result.Location);
            StatusText.Text = result.BackupDirectory is null
                ? "SanctumChat was installed successfully. Load it in game with /addon load sanctumchat."
                : "SanctumChat was updated successfully. The previous version was preserved at " + result.BackupDirectory;
            MessageBox.Show(
                this,
                "SanctumChat is ready.\n\nIn game, run:\n/addon load sanctumchat\n\nThen use /sanctumchat status to confirm the server registration.",
                "SanctumChat installed",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            StatusText.Text = "Installation failed: " + ex.Message;
            MessageBox.Show(
                this,
                "SanctumChat could not be installed.\n\n" + ex.Message,
                "SanctumChat installation failed",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private void Remove_Click(object sender, RoutedEventArgs e)
    {
        if (LocationComboBox.SelectedItem is not SanctumChatInstallLocation location)
            return;

        var confirmation = MessageBox.Show(
            this,
            "Remove SanctumChat from this Ashita installation?\n\nThe folder will be renamed and preserved so it can be recovered.",
            "Remove SanctumChat",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning,
            MessageBoxResult.No);
        if (confirmation != MessageBoxResult.Yes)
            return;

        try
        {
            var recoveryPath = installerService.MoveInstalledAddonAside(location.AshitaRoot);
            AddOrSelectLocation(installerService.InspectPath(location.AshitaRoot));
            StatusText.Text = "SanctumChat was removed. Its previous files were preserved at " + recoveryPath;
        }
        catch (Exception ex)
        {
            StatusText.Text = "Removal failed: " + ex.Message;
            MessageBox.Show(
                this,
                ex.Message,
                "SanctumChat removal failed",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed)
            return;
        try { DragMove(); } catch (InvalidOperationException) { }
    }

    private static string? TryNormalizeRoot(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return null;
        try
        {
            var fullPath = Path.GetFullPath(path);
            if (string.Equals(Path.GetFileName(fullPath), "addons", StringComparison.OrdinalIgnoreCase))
                return Directory.GetParent(fullPath)?.FullName ?? fullPath;
            if (string.Equals(Path.GetFileName(fullPath), "sanctumchat", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(Path.GetFileName(Directory.GetParent(fullPath)?.FullName), "addons", StringComparison.OrdinalIgnoreCase))
            {
                return Directory.GetParent(Directory.GetParent(fullPath)!.FullName)?.FullName ?? fullPath;
            }
            return fullPath;
        }
        catch (Exception)
        {
            return null;
        }
    }
}
