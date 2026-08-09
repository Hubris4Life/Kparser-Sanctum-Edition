using System;
using System.Collections.Generic;
// Modified for KParser - Sanctum Edition, 2026. See /MODIFICATIONS.md.
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading;
using WaywardGamers.KParser.Monitoring.Memory;

namespace WaywardGamers.KParser.Monitoring
{
    internal static class ProcessAccess
    {
        private static readonly string[] KnownFFXIProcessNames =
        {
            "xiloader",
            "horizon-loader",
            "pol"
        };

        /// <summary>
        /// Returns accessible FFXI client processes. A process is only accepted when
        /// FFXiMain.dll is loaded; matching a launcher name alone is not sufficient.
        /// </summary>
        internal static Process[] FindFFXIProcesses()
        {
            Dictionary<int, Process> candidates = new Dictionary<int, Process>();

            foreach (string processName in KnownFFXIProcessNames)
            {
                try
                {
                    foreach (Process process in Process.GetProcessesByName(processName))
                    {
                        AddIfFFXIProcess(candidates, process);
                    }
                }
                catch (Exception e)
                {
                    Logger.Instance.Log("Process discovery", e.Message);
                }
            }

            // Private-server loaders occasionally use a custom filename. Fall back to
            // inspecting accessible 32-bit processes when the known names find nothing.
            if (candidates.Count == 0)
            {
                foreach (Process process in Process.GetProcesses())
                {
                    AddIfFFXIProcess(candidates, process);
                }
            }

            return candidates.Values.OrderBy(process => process.Id).ToArray();
        }

        /// <summary>
        /// Returns only the process IDs for accessible FFXI clients. The temporary
        /// Process wrappers created during discovery are disposed before returning.
        /// </summary>
        internal static int[] FindFFXIProcessIds()
        {
            Process[] processes = FindFFXIProcesses();

            try
            {
                return processes.Select(process => process.Id).ToArray();
            }
            finally
            {
                foreach (Process process in processes)
                {
                    if (process != null)
                        process.Dispose();
                }
            }
        }

        private static void AddIfFFXIProcess(Dictionary<int, Process> candidates, Process process)
        {
            if (process == null)
                return;

            if (candidates.ContainsKey(process.Id))
            {
                process.Dispose();
                return;
            }

            IntPtr baseAddress;
            int moduleSize;
            if (TryGetFFXIModule(process, out baseAddress, out moduleSize))
                candidates.Add(process.Id, process);
            else
                process.Dispose();
        }

        /// <summary>
        /// Locates FFXiMain.dll in a candidate process and returns the information
        /// needed by the RAM reader.
        /// </summary>
        internal static bool TryGetFFXIModule(Process process, out IntPtr baseAddress, out int moduleSize)
        {
            baseAddress = IntPtr.Zero;
            moduleSize = 0;

            if (process == null)
                return false;

            try
            {
                foreach (ProcessModule module in process.Modules)
                {
                    if (string.Equals(module.ModuleName, "ffximain.dll", StringComparison.OrdinalIgnoreCase))
                    {
                        baseAddress = module.BaseAddress;
                        moduleSize = module.ModuleMemorySize;
                        return (baseAddress != IntPtr.Zero) && (moduleSize > 0);
                    }
                }
            }
            catch
            {
                // Access-denied, exited, and cross-bitness processes are not candidates.
            }

            return false;
        }

        /// <summary>
        /// Opens one specific FFXI client without waiting or displaying UI. This is
        /// used by explicit actions such as the Options dialog's offset detector.
        /// </summary>
        internal static POL OpenFFXIProcess(int processId)
        {
            if (processId <= 0)
                throw new ArgumentOutOfRangeException("processId");

            Process process = null;

            try
            {
                process = Process.GetProcessById(processId);

                IntPtr baseAddress;
                int moduleSize;
                if (!TryGetFFXIModule(process, out baseAddress, out moduleSize))
                {
                    throw new InvalidOperationException(
                        "The selected process does not contain an accessible FFXiMain.dll. " +
                        "Run KParser at the same elevation as the game client.");
                }

                return new POL(process, baseAddress, moduleSize);
            }
            catch (ArgumentException e)
            {
                if (process != null)
                    process.Dispose();

                throw new InvalidOperationException(
                    "The selected FFXI client is no longer running.", e);
            }
            catch
            {
                if (process != null)
                    process.Dispose();

                throw;
            }
        }

        /// <summary>
        /// This function searches the computer processes to locate the FFXI process.
        /// If a particular process ID is specified, it will restrict its search to
        /// that process.
        /// </summary>
        /// <param name="polPID">Optional process ID.  Set to 0 to find the first
        /// instance of FFXI on the computer.</param>
        /// <param name="_abort">A resettable event that can be set to indicate
        /// that the attempt to monitor is being aborted.
        /// Passing a null will cause this to loop forever until the process is found.
        /// Only do so if calling from debuggable code, never from production code.</param>
        /// <returns>Returns a POL object containing the process information needed,
        /// or null if no process was found and the request was aborted.</returns>
        internal static POL GetFFXIProcess(int polPID, ManualResetEvent _abort)
        {
#if DEBUG
            
#else
            if (_abort == null)
                throw new ArgumentNullException("_abort");
#endif


            // Keep going as long as we're still attempting to monitor
            while ((_abort == null) || (!_abort.WaitOne(0)))
            {
                try
                {
                    Trace.WriteLine(Thread.CurrentThread.Name + ": Attempting to connect to Final Fantasy.");

                    // If we're given a specific process to connect to, try for that.
                    if (polPID != 0)
                    {
                        Process process = Process.GetProcessById(polPID);
                        IntPtr baseAddress;
                        int moduleSize;

                        if (!TryGetFFXIModule(process, out baseAddress, out moduleSize))
                        {
                            throw new InvalidOperationException(
                                "The selected process does not contain FFXiMain.dll or cannot be accessed. " +
                                "Run KParser at the same elevation as the game client.");
                        }

                        Trace.WriteLine(string.Format("Module: FFXiMain.dll  Base Address: 0x{0:X8}", (uint)baseAddress));
                        return new POL(process, baseAddress, moduleSize);
                    }
                    else
                    {
                        foreach (Process process in FindFFXIProcesses())
                        {
                            IntPtr baseAddress;
                            int moduleSize;
                            if (TryGetFFXIModule(process, out baseAddress, out moduleSize))
                            {
                                Trace.WriteLine(string.Format("Module: FFXiMain.dll  Base Address: 0x{0:X8}", (uint)baseAddress));
                                return new POL(process, baseAddress, moduleSize);
                            }
                        }
                    }
                }
                catch (ArgumentException e)
                {
                    System.Windows.Forms.MessageBox.Show(e.Message,
                        "Process not found", System.Windows.Forms.MessageBoxButtons.OK);
                }
                catch (InvalidOperationException e)
                {
                    System.Windows.Forms.MessageBox.Show(e.Message,
                        Resources.PublicResources.Error, System.Windows.Forms.MessageBoxButtons.OK);
                }
                catch (Exception e)
                {
                    Logger.Instance.Log("Memory access", String.Format(Thread.CurrentThread.Name + ": ERROR: An exception occured while trying to connect to Final Fantasy.  Message = {0}", e.Message));
                }

                // Wait before trying again.
                System.Threading.Thread.Sleep(5000);
            }

            // If we got here, attempt to acquire process was aborted.  Return null.
            return null;
        }

    }
}
