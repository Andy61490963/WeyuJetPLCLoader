using IConnectMachineSync.Configuration;
using IConnectMachineSync.Logging;
using IConnectMachineSync.Services.Infrastructure;
using IConnectMachineSync.Services.Interface;
using IConnectMachineSync.Services.Processing;
using IConnectMachineSync.Services.Service;
using Microsoft.Extensions.Options;
using Serilog;

namespace IConnectMachineSync;

public class Program
{
    public static async Task Main(string[] args)
    {
        Log.Logger = SerilogConfigurator.CreateBootstrapLogger();
        try
        {
            var builder = Host.CreateApplicationBuilder(args);
            if (args.Any(arg => string.Equals(arg, "--no-excel", StringComparison.OrdinalIgnoreCase)))
                builder.Configuration["ExcelExport:Enabled"] = "false";
            builder.Services.AddWindowsService(options => options.ServiceName = "IConnect Machine Sync");
            builder.Services.Configure<AppOptions>(builder.Configuration);

            var loggingOptions = builder.Configuration.GetSection("Logging").Get<AppLoggingOptions>() ?? new AppLoggingOptions();
            builder.Services.AddSerilog(configuration => SerilogConfigurator.Configure(configuration, loggingOptions));

            builder.Services.AddSingleton<ISqlConnectionFactory, SqlConnectionFactory>();
            builder.Services.AddSingleton<IDapperRepository, DapperRepository>();
            builder.Services.AddSingleton<IExcelService, ExcelService>();
            builder.Services.AddSingleton<IIConnectClient, IConnectClient>();
            builder.Services.AddHttpClient<IEquipmentApiClient, EquipmentApiClient>((services, client) =>
            {
                var options = services.GetRequiredService<IOptions<AppOptions>>().Value.Api;
                client.BaseAddress = new Uri(options.BaseUrl.EndsWith('/') ? options.BaseUrl : $"{options.BaseUrl}/");
                client.Timeout = TimeSpan.FromSeconds(30);
            });
            builder.Services.AddSingleton<IJob, MachineSyncJob>();

            var seedOnly = args.Any(arg => string.Equals(arg, "--seed-only", StringComparison.OrdinalIgnoreCase));
            var scrapeOnce = args.Any(arg => string.Equals(arg, "--scrape-once", StringComparison.OrdinalIgnoreCase));
            var runOnce = args.Any(arg => string.Equals(arg, "--run-once", StringComparison.OrdinalIgnoreCase));
            if (!seedOnly && !scrapeOnce && !runOnce) builder.Services.AddHostedService<Worker>();

            using var host = builder.Build();
            if (seedOnly)
            {
                await SeedMachinesAsync(host.Services);
                return;
            }
            if (scrapeOnce)
            {
                await ScrapeOnceAsync(host.Services);
                return;
            }
            await SeedMachinesAsync(host.Services);
            if (runOnce)
            {
                await host.Services.GetRequiredService<IJob>().ExecuteAsync(CancellationToken.None);
                return;
            }

            await host.RunAsync();
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "Application terminated unexpectedly.");
            WriteStartupFatalLog(ex);
            Environment.ExitCode = 1;
        }
        finally
        {
            await Log.CloseAndFlushAsync();
        }
    }

    private static void WriteStartupFatalLog(Exception exception)
    {
        try
        {
            var directory = Path.Combine(AppContext.BaseDirectory, "Logs");
            Directory.CreateDirectory(directory);
            File.AppendAllText(
                Path.Combine(directory, "startup-fatal.log"),
                $"{DateTimeOffset.Now:O}{Environment.NewLine}{exception}{Environment.NewLine}{Environment.NewLine}");
        }
        catch
        {
        }
    }

    private static async Task ScrapeOnceAsync(IServiceProvider services)
    {
        var options = services.GetRequiredService<IOptions<AppOptions>>().Value;
        var logger = services.GetRequiredService<ILogger<Program>>();
        var client = services.GetRequiredService<IIConnectClient>();
        var batch = await client.ReadMachinesAsync(CancellationToken.None);
        logger.LogInformation("Scrape completed. Machines: {Machines}.", batch.Machines.Count);
        if (options.ExcelExport.Enabled)
        {
            var excel = services.GetRequiredService<IExcelService>();
            var output = await excel.ExportAsync(batch, options.ExcelExport.OutputDirectory, CancellationToken.None);
            logger.LogInformation("Excel exported: {Output}.", output);
        }
    }

    private static async Task SeedMachinesAsync(IServiceProvider services)
    {
        var options = services.GetRequiredService<IOptions<AppOptions>>().Value;
        var logger = services.GetRequiredService<ILogger<Program>>();
        if (!options.MachineSeed.Enabled)
        {
            logger.LogInformation("MachineSeed is disabled; nothing to do.");
            return;
        }

        var excel = services.GetRequiredService<IExcelService>();
        var repository = services.GetRequiredService<IDapperRepository>();
        var machines = excel.ReadMachines(options.MachineSeed.SourceExcelPath);
        var inserted = await repository.SeedMachinesAsync(machines, options.Sync.EquipmentPrefix, CancellationToken.None);
        logger.LogInformation("Machine seed completed. Source rows: {Rows}; inserted: {Inserted}.", machines.Count, inserted);
    }
}
