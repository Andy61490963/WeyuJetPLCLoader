using System.Data;
using Dapper;
using IConnectMachineSync.DataModels;
using IConnectMachineSync.Services.Infrastructure;
using IConnectMachineSync.Services.Interface;

namespace IConnectMachineSync.Services.Service;

public sealed class DapperRepository(ISqlConnectionFactory connectionFactory) : IDapperRepository
{
    private const string EnsureCustomColumnsSql = """
        -- EQM_MASTER keeps the latest raw i-Connect values for quick current-state lookup.
        IF COL_LENGTH('dbo.EQM_MASTER','ZZ_CH1_VALUE') IS NULL
            ALTER TABLE dbo.EQM_MASTER ADD ZZ_CH1_VALUE nvarchar(510) NULL;
        IF COL_LENGTH('dbo.EQM_MASTER','ZZ_CH2_VALUE') IS NULL
            ALTER TABLE dbo.EQM_MASTER ADD ZZ_CH2_VALUE nvarchar(510) NULL;
        IF COL_LENGTH('dbo.EQM_MASTER','ZZ_CH3_VALUE') IS NULL
            ALTER TABLE dbo.EQM_MASTER ADD ZZ_CH3_VALUE nvarchar(510) NULL;
        IF COL_LENGTH('dbo.EQM_MASTER','ZZ_CH4_VALUE') IS NULL
            ALTER TABLE dbo.EQM_MASTER ADD ZZ_CH4_VALUE nvarchar(510) NULL;

        -- EQM_STATUS_CHANGE_HIST keeps the raw i-Connect values captured with each status change.
        IF COL_LENGTH('dbo.EQM_STATUS_CHANGE_HIST','ZZ_CH1_VALUE') IS NULL
            ALTER TABLE dbo.EQM_STATUS_CHANGE_HIST ADD ZZ_CH1_VALUE nvarchar(510) NULL;
        IF COL_LENGTH('dbo.EQM_STATUS_CHANGE_HIST','ZZ_CH2_VALUE') IS NULL
            ALTER TABLE dbo.EQM_STATUS_CHANGE_HIST ADD ZZ_CH2_VALUE nvarchar(510) NULL;
        IF COL_LENGTH('dbo.EQM_STATUS_CHANGE_HIST','ZZ_CH3_VALUE') IS NULL
            ALTER TABLE dbo.EQM_STATUS_CHANGE_HIST ADD ZZ_CH3_VALUE nvarchar(510) NULL;
        IF COL_LENGTH('dbo.EQM_STATUS_CHANGE_HIST','ZZ_CH4_VALUE') IS NULL
            ALTER TABLE dbo.EQM_STATUS_CHANGE_HIST ADD ZZ_CH4_VALUE nvarchar(510) NULL;
        """;

    private const string InsertMissingMachineSql = """
        -- UPDLOCK/HOLDLOCK keeps machine seeding idempotent when the service is started twice.
        IF NOT EXISTS (SELECT 1 FROM dbo.EQM_MASTER WITH (UPDLOCK, HOLDLOCK) WHERE EQM_MASTER_NO = @EquipmentNo)
        BEGIN
            INSERT INTO dbo.EQM_MASTER
            (
                EQM_MASTER_SID, EQM_MASTER_NO, EQM_MASTER_NAME, CUR_MOLD_NO,
                CUR_USE_COUNT, STATUS_SID, STATUS, ZZ_CH1_VALUE, ZZ_CH2_VALUE,
                ZZ_CH3_VALUE, ZZ_CH4_VALUE, ENABLE_FLAG, CREATE_USER, CREATE_TIME
            )
            VALUES
            (
                @EquipmentSid, @EquipmentNo, @EquipmentName, @MoldNo,
                @UseCount, @StatusSid, @StatusNo, @Channel1, @Channel2,
                @Channel3, @Channel4, 'Y', 'ICONNECT_SYNC', @CreateTime
            );
            SELECT 1;
        END;
        ELSE SELECT 0;
        """;

    private const string UpdateMachineCurrentFieldsSql = """
        UPDATE dbo.EQM_MASTER
           SET CUR_MOLD_NO = @MoldNo,
               CUR_USE_COUNT = @UseCount,
               ZZ_CH1_VALUE = @Channel1,
               ZZ_CH2_VALUE = @Channel2,
               ZZ_CH3_VALUE = @Channel3,
               ZZ_CH4_VALUE = @Channel4,
               EDIT_USER = 'ICONNECT_SYNC',
               EDIT_TIME = @EditTime
         WHERE EQM_MASTER_NO = @EquipmentNo;
        """;

    private const string UpdateHistoryCustomFieldsSql = """
        UPDATE dbo.EQM_STATUS_CHANGE_HIST
           SET ZZ_CH1_VALUE = @Channel1,
               ZZ_CH2_VALUE = @Channel2,
               ZZ_CH3_VALUE = @Channel3,
               ZZ_CH4_VALUE = @Channel4
         WHERE DATA_LINK_SID = @DataLinkSid;
        """;

    public async Task EnsureCustomColumnsAsync(CancellationToken cancellationToken)
    {
        await using var connection = connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await connection.ExecuteAsync(new CommandDefinition(EnsureCustomColumnsSql, cancellationToken: cancellationToken));
    }

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

        // Insert only missing machines; existing EQM_MASTER rows may already be maintained by
        // operators or the upstream system and must not be overwritten by seed data.
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
                    machine.Channel1,
                    machine.Channel2,
                    machine.Channel3,
                    machine.Channel4,
                    CreateTime = createdAt.LocalDateTime
                },
                transaction,
                cancellationToken: cancellationToken));
        }

        await transaction.CommitAsync(cancellationToken);
        return inserted;
    }

    public async Task<int> UpdateMachineCurrentFieldsAsync(
        IReadOnlyList<MachineSnapshot> machines,
        string prefix,
        CancellationToken cancellationToken)
    {
        await using var connection = connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        var editTime = DateTimeOffset.Now.LocalDateTime;
        var affected = 0;
        foreach (var machine in machines)
        {
            affected += await connection.ExecuteAsync(new CommandDefinition(
                UpdateMachineCurrentFieldsSql,
                new
                {
                    EquipmentNo = MachineDataFunctions.BuildEquipmentNo(prefix, machine.MachineName),
                    MoldNo = MachineDataFunctions.ParseMoldNo(machine.Channel2),
                    UseCount = MachineDataFunctions.ParseShotCount(machine.Channel3),
                    machine.Channel1,
                    machine.Channel2,
                    machine.Channel3,
                    machine.Channel4,
                    EditTime = editTime
                },
                cancellationToken: cancellationToken));
        }

        return affected;
    }

    public async Task<int> UpdateHistoryCustomFieldsAsync(
        decimal dataLinkSid,
        MachineSnapshot machine,
        CancellationToken cancellationToken)
    {
        await using var connection = connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        return await connection.ExecuteAsync(new CommandDefinition(
            UpdateHistoryCustomFieldsSql,
            new
            {
                // DATA_LINK_SID is generated before the StatusChange API call and echoed here so
                // these raw values land on the exact history row created by that API call.
                DataLinkSid = dataLinkSid,
                machine.Channel1,
                machine.Channel2,
                machine.Channel3,
                machine.Channel4
            },
            cancellationToken: cancellationToken));
    }
}
