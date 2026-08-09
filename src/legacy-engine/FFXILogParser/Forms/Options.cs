// Modified for KParser - Sanctum Edition, 2026. See /MODIFICATIONS.md.
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Text;
using System.IO;
using System.Windows.Forms;
using WaywardGamers.KParser.Monitoring;

namespace WaywardGamers.KParser
{
    public partial class Options : Form
    {
        #region Member Variables
        Properties.WindowSettings windowSettings;
        Properties.Settings coreSettings;
        readonly bool parseRunningAtOpen;
        Button detectMemoryOffset;
        Label memoryOffsetDetectionStatus;
        BackgroundWorker memoryOffsetDetectionWorker;

        const string AutomaticDetectionStatus =
            "Invalid saved offsets are detected automatically when parsing starts.";
        #endregion

        #region Constructor
        /// <summary>
        /// Basic constructor.  Main window passes in whether a parse is
        /// running when we start.
        /// </summary>
        /// <param name="isParseRunning"></param>
        public Options(bool isParseRunning)
        {
            InitializeComponent();
            // The inherited Packet option depended on an unfinished ZeroMQ reader.
            // Sanctum supports direct RAM parsing and log parsing only.
            dataSourcePackets.Visible = false;
            dataSourcePackets.Enabled = false;
            parseRunningAtOpen = isParseRunning;
            InitializeMemoryOffsetDetectionControls();

            // Load a local copy of the app settings.
            windowSettings = new WaywardGamers.KParser.Properties.WindowSettings();
            windowSettings.Reload();
            coreSettings = new WaywardGamers.KParser.Properties.Settings();
            coreSettings.Reload();

            // Copy the settings into the form.
            LoadSettingsValues();

            // Disable changing most values if a parse is already running.
            if (isParseRunning == true)
            {
                dataSourceGroup.Enabled = false;
                dataSourceGroup.Text = "Data Source (Cannot change while parse is running)";
            }
            else
            {
                dataSourceGroup.Enabled = true;
                dataSourceGroup.Text = "Data Source";
            }
        }

        #endregion

        #region Properties
        /// <summary>
        /// Gets and sets the source to read data from.
        /// </summary>
        public DataSource DataSource
        {
            get
            {
                if (dataSourceLogs.Checked)
                    return DataSource.Log;
                else if (dataSourceRam.Checked)
                    return DataSource.Ram;
                else
                    return DataSource.Log;
            }
            protected set
            {
                if (value == DataSource.Ram)
                    dataSourceRam.Checked = true;
                else if (value == DataSource.Log)
                    dataSourceLogs.Checked = true;
                else if (value == DataSource.Packet)
                    dataSourceRam.Checked = true;
            }
        }

        /// <summary>
        /// Gets the default memory offset for chat log data within the
        /// FFXI process space.
        /// </summary>
        public uint MemoryOffset
        {
            get
            {
                // For reference: Default at the moment (6/9/08) is 0x00575968

                // Be resilient in parsing the value

                // Clear leading/trailing whitespace
                string tmpMemOffset = memoryOffsetAddress.Text.Trim();

                // If entered as 0x#####, strip the 0x prefix before trying to parse the value.
                if (tmpMemOffset.StartsWith("0x", StringComparison.CurrentCultureIgnoreCase) == true)
                    tmpMemOffset = tmpMemOffset.Substring(2);

                // If entered as #####h, remove the 'h' before trying to parse the value.
                if (tmpMemOffset.EndsWith("h", StringComparison.CurrentCultureIgnoreCase) == true)
                    tmpMemOffset = tmpMemOffset.Substring(0, tmpMemOffset.Length - 1);

                uint result = 0;
                System.Globalization.NumberFormatInfo nfi = System.Globalization.CultureInfo.CurrentCulture.NumberFormat;

                if (uint.TryParse(tmpMemOffset, System.Globalization.NumberStyles.HexNumber, nfi, out result) == true)
                    return result;
                else
                    return 0;

            }
        }

        /// <summary>
        /// If reading from the log directory, indicate whether we want to
        /// read the logs that are already there, or only read new ones as
        /// they come in.
        /// </summary>
        public bool ParseExistingLogs
        {
            get
            {
                return readExistingLogs.Checked;
            }
        }
        #endregion

        #region Event Handlers
        private void Options_Load(object sender, EventArgs e)
        {
            // Adjust the fields that are to be enabled when the form first loads.
            SetEnabledFields();
        }

