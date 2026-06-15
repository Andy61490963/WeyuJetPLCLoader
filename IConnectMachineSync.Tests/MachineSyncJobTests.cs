using IConnectMachineSync.Configuration;
using IConnectMachineSync.DataModels;
using IConnectMachineSync.Services.Interface;
using IConnectMachineSync.Services.Service;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace IConnectMachineSync.Tests;

public sealed class MachineSyncJobTests
{
    [Fact]
    public async Task ExecuteAsync_UploadsSameQuantityOnEveryCycle()
    {
        var api = new FakeEquipmentApiClient();
        var job = CreateJob(api, new MachineSnapshot("1", "S1", "Operation", "", "", "Total number of shots 1020shots", ""));

        await job.ExecuteAsync(CancellationToken.None);
        await job.ExecuteAsync(CancellationToken.None);

        Assert.Equal(2, api.Quantities.Count);
        Assert.All(api.Quantities, upload => Assert.Equal(("JET-S1", 1020m), upload));
    }

    [Fact]
    public async Task ExecuteAsync_SkipsUnparseableQuantity()
    {
        var api = new FakeEquipmentApiClient();
        var job = CreateJob(api, new MachineSnapshot("1", "S1", "Operation", "", "", "not available", ""));

        await job.ExecuteAsync(CancellationToken.None);

        Assert.Empty(api.Quantities);
    }

    [Fact]
    public async Task ExecuteAsync_UploadsQuantityWhenStatusSynchronizationFails()
    {
        var api = new FakeEquipmentApiClient { FailStatus = true };
        var job = CreateJob(api, new MachineSnapshot("1", "S1", "Operation", "", "", "1020 shots", ""));

        await job.ExecuteAsync(CancellationToken.None);

        Assert.Single(api.Quantities);
        Assert.Equal(("JET-S1", 1020m), api.Quantities[0]);
    }

    [Fact]
    public async Task ExecuteAsync_SynchronizesStatusWhenQuantityUploadFails()
    {
        var api = new FakeEquipmentApiClient { FailQuantity = true };
        var job = CreateJob(api, new MachineSnapshot("1", "S1", "Operation", "", "", "1020 shots", ""));

        await job.ExecuteAsync(CancellationToken.None);

        Assert.Equal(1, api.StatusCallCount);
        Assert.Equal(1, api.QuantityCallCount);
    }

    private static MachineSyncJob CreateJob(FakeEquipmentApiClient api, params MachineSnapshot[] machines)
    {
        var batch = new MachineBatch(DateTimeOffset.Parse("2026-06-15T10:00:00+08:00"), machines, new MachineCounts(machines.Length, 0, 0, 0, 0));
        var options = Options.Create(new AppOptions
        {
            Api = new ApiOptions { Account = "api-user", Password = "api-password" },
            Sync = new SyncOptions { EquipmentPrefix = "JET-", SendQtyAutoDc = true },
            ExcelExport = new ExcelExportOptions { Enabled = false }
        });
        return new MachineSyncJob(
            new FakeIConnectClient(batch),
            api,
            new FakeExcelService(),
            new FakeRepository(),
            options,
            NullLogger<MachineSyncJob>.Instance);
    }

    private sealed class FakeEquipmentApiClient : IEquipmentApiClient
    {
        public bool FailStatus { get; init; }
        public bool FailQuantity { get; init; }
        public int StatusCallCount { get; private set; }
        public int QuantityCallCount { get; private set; }
        public List<(string EquipmentNo, decimal Quantity)> Quantities { get; } = [];

        public Task<decimal> SendStatusAsync(MachineSnapshot machine, string equipmentNo, DateTimeOffset reportTime, CancellationToken cancellationToken)
        {
            StatusCallCount++;
            return FailStatus
                ? Task.FromException<decimal>(new HttpRequestException("status failed"))
                : Task.FromResult(1m);
        }

        public Task SendQuantityAsync(string equipmentNo, decimal quantity, CancellationToken cancellationToken)
        {
            QuantityCallCount++;
            if (FailQuantity) return Task.FromException(new HttpRequestException("quantity failed"));
            Quantities.Add((equipmentNo, quantity));
            return Task.CompletedTask;
        }
    }

    private sealed class FakeIConnectClient(MachineBatch batch) : IIConnectClient
    {
        public Task<MachineBatch> ReadMachinesAsync(CancellationToken cancellationToken) => Task.FromResult(batch);
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class FakeExcelService : IExcelService
    {
        public IReadOnlyList<MachineSnapshot> ReadMachines(string path) => [];
        public Task<string> ExportAsync(MachineBatch batch, string outputDirectory, CancellationToken cancellationToken) => Task.FromResult("");
    }

    private sealed class FakeRepository : IDapperRepository
    {
        public Task EnsureCustomColumnsAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<int> SeedMachinesAsync(IReadOnlyList<MachineSnapshot> machines, string prefix, CancellationToken cancellationToken) => Task.FromResult(0);
        public Task<int> UpdateMachineCurrentFieldsAsync(IReadOnlyList<MachineSnapshot> machines, string prefix, CancellationToken cancellationToken) => Task.FromResult(machines.Count);
        public Task<int> UpdateHistoryCustomFieldsAsync(decimal dataLinkSid, MachineSnapshot machine, CancellationToken cancellationToken) => Task.FromResult(1);
    }
}
