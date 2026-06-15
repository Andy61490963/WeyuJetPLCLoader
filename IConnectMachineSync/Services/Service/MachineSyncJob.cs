using IConnectMachineSync.Configuration;
using IConnectMachineSync.Services.Interface;
using Microsoft.Extensions.Options;

namespace IConnectMachineSync.Services.Service;

public sealed class MachineSyncJob(
    IIConnectClient iConnectClient,
    IEquipmentApiClient apiClient,
    IExcelService excelService,
    IDapperRepository repository,
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

        // Keep EQM_MASTER aligned with the latest i-Connect screen even when the machine status
        // did not change enough to trigger a cloud API call.
        var updatedMasterRows = await repository.UpdateMachineCurrentFieldsAsync(batch.Machines, _options.Sync.EquipmentPrefix, cancellationToken);
        logger.LogInformation("Updated EQM_MASTER current fields for {UpdatedRows} machines.", updatedMasterRows);

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
                // The API inserts EQM_STATUS_CHANGE_HIST by DATA_LINK_SID. We use that SID right
                // after a successful call to enrich the same history row with raw i-Connect ch1-ch4 values.
                var dataLinkSid = await apiClient.SendStatusAsync(machine, equipmentNo, batch.CapturedAt, cancellationToken);
                await repository.UpdateHistoryCustomFieldsAsync(dataLinkSid, machine, cancellationToken);
                _lastSuccessfulStatuses[equipmentNo] = machine.MachineCondition;
                successCount++;
            }
            catch (Exception ex)
            {
                failureCount++;
                logger.LogError(ex, "Status synchronization failed for {EquipmentNo}; it will be retried next cycle.", equipmentNo);
            }
        }

        var qtySuccessCount = 0;
        var qtyFailureCount = 0;
        var qtySkippedCount = 0;
        if (_options.Sync.SendQtyAutoDc)
        {
            foreach (var machine in batch.Machines)
            {
                var equipmentNo = MachineDataFunctions.BuildEquipmentNo(_options.Sync.EquipmentPrefix, machine.MachineName);
                var quantity = MachineDataFunctions.ParseShotCount(machine.Channel3);
                if (quantity is null)
                {
                    qtySkippedCount++;
                    logger.LogWarning("Qty AutoDC upload skipped for {EquipmentNo}; ch3 could not be parsed: {Channel3}.", equipmentNo, machine.Channel3);
                    continue;
                }

                try
                {
                    await apiClient.SendQuantityAsync(equipmentNo, quantity.Value, cancellationToken);
                    qtySuccessCount++;
                }
                catch (Exception ex)
                {
                    qtyFailureCount++;
                    logger.LogError(ex, "Qty AutoDC upload failed for {EquipmentNo}, Qty {Quantity}; it will be retried next cycle.", equipmentNo, quantity);
                }
            }
        }
        else
        {
            qtySkippedCount = batch.Machines.Count;
        }

        _firstRun = false;
        logger.LogInformation(
            "Machine sync cycle completed. Status success: {SuccessCount}; status failed: {FailureCount}; status skipped unchanged: {SkippedCount}; Qty success: {QtySuccessCount}; Qty failed: {QtyFailureCount}; Qty skipped: {QtySkippedCount}.",
            successCount,
            failureCount,
            batch.Machines.Count - changed.Count,
            qtySuccessCount,
            qtyFailureCount,
            qtySkippedCount);
    }

    private void ValidateApiCredentials()
    {
        if (string.IsNullOrEmpty(_options.Api.Account) || string.IsNullOrEmpty(_options.Api.Password))
            throw new InvalidOperationException("Api:Account and Api:Password are empty. Fill them in appsettings.json before starting synchronization.");
    }
}
