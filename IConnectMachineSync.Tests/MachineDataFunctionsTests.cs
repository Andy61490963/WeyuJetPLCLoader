using IConnectMachineSync.DataModels;
using IConnectMachineSync.Services.Service;

namespace IConnectMachineSync.Tests;

public sealed class MachineDataFunctionsTests
{
    [Theory]
    [InlineData("Operation", "Run")]
    [InlineData("Stop", "Stop_Tool")]
    [InlineData("Abnormal", "Error")]
    [InlineData("Power Off", "PowerOff")]
    public void MapStatus_ReturnsExpectedStatus(string source, string expected) =>
        Assert.Equal(expected, MachineDataFunctions.MapStatus(source).StatusNo);

    [Fact]
    public void ParseShotCount_ReturnsExpectedValue() =>
        Assert.Equal(168196m, MachineDataFunctions.ParseShotCount("Total number of shots 168196shots"));

    [Fact]
    public void ParseShotCount_ReturnsNullWhenMissing() =>
        Assert.Null(MachineDataFunctions.ParseShotCount(""));

    [Theory]
    [InlineData("Mold TOP", "TOP")]
    [InlineData("Mold", null)]
    [InlineData("Mold Mold Name", null)]
    public void ParseMoldNo_ReturnsExpectedValue(string source, string? expected) =>
        Assert.Equal(expected, MachineDataFunctions.ParseMoldNo(source));

    [Fact]
    public void ChangedMachines_OnlyReturnsChangesAfterStartup()
    {
        var machines = new[]
        {
            new MachineSnapshot("1", "S1", "Operation", "", "", "", ""),
            new MachineSnapshot("2", "S2", "Stop", "", "", "", "")
        };
        var previous = new Dictionary<string, string> { ["JET-S1"] = "Operation", ["JET-S2"] = "Operation" };

        var changed = MachineDataFunctions.ChangedMachines(machines, previous, "JET-", false);

        Assert.Single(changed);
        Assert.Equal("S2", changed[0].MachineName);
    }

    [Fact]
    public void CreateSid_IsUniqueWithinBatch()
    {
        var time = DateTimeOffset.Now;
        Assert.NotEqual(MachineDataFunctions.CreateSid(time, 0), MachineDataFunctions.CreateSid(time, 1));
    }
}
