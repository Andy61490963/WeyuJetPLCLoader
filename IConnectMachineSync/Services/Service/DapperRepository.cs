using System.Data;
using Dapper;
using IConnectMachineSync.DataModels;
using IConnectMachineSync.Services.Infrastructure;
using IConnectMachineSync.Services.Interface;

namespace IConnectMachineSync.Services.Service;

public sealed class DapperRepository(ISqlConnectionFactory connectionFactory) : IDapperRepository
{
    private const string InsertMissingMachineSql = """
        IF NOT EXISTS (SELECT 1 FROM dbo.EQM_MASTER WITH (UPDLOCK, HOLDLOCK) WHERE EQM_MASTER_NO = @EquipmentNo)
        BEGIN
            INSERT INTO dbo.EQM_MASTER
            (
                EQM_MASTER_SID, EQM_MASTER_NO, EQM_MASTER_NAME, CUR_MOLD_NO,
                CUR_USE_COUNT, STATUS_SID, STATUS, ENABLE_FLAG, CREATE_USER, CREATE_TIME
            )
            VALUES
            (
                @EquipmentSid, @EquipmentNo, @EquipmentName, @MoldNo,
                @UseCount, @StatusSid, @StatusNo, 'Y', 'ICONNECT_SYNC', @CreateTime
            );
            SELECT 1;
        END;
        ELSE SELECT 0;
        """;

    public async Task<int> SeedMachinesAsync(
        IReadOnlyList<MachineSnapshot> machines,
        string prefix,
        CancellationToken cancellationToken)
    {
        await using var connection = connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);

        var createdAt = DateTimeOffset.Now;
        var inserted = 0;

        for (var index = 0; index < machines.Count; index++)
        {
            var machine = machines[index];
            var status = MachineDataFunctions.MapStatus(machine.MachineCondition);
            inserted += await connection.QuerySingleAsync<int>(new CommandDefinition(
                InsertMissingMachineSql,
                new
                {
                    EquipmentSid = MachineDataFunctions.CreateSid(createdAt, index),
                    EquipmentNo = MachineDataFunctions.BuildEquipmentNo(prefix, machine.MachineName),
                    EquipmentName = machine.MachineName,
                    MoldNo = MachineDataFunctions.ParseMoldNo(machine.Channel2),
                    UseCount = MachineDataFunctions.ParseShotCount(machine.Channel3),
                    status.StatusSid,
                    status.StatusNo,
                    CreateTime = createdAt.LocalDateTime
                },
                transaction,
                cancellationToken: cancellationToken));
        }

        await transaction.CommitAsync(cancellationToken);
        return inserted;
    }
}
