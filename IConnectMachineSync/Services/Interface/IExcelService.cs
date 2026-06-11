using IConnectMachineSync.DataModels;

namespace IConnectMachineSync.Services.Interface;

public interface IExcelService
{
    IReadOnlyList<MachineSnapshot> ReadMachines(string path);
    Task<string> ExportAsync(MachineBatch batch, string outputDirectory, CancellationToken cancellationToken);
}