        private void dataSource_CheckedChanged(object sender, EventArgs e)
        {
            // Adjust the fields that are to be enabled based on this setting.
            var radio = sender as RadioButton;
            if (radio != null)
            {
                if (radio.Checked)
                    SetEnabledFields();
            }
        }

        private void getLogDirectory_Click(object sender, EventArgs e)
        {
            folderBrowserDialog.SelectedPath = logDirectory.Text;

            if (folderBrowserDialog.ShowDialog() == DialogResult.OK)
            {
                logDirectory.Text = folderBrowserDialog.SelectedPath;
            }
        }

        private void getSaveDirectory_Click(object sender, EventArgs e)
        {
            folderBrowserDialog.SelectedPath = defaultSaveDirectory.Text;

            if (folderBrowserDialog.ShowDialog() == DialogResult.OK)
            {
                defaultSaveDirectory.Text = folderBrowserDialog.SelectedPath;
            }
        }

        private void editMemoryAddress_CheckedChanged(object sender, EventArgs e)
        {
            memoryOffsetAddress.ReadOnly = !(editMemoryAddress.Checked);
        }

        private void detectMemoryOffset_Click(object sender, EventArgs e)
        {
            try
            {
                SetMemoryOffsetDetectionBusy(true);
                memoryOffsetDetectionStatus.Text = "Looking for an accessible FFXI client...";
                memoryOffsetDetectionWorker.RunWorkerAsync();
            }
            catch (Exception detectionError)
            {
                ShowMemoryOffsetDetectionError(detectionError);
            }
        }

        private void memoryOffsetDetectionWorker_DoWork(object sender, DoWorkEventArgs e)
        {
            if (e.Argument == null)
                e.Result = Monitor.Instance.FindFFXIClientProcessIds();
            else
                e.Result = Monitor.Instance.DetectMemoryOffset((int)e.Argument);
        }

        private void memoryOffsetDetectionWorker_RunWorkerCompleted(
            object sender, RunWorkerCompletedEventArgs e)
        {
            if (e.Error != null)
            {
                ShowMemoryOffsetDetectionError(e.Error);
                return;
            }

            int[] processIds = e.Result as int[];
            if (processIds != null)
            {
                BeginMemoryOffsetScan(processIds);
                return;
            }

            SetMemoryOffsetDetectionBusy(false);

            try
            {
                uint detectedOffset = (uint)e.Result;

                // Apply only after signature matching and live-structure validation
                // have both succeeded. Saving here preserves the existing setting if
                // detection fails and makes Detect an explicit apply action.
                coreSettings.MemoryOffset = detectedOffset;
                coreSettings.Save();

                memoryOffsetAddress.Text = string.Format("{0:X8}", detectedOffset);
                editMemoryAddress.Checked = false;
                memoryOffsetDetectionStatus.Text = string.Format(
                    "Detected, validated, and saved 0x{0:X8}.", detectedOffset);
            }
            catch (Exception saveError)
            {
                ShowMemoryOffsetDetectionError(saveError);
            }
        }

