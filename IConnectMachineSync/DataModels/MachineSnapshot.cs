namespace IConnectMachineSync.DataModels;

public sealed record MachineSnapshot(
    string SourceId,
    string MachineName,
    string MachineCondition,
    string Channel1,
    string Channel2,
    string Channel3,
    string Channel4);

public sealed record MachineBatch(
    DateTimeOffset CapturedAt,
    IReadOnlyList<MachineSnapshot> Machines,
    MachineCounts Counts);

public sealed record MachineCounts(int All, int Operation, int Stop, int Abnormal, int PowerOff);

public sealed record StatusMapping(decimal StatusSid, string StatusNo);
