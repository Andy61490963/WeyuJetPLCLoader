using IConnectMachineSync.DataModels;

namespace IConnectMachineSync.Services.Interface;

public interface IEquipmentApiClient
{
    Task<decimal> SendStatusAsync(MachineSnapshot machine, string equipmentNo, DateTimeOffset reportTime, CancellationToken cancellationToken);
    Task SendQuantityAsync(string equipmentNo, decimal quantity, CancellationToken cancellationToken);
}
