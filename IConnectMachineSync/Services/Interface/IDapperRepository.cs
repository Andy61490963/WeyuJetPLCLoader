using IConnectMachineSync.DataModels;

namespace IConnectMachineSync.Services.Interface;

public interface IDapperRepository
{
    Task<int> SeedMachinesAsync(IReadOnlyList<MachineSnapshot> machines, string prefix, CancellationToken cancellationToken);
}
