using System.Globalization;
using System.Text.RegularExpressions;
using IConnectMachineSync.DataModels;

namespace IConnectMachineSync.Services.Service;

public static partial class MachineDataFunctions
{
    private static readonly IReadOnlyDictionary<string, StatusMapping> StatusMappings =
        new Dictionary<string, StatusMapping>(StringComparer.OrdinalIgnoreCase)
        {
            ["Operation"] = new(67742021653861m, "Run"),
            ["Stop"] = new(178263206220625m, "Stop_Tool"),
            ["Abnormal"] = new(67741971200698m, "Error"),
            ["Power Off"] = new(67741971200700m, "PowerOff")
        };

    public static StatusMapping MapStatus(string condition) =>
        StatusMappings.TryGetValue(condition.Trim(), out var mapping)
            ? mapping
            : throw new InvalidOperationException($"Unsupported machine condition: {condition}");

    public static string BuildEquipmentNo(string prefix, string machineName) =>
        $"{prefix}{machineName.Trim()}";

    public static decimal? ParseShotCount(string channel3)
    {
        var match = ShotCountRegex().Match(channel3);
        return match.Success && decimal.TryParse(match.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var value)
            ? value
            : null;
    }

    public static string? ParseMoldNo(string channel2)
    {
        var value = channel2.Trim();
        if (string.Equals(value, "Mold", StringComparison.OrdinalIgnoreCase)) return null;
        if (value.StartsWith("Mold ", StringComparison.OrdinalIgnoreCase)) value = value[5..].Trim();
        return string.IsNullOrWhiteSpace(value) || string.Equals(value, "Mold Name", StringComparison.OrdinalIgnoreCase)
            ? null
            : value;
    }

    public static decimal CreateSid(DateTimeOffset timestamp, int batchIndex)
    {
        var seconds = (long)(timestamp.LocalDateTime - new DateTime(2013, 5, 1)).TotalSeconds;
        var suffix = (timestamp.Millisecond * 1000 + batchIndex) % 1_000_000;
        return decimal.Parse($"{seconds}{suffix:000000}", CultureInfo.InvariantCulture);
    }

    public static IReadOnlyList<MachineSnapshot> ChangedMachines(
        IReadOnlyList<MachineSnapshot> current,
        IReadOnlyDictionary<string, string> previous,
        string prefix,
        bool forceAll) =>
        current.Where(machine =>
        {
            if (forceAll) return true;
            var equipmentNo = BuildEquipmentNo(prefix, machine.MachineName);
            return !previous.TryGetValue(equipmentNo, out var condition) ||
                   !string.Equals(condition, machine.MachineCondition, StringComparison.OrdinalIgnoreCase);
        }).ToArray();

    [GeneratedRegex(@"(\d+)\s*shots", RegexOptions.IgnoreCase)]
    private static partial Regex ShotCountRegex();
}
