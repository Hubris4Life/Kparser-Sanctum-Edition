using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Threading;

namespace KParser.Sanctum.UI.Services;

internal static class ApplicationDiagnostics
{
    private static readonly object Gate = new();
    private static bool initialized;
    private static DateTimeOffset nextRecoveryNoticeUtc = DateTimeOffset.MinValue;

    public static string LogDirectoryPath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "KParser Sanctum Modern",
        "Logs");

    public static string ApplicationErrorLogPath { get; } = Path.Combine(
        LogDirectoryPath,
        "application-errors.log");

    public static void Initialize(Application application)
    {
        lock (Gate)
        {
            if (initialized)
                return;
            initialized = true;
        }

        application.DispatcherUnhandledException += DispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += CurrentDomainUnhandledException;
        TaskScheduler.UnobservedTaskException += UnobservedTaskException;
    }

    public static void LogHandledException(string source, Exception exception) =>
        WriteException(source, exception, recovered: true);

    private static void DispatcherUnhandledException(
        object sender,
        DispatcherUnhandledExceptionEventArgs e)
    {
        var recoverable = IsRecoverable(e.Exception) &&
                          Application.Current?.MainWindow is { IsLoaded: true };
        WriteException("WPF dispatcher", e.Exception, recoverable);
        if (!recoverable)
            return;

        // The parser engine runs separately from the dashboard. Recovering a
        // normal UI exception preserves the parse while the affected action or
        // view is abandoned. Startup/XAML construction and fatal runtime
        // exceptions are never swallowed.
        e.Handled = true;
        ShowRecoveryNotice();
    }

    private static void CurrentDomainUnhandledException(
        object sender,
        UnhandledExceptionEventArgs e)
    {
        var exception = e.ExceptionObject as Exception ??
                        new InvalidOperationException(
                            "A non-Exception object reached the unhandled-exception boundary.");
        WriteException("AppDomain", exception, recovered: false);
    }

    private static void UnobservedTaskException(
        object? sender,
        UnobservedTaskExceptionEventArgs e)
    {
        WriteException("Unobserved task", e.Exception, recovered: true);
        e.SetObserved();
    }

    private static bool IsRecoverable(Exception exception)
    {
        if (exception is AggregateException aggregate)
            return aggregate.Flatten().InnerExceptions.All(IsRecoverable);

        return exception is not OutOfMemoryException and
               not StackOverflowException and
               not AccessViolationException and
               not BadImageFormatException and
               not AppDomainUnloadedException;
    }

    private static void ShowRecoveryNotice()
    {
        lock (Gate)
        {
            var now = DateTimeOffset.UtcNow;
            if (now < nextRecoveryNoticeUtc)
                return;
            nextRecoveryNoticeUtc = now.AddMinutes(1);
        }

        try
        {
            var message =
                "KParser recovered from an unexpected interface error. " +
                "The separate parser engine should continue preserving your parse.\n\n" +
                "Details were saved in the application diagnostics log. " +
                "If the affected view remains unstable, close and reopen that view.";
            var owner = Application.Current?.MainWindow;
            if (owner is null)
            {
                MessageBox.Show(
                    message,
                    "KParser recovered",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
            else
            {
                MessageBox.Show(
                    owner,
                    message,
                    "KParser recovered",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
        }
        catch
        {
            // Reporting an already recovered error must not create another one.
        }
    }

    private static void WriteException(
        string source,
        Exception exception,
        bool recovered)
    {
        Trace.TraceError(
            "KParser {0} exception ({1}): {2}",
            source,
            recovered ? "recovered" : "terminating",
            exception);

        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(LogDirectoryPath);
                var entryAssembly = Assembly.GetEntryAssembly();
                var version = entryAssembly?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()
                                  ?.InformationalVersion ??
                              entryAssembly?.GetName().Version?.ToString() ??
                              "unknown";
                var entry = new StringBuilder()
                    .AppendLine("============================================================")
                    .AppendLine($"Time: {DateTimeOffset.Now:O}")
                    .AppendLine($"Source: {source}")
                    .AppendLine($"Recovered: {recovered}")
                    .AppendLine($"Version: {version}")
                    .AppendLine($"Runtime: {Environment.Version}")
                    .AppendLine($"OS: {Environment.OSVersion}")
                    .AppendLine($"Process: {Environment.ProcessId} ({RuntimeInformation.ProcessArchitecture})")
                    .AppendLine(exception.ToString())
                    .AppendLine()
                    .ToString();
                File.AppendAllText(ApplicationErrorLogPath, entry, Encoding.UTF8);
            }
        }
        catch
        {
            // Logging is best effort and must never obscure the original error.
        }
    }
}