        private void BeginMemoryOffsetScan(int[] processIds)
        {
            if ((processIds == null) || (processIds.Length == 0))
            {
                SetMemoryOffsetDetectionBusy(false);
                memoryOffsetDetectionStatus.Text = "No accessible FFXI client was found.";
                MessageBox.Show(
                    "No accessible FFXI client was found. Start Sanctum, log into a character, " +
                    "and run KParser at the same elevation as the game client.",
                    "FFXI client not found", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            int selectedProcessId = processIds[0];
            if (processIds.Length > 1)
            {
                // Restore the normal cursor while the existing client selector is shown.
                SetMemoryOffsetDetectionBusy(false);

                using (SelectPOLProcess processSelector = new SelectPOLProcess())
                {
                    if (processSelector.ShowDialog(this) != DialogResult.OK)
                    {
                        memoryOffsetDetectionStatus.Text = AutomaticDetectionStatus;
                        return;
                    }

                    selectedProcessId = processSelector.SelectedPID;
                }

                if (selectedProcessId <= 0)
                {
                    memoryOffsetDetectionStatus.Text = AutomaticDetectionStatus;
                    return;
                }
            }

            SetMemoryOffsetDetectionBusy(true);
            memoryOffsetDetectionStatus.Text = "Scanning and validating FFXI chat memory...";
            memoryOffsetDetectionWorker.RunWorkerAsync(selectedProcessId);
        }

        private void memoryOffsetAddress_KeyPress(object sender, KeyPressEventArgs e)
        {
            // disable
            /*
            if (char.IsDigit(e.KeyChar))
                return;

            if (char.IsControl(e.KeyChar))
                return;

            if (char.IsLetter(e.KeyChar))
            {
                if (((e.KeyChar >= 'a') && (e.KeyChar <= 'f')) ||
                    ((e.KeyChar >= 'A') && (e.KeyChar <= 'F')))
                    return;
            }

            e.Handled = true;
             * */
        }

        private void reset_Click(object sender, EventArgs e)
        {
            // Reset the app settings and refill the window data.
            coreSettings.Reset();
            windowSettings.Reset();
            LoadSettingsValues();
        }

        private void ok_Click(object sender, EventArgs e)
        {
            this.Close();
        }

        private void cancel_Click(object sender, EventArgs e)
        {
            this.Close();
        }

        private void Options_FormClosing(object sender, FormClosingEventArgs e)
        {
            if ((memoryOffsetDetectionWorker != null) &&
                memoryOffsetDetectionWorker.IsBusy)
            {
                e.Cancel = true;
                MessageBox.Show(
                    "Wait for memory-offset detection to finish before closing Options.",
                    "Detection in progress", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            // If user is closing the window, save all data back to the program settings object.
            if ((e.CloseReason == CloseReason.UserClosing) ||
                (e.CloseReason == CloseReason.None))
            {
                if (this.DialogResult == DialogResult.OK)
                {
                    coreSettings.ParseMode = this.DataSource;

                    if (coreSettings.ParseMode == DataSource.Log)
                    {
                        if (Directory.Exists(logDirectory.Text) == true)
                            coreSettings.FFXILogDirectory = logDirectory.Text;
                        else
                        {
                            MessageBox.Show("Specified directory for FFXI log files does not exist.",
                                "Directory does not exist.", MessageBoxButtons.OK, MessageBoxIcon.Error);
                            e.Cancel = true;
                        }
                    }

                    if (coreSettings.ParseMode == DataSource.Ram)
                    {
                        uint memory = this.MemoryOffset;
                        if (memory != 0)
                            coreSettings.MemoryOffset = memory;
                        else
                        {
                            MessageBox.Show("Specified memory offset value is not valid.",
                                "Directory does not exist.", MessageBoxButtons.OK, MessageBoxIcon.Error);
                            e.Cancel = true;
                        }

                        coreSettings.SpecifyPID = specifyPID.Checked;
                    }

                    coreSettings.ParseExistingLogs = this.ParseExistingLogs;

                    coreSettings.DebugMode = this.debugMode.Checked;

                    coreSettings.DefaultParseSaveDirectory = defaultSaveDirectory.Text;

                    windowSettings.NumberOfRecentFilesToDisplay = (int)numberOfRecentFilesUpDown.Value;
                }
            }
        }

        private void Options_FormClosed(object sender, FormClosedEventArgs e)
        {
            // If form closed and user hit OK, save settings.
            if ((e.CloseReason == CloseReason.UserClosing) ||
                (e.CloseReason == CloseReason.None))
            {
                if (this.DialogResult == DialogResult.OK)
                {
                    coreSettings.Save();
                    windowSettings.Save();
                }
            }

            if (memoryOffsetDetectionWorker != null)
            {
                memoryOffsetDetectionWorker.Dispose();
                memoryOffsetDetectionWorker = null;
            }
        }
        #endregion

        #region Private methods
        private void LoadSettingsValues()
        {
            // Put the values from the app settings into the form.
            this.DataSource = coreSettings.ParseMode;
            logDirectory.Text = coreSettings.FFXILogDirectory;
            memoryOffsetAddress.Text = string.Format("{0:X8}", coreSettings.MemoryOffset);

            readExistingLogs.Checked = coreSettings.ParseExistingLogs;
            specifyPID.Checked = coreSettings.SpecifyPID;
            numberOfRecentFilesUpDown.Value = windowSettings.NumberOfRecentFilesToDisplay;

            debugMode.Checked = coreSettings.DebugMode;

            if ((memoryOffsetDetectionStatus != null) &&
                ((memoryOffsetDetectionWorker == null) || !memoryOffsetDetectionWorker.IsBusy))
            {
                memoryOffsetDetectionStatus.Text = AutomaticDetectionStatus;
            }

            // Check for already-specified default save directory that was set
            // in the old location.  If it exists and the new location hasn't
            // been set, use the windowSettings location, otherwise the
            // coreSettings location.  If neither have been set, use the
            // CommonApp location.
            if (coreSettings.DefaultParseSaveDirectory == string.Empty)
            {
                if (windowSettings.DefaultParseSaveDirectory == string.Empty)
                {
                    defaultSaveDirectory.Text = Application.CommonAppDataPath;
                }
                else
                {
                    defaultSaveDirectory.Text = windowSettings.DefaultParseSaveDirectory;
                }
            }
            else
            {
                defaultSaveDirectory.Text = coreSettings.DefaultParseSaveDirectory;
            }
        }

        private void SetEnabledFields()
        {
            // Enable/disable these controls based on whether the
            // option to read from logs is set.
            directoryLabel.Enabled = dataSourceLogs.Checked;
            logDirectory.Enabled = dataSourceLogs.Checked;
            getLogDirectory.Enabled = dataSourceLogs.Checked;
            readExistingLogs.Enabled = dataSourceLogs.Checked;

            // Enable/disable these controls based on whether the
            // option to read from memory is set.
            memoryLabel.Enabled = dataSourceRam.Checked;
            memoryOffsetAddress.Enabled = dataSourceRam.Checked;
            editMemoryAddress.Enabled = dataSourceRam.Checked &&
                !memoryOffsetDetectionWorker.IsBusy;
            detectMemoryOffset.Enabled = dataSourceRam.Checked &&
                !memoryOffsetDetectionWorker.IsBusy;
            memoryOffsetDetectionStatus.Enabled = dataSourceRam.Checked;

            // Direct RAM reading can target a specific FFXI process.
            specifyPID.Enabled = dataSourceRam.Checked;
        }

        private void InitializeMemoryOffsetDetectionControls()
        {
            detectMemoryOffset = new Button();
            detectMemoryOffset.Name = "detectMemoryOffset";
            detectMemoryOffset.Text = "Detect";
            detectMemoryOffset.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            detectMemoryOffset.Size = new Size(
                Math.Max(70, TextRenderer.MeasureText(detectMemoryOffset.Text, Font).Width + 20),
                editMemoryAddress.Height);
            detectMemoryOffset.Location = new Point(
                editMemoryAddress.Left - detectMemoryOffset.Width - 6,
                editMemoryAddress.Top);
            detectMemoryOffset.TabIndex = editMemoryAddress.TabIndex;
            editMemoryAddress.TabIndex++;
            detectMemoryOffset.UseVisualStyleBackColor = true;
            detectMemoryOffset.Click += detectMemoryOffset_Click;

            memoryOffsetAddress.Width = Math.Max(
                90, detectMemoryOffset.Left - memoryOffsetAddress.Left - 6);

            memoryOffsetDetectionStatus = new Label();
            memoryOffsetDetectionStatus.Name = "memoryOffsetDetectionStatus";
            memoryOffsetDetectionStatus.AutoEllipsis = true;
            memoryOffsetDetectionStatus.Location = new Point(
                memoryOffsetAddress.Left, memoryOffsetAddress.Bottom + 5);
            memoryOffsetDetectionStatus.Size = new Size(
                dataSourceGroup.ClientSize.Width - memoryOffsetAddress.Left - 10, 28);
            memoryOffsetDetectionStatus.Anchor =
                AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            memoryOffsetDetectionStatus.Text = AutomaticDetectionStatus;

            dataSourceGroup.Controls.Add(detectMemoryOffset);
            dataSourceGroup.Controls.Add(memoryOffsetDetectionStatus);

            memoryOffsetDetectionWorker = new BackgroundWorker();
            memoryOffsetDetectionWorker.DoWork +=
                memoryOffsetDetectionWorker_DoWork;
            memoryOffsetDetectionWorker.RunWorkerCompleted +=
                memoryOffsetDetectionWorker_RunWorkerCompleted;
        }

        private void SetMemoryOffsetDetectionBusy(bool busy)
        {
            dataSourceGroup.Enabled = !parseRunningAtOpen && !busy;
            reset.Enabled = !busy;
            ok.Enabled = !busy;
            cancel.Enabled = !busy;
            UseWaitCursor = busy;

            if (!busy)
                SetEnabledFields();
        }

        private void ShowMemoryOffsetDetectionError(Exception error)
        {
            SetMemoryOffsetDetectionBusy(false);
            memoryOffsetDetectionStatus.Text = "Detection failed; the saved offset was not changed.";
            Logger.Instance.Log("Memory offset detection", error.Message);
            MessageBox.Show(
                error.Message,
                "Unable to detect memory offset",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
        #endregion
    }
}
