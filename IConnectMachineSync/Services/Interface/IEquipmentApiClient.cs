using IConnectMachineSync.DataModels;

namespace IConnectMachineSync.Services.Interface;

public interface IEquipmentApiClient
{
    Task SendStatusAsync(MachineSnapshot machine, string equipmentNo, DateTimeOffset reportTime, CancellationToken cancellationToken);
}
