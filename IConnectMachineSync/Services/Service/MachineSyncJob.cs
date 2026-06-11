using IConnectMachineSync.Configuration;
using IConnectMachineSync.Services.Interface;
using Microsoft.Extensions.Options;

namespace IConnectMachineSync.Services.Service;

public sealed class MachineSyncJob(
    IIConnectClient iConnectClient,
    IEquipmentApiClient apiClient,
    IExcelService excelService,
    IOptions<AppOptions> options,
    ILogger<MachineSyncJob> logger) : IJob
{
    private readonly AppOptions _options = options.Value;
    private readonly Dictionary<string, string> _lastSuccessfulStatuses = new(StringComparer.OrdinalIgnoreCase);
    private bool _firstRun = true;

    public async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        ValidateApiCredentials();
        var batch = await iConnectClient.ReadMachinesAsync(cancellationToken);
        logger.LogInformation("Read {Count} machines from i-Connect at {CapturedAt}.", batch.Machines.Count, batch.CapturedAt);

        if (_options.ExcelExport.Enabled)
        {
            var output = await excelService.ExportAsync(batch, _options.ExcelExport.OutputDirectory, cancellationToken);
            logger.LogInformation("Exported Excel workbook: {Output}.", output);
        }

        var changed = MachineDataFunctions.ChangedMachines(
            batch.Machines,
            _lastSuccessfulStatuses,
            _options.Sync.EquipmentPrefix,
            _firstRun && _options.Sync.SendStartupFullSync);

        var successCount = 0;
        var failureCount = 0;
        foreach (var machine in changed)
        {
            var equipmentNo = MachineDataFunctions.BuildEquipmentNo(_options.Sync.EquipmentPrefix, machine.MachineName);
            try
            {
                await apiClient.SendStatusAsync(machine, equipmentNo, batch.CapturedAt, cancellationToken);
                _lastSuccessfulStatuses[equipmentNo] = machine.MachineCondition;
                successCount++;
            }
            catch (Exception ex)
            {
                failureCount++;
                logger.LogError(ex, "Status synchronization failed for {EquipmentNo}; it will be retried next cycle.", equipmentNo);
            }
        }

        _firstRun = false;
        logger.LogInformation(
            "Machine sync cycle completed. Success: {SuccessCount}; failed: {FailureCount}; skipped unchanged: {SkippedCount}.",
            successCount,
            failureCount,
            batch.Machines.Count - changed.Count);
    }

    private void ValidateApiCredentials()
    {
        if (string.IsNullOrEmpty(_options.Api.Account) || string.IsNullOrEmpty(_options.Api.Password))
            throw new InvalidOperationException("Api:Account and Api:Password are empty. Fill them in appsettings.json before starting synchronization.");
    }
}
