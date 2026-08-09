using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using KParser.Sanctum.UI.Models;

namespace KParser.Sanctum.UI.Services;

internal static class GameChatService
{
    private const uint InputKeyboard = 1;
    private const uint KeyEventKeyUp = 0x0002;
    private const uint KeyEventUnicode = 0x0004;
    private const int RestoreWindow = 9;

    public static async Task<GameChatResult> SendPartySummaryAsync(CombatantRow row)
    {
        var command = BuildPartyCommand(row);
        var gameWindow = FindGameWindow();
        if (gameWindow == IntPtr.Zero)
        {
            return new GameChatResult(
                false,
                command,
                "The FFXI window could not be activated. The party command was copied so you can paste or type it in game.");
        }

        ShowWindow(gameWindow, RestoreWindow);
        if (!SetForegroundWindow(gameWindow))
        {
            return new GameChatResult(
                false,
                command,
                "Windows blocked activation of the FFXI window. The party command was copied instead.");
        }

        await Task.Delay(100);
        if (!SendUnicode(command) || !SendVirtualKey(0x0D))
        {
            return new GameChatResult(
                false,
                command,
                "FFXI was activated, but the keystrokes could not be sent. The party command was copied instead.");
        }

        return new GameChatResult(true, command, "Selected player summary sent to party chat.");
    }

    internal static string BuildPartyCommand(CombatantRow row)
    {
        var name = CleanChatText(row.Name);
        var accuracy = CleanChatText(row.AccuracyDisplay);
        if (string.IsNullOrWhiteSpace(accuracy) || accuracy == "-")
            accuracy = "n/a";

        var command = string.Format(
            System.Globalization.CultureInfo.InvariantCulture,
            "/p {0}: {1:N0} dmg | {2:0.0}% share | {3} acc",
            name,
            row.Damage,
            row.Share,
            accuracy);
        return command.Length <= 140 ? command : command[..140];
    }

    private static string CleanChatText(string value)
    {
        var builder = new StringBuilder(value.Length);
        foreach (var character in value)
        {
            if (character >= 32 && character <= 126 && character != '|')
                builder.Append(character);
        }
        return builder.ToString().Trim();
    }

    private static IntPtr FindGameWindow()
    {
        var processIds = new HashSet<uint>();
        foreach (var process in Process.GetProcesses())
        {
            try
            {
                var name = process.ProcessName;
                if (name.Equals("xiloader", StringComparison.OrdinalIgnoreCase) ||
                    name.Equals("pol", StringComparison.OrdinalIgnoreCase) ||
                    name.Equals("ffximain", StringComparison.OrdinalIgnoreCase))
                {
                    processIds.Add((uint)process.Id);
                    if (process.MainWindowHandle != IntPtr.Zero &&
                        IsWindowVisible(process.MainWindowHandle))
                    {
                        return process.MainWindowHandle;
                    }
                }
            }
            catch
            {
            }
            finally
            {
                process.Dispose();
            }
        }

        var result = IntPtr.Zero;
        EnumWindows((window, _) =>
        {
            GetWindowThreadProcessId(window, out var processId);
            if (processIds.Contains(processId) && IsWindowVisible(window))
            {
                result = window;
                return false;
            }
            return true;
        }, IntPtr.Zero);
        return result;
    }

    private static bool SendUnicode(string text)
    {
        foreach (var character in text)
        {
            var down = CreateUnicodeInput(character, false);
            var up = CreateUnicodeInput(character, true);
            if (SendInput(2, new[] { down, up }, Marshal.SizeOf<Input>()) != 2)
                return false;
        }
        return true;
    }

    private static bool SendVirtualKey(ushort key)
    {
        var down = new Input
        {
            Type = InputKeyboard,
            Data = new InputUnion { Keyboard = new KeyboardInput { VirtualKey = key } }
        };
        var up = down;
        up.Data.Keyboard.Flags = KeyEventKeyUp;
        return SendInput(2, new[] { down, up }, Marshal.SizeOf<Input>()) == 2;
    }

    private static Input CreateUnicodeInput(char character, bool keyUp) => new()
    {
        Type = InputKeyboard,
        Data = new InputUnion
        {
            Keyboard = new KeyboardInput
            {
                ScanCode = character,
                Flags = KeyEventUnicode | (keyUp ? KeyEventKeyUp : 0)
            }
        }
    };

    private delegate bool EnumWindowsCallback(IntPtr window, IntPtr parameter);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsCallback callback, IntPtr parameter);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr window);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr window);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr window, int command);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint inputCount, Input[] inputs, int inputSize);

    [StructLayout(LayoutKind.Sequential)]
    private struct Input
    {
        public uint Type;
        public InputUnion Data;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)] public KeyboardInput Keyboard;
        [FieldOffset(0)] public MouseInput Mouse;
        [FieldOffset(0)] public HardwareInput Hardware;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KeyboardInput
    {
        public ushort VirtualKey;
        public ushort ScanCode;
        public uint Flags;
        public uint Time;
        public UIntPtr ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MouseInput
    {
        public int X;
        public int Y;
        public uint MouseData;
        public uint Flags;
        public uint Time;
        public UIntPtr ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct HardwareInput
    {
        public uint Message;
        public ushort ParameterLow;
        public ushort ParameterHigh;
    }
}

internal sealed record GameChatResult(bool Success, string Command, string Message);
