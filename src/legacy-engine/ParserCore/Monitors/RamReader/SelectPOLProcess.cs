using System;
using System.Collections.Generic;
// Modified for KParser - Sanctum Edition, 2026. See /MODIFICATIONS.md.
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using System.Diagnostics;

namespace WaywardGamers.KParser.Monitoring
{
    public partial class SelectPOLProcess : Form
    {
        #region Construction
        public SelectPOLProcess()
        {
            InitializeComponent();
            Text = "Select FFXI Client";
            processList.HorizontalScrollbar = true;
            Disposed += SelectPOLProcess_Disposed;
        }

        Process[] polProcesses = new Process[0];
        #endregion

        #region Properties
        public int SelectedPID
        {
            get
            {
                if (polProcesses.Length == 0)
                    return 0;

                if (processList.SelectedIndex < 0)
                    return 0;

                Process selectedProcess = polProcesses[processList.SelectedIndex];

                if (selectedProcess != null)
                    return selectedProcess.Id;

                return 0;
            }
        }
        #endregion

        #region Event handlers - initializing
        private void SelectPOLProcess_Load(object sender, EventArgs e)
        {
            PopulateProcessList();
        }

        private void refresh_Click(object sender, EventArgs e)
        {
            PopulateProcessList();
        }

        private void PopulateProcessList()
        {
            DisposeProcessHandles();
            processList.Items.Clear();

            polProcesses = ProcessAccess.FindFFXIProcesses();

            ok.Enabled = (polProcesses.Length > 0);

            if (ok.Enabled == true)
            {
                foreach (var proc in polProcesses)
                {
                    string windowTitle = string.Empty;
                    try
                    {
                        windowTitle = proc.MainWindowTitle;
                    }
                    catch
                    {
                    }

                    if (string.IsNullOrEmpty(windowTitle))
                        windowTitle = "FFXI client";

                    processList.Items.Add(
                        string.Format("{0}  |  {1}.exe  |  PID {2}", windowTitle, proc.ProcessName, proc.Id));
                }

                processList.SelectedIndex = 0;
            }
            else
            {
                processList.Items.Add("No client found. Launch Sanctum, then click Refresh.");
                processList.SelectedIndex = -1;
            }
        }

        private void SelectPOLProcess_Disposed(object sender, EventArgs e)
        {
            DisposeProcessHandles();
        }

        private void DisposeProcessHandles()
        {
            foreach (Process process in polProcesses)
            {
                if (process != null)
                    process.Dispose();
            }

            polProcesses = new Process[0];
        }
        #endregion

        #region Event handlers - closing
        private void processList_MouseDoubleClick(object sender, MouseEventArgs e)
        {
            this.DialogResult = DialogResult.OK;
            this.Close();
        }

        private void ok_MouseClick(object sender, MouseEventArgs e)
        {
            this.Close();
        }

        private void cancel_MouseClick(object sender, MouseEventArgs e)
        {
            this.Close();
        }
        #endregion
    }
}
