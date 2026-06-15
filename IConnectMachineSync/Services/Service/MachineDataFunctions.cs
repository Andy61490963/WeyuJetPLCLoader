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
        // ch2 is stored as-is because users inspect the same wording shown on i-Connect,
        // including values such as "Mold", "Mold ABS", and "Mold Mold Name".
        var value = channel2.Trim();
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    public static decimal CreateSid(DateTimeOffset timestamp, int batchIndex)
    {
        // dbo.GetSid() was not reliable in this environment. This produces a deterministic,
        // time-based numeric SID with a per-batch suffix so seed/API rows stay unique.
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
            // Startup sends a full baseline once; later cycles only retry unsent or changed statuses.
            if (forceAll) return true;
            var equipmentNo = BuildEquipmentNo(prefix, machine.MachineName);
            return !previous.TryGetValue(equipmentNo, out var condition) ||
                   !string.Equals(condition, machine.MachineCondition, StringComparison.OrdinalIgnoreCase);
        }).ToArray();

    [GeneratedRegex(@"(\d+)\s*shots", RegexOptions.IgnoreCase)]
    private static partial Regex ShotCountRegex();
}
