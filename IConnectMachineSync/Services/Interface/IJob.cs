namespace IConnectMachineSync.Services.Interface;

public interface IJob
{
    Task ExecuteAsync(CancellationToken cancellationToken);
}
