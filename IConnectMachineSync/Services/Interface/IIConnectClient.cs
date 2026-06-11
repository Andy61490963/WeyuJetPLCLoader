using IConnectMachineSync.DataModels;

namespace IConnectMachineSync.Services.Interface;

public interface IIConnectClient : IAsyncDisposable
{
    Task<MachineBatch> ReadMachinesAsync(CancellationToken cancellationToken);
}
