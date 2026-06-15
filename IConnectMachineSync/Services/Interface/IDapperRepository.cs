using IConnectMachineSync.DataModels;

namespace IConnectMachineSync.Services.Interface;

public interface IDapperRepository
{
    Task EnsureCustomColumnsAsync(CancellationToken cancellationToken);
    Task<int> SeedMachinesAsync(IReadOnlyList<MachineSnapshot> machines, string prefix, CancellationToken cancellationToken);
    Task<int> UpdateMachineCurrentFieldsAsync(IReadOnlyList<MachineSnapshot> machines, string prefix, CancellationToken cancellationToken);
    Task<int> UpdateHistoryCustomFieldsAsync(decimal dataLinkSid, MachineSnapshot machine, CancellationToken cancellationToken);
}
