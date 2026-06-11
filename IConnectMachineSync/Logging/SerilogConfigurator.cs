using System.Text;
using IConnectMachineSync.Configuration;
using Serilog;
using Serilog.Core;
using Serilog.Debugging;
using Serilog.Events;

namespace IConnectMachineSync.Logging;

internal static class SerilogConfigurator
{
    private const string LogDirectoryName = "Logs";
    private const string LogFilePattern = "app-.log";

    public static Logger CreateBootstrapLogger() =>
        new LoggerConfiguration().MinimumLevel.Information().WriteTo.Console().CreateLogger();

    public static void Configure(LoggerConfiguration configuration, AppLoggingOptions options)
    {
        var logDirectory = ResolveLogDirectory();
        SetupSelfLog(logDirectory);
        configuration.MinimumLevel.Is(ParseLevel(options.MinimumLevel))
            .Enrich.FromLogContext()
            .Enrich.WithProperty("Application", options.ApplicationName)
            .WriteTo.Console();

        if (options.File.Enabled)
        {
            configuration.WriteTo.File(
                Path.Combine(logDirectory, LogFilePattern),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: options.File.RetainDays,
                encoding: Encoding.UTF8,
                fileSizeLimitBytes: options.File.FileSizeLimitMB * 1024L * 1024L,
                rollOnFileSizeLimit: true,
                shared: true);
        }

        if (options.Seq.Enabled && !string.IsNullOrWhiteSpace(options.Seq.ServerUrl))
        {
            configuration.WriteTo.Seq(
                options.Seq.ServerUrl,
                bufferBaseFilename: Path.Combine(logDirectory, options.Seq.BufferRelativePath),
                period: TimeSpan.FromSeconds(options.Seq.PeriodSeconds));
        }
    }

    private static string ResolveLogDirectory()
    {
        var directory = Path.Combine(AppContext.BaseDirectory, LogDirectoryName);
        Directory.CreateDirectory(directory);
        return directory;
    }

    private static void SetupSelfLog(string directory) =>
        SelfLog.Enable(message =>
        {
            try { File.AppendAllText(Path.Combine(directory, "serilog-selflog.txt"), message); }
            catch { }
        });

    private static LogEventLevel ParseLevel(string level) => level.Trim().ToLowerInvariant() switch
    {
        "verbose" => LogEventLevel.Verbose,
        "debug" => LogEventLevel.Debug,
        "warning" => LogEventLevel.Warning,
        "error" => LogEventLevel.Error,
        "fatal" => LogEventLevel.Fatal,
        _ => LogEventLevel.Information
    };
}
