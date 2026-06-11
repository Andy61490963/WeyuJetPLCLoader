using ClosedXML.Excel;
using IConnectMachineSync.DataModels;
using IConnectMachineSync.Services.Interface;

namespace IConnectMachineSync.Services.Service;

public sealed class ExcelService : IExcelService
{
    private static readonly string[] Headers =
    [
        "Molding machine ID", "Mold machine name", "Machine condition",
        "ch1 value", "ch2 value", "ch3 value", "ch4 value"
    ];

    public IReadOnlyList<MachineSnapshot> ReadMachines(string path)
    {
        using var workbook = new XLWorkbook(ResolvePath(path));
        var sheet = workbook.Worksheet("Machines");
        return sheet.RowsUsed().Skip(1).Select(row => new MachineSnapshot(
            row.Cell(1).GetString(), row.Cell(2).GetString(), row.Cell(3).GetString(),
            row.Cell(4).GetString(), row.Cell(5).GetString(), row.Cell(6).GetString(),
            row.Cell(7).GetString())).ToArray();
    }

    public Task<string> ExportAsync(MachineBatch batch, string outputDirectory, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var directory = ResolvePath(outputDirectory);
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, $"iconnect_machines_{batch.CapturedAt:yyyyMMdd_HHmmss}.xlsx");

        using var workbook = new XLWorkbook();
        var summary = workbook.AddWorksheet("Summary");
        summary.Cell(1, 1).Value = "Metric";
        summary.Cell(1, 2).Value = "Value";
        var metrics = new (string Name, object Value)[]
        {
            ("Captured at", batch.CapturedAt.LocalDateTime),
            ("All", batch.Counts.All), ("Operation", batch.Counts.Operation),
            ("Stop", batch.Counts.Stop), ("Abnormal", batch.Counts.Abnormal),
            ("Power Off", batch.Counts.PowerOff), ("Exported rows", batch.Machines.Count)
        };
        for (var i = 0; i < metrics.Length; i++)
        {
            summary.Cell(i + 2, 1).Value = metrics[i].Name;
            summary.Cell(i + 2, 2).Value = XLCellValue.FromObject(metrics[i].Value);
        }

        var machines = workbook.AddWorksheet("Machines");
        for (var col = 0; col < Headers.Length; col++) machines.Cell(1, col + 1).Value = Headers[col];
        for (var row = 0; row < batch.Machines.Count; row++)
        {
            var m = batch.Machines[row];
            var values = new[] { m.SourceId, m.MachineName, m.MachineCondition, m.Channel1, m.Channel2, m.Channel3, m.Channel4 };
            for (var col = 0; col < values.Length; col++) machines.Cell(row + 2, col + 1).Value = values[col];
        }

        foreach (var sheet in workbook.Worksheets)
        {
            sheet.Row(1).Style.Font.Bold = true;
            sheet.SheetView.FreezeRows(1);
            sheet.ColumnsUsed().AdjustToContents();
        }
        machines.RangeUsed()!.SetAutoFilter();
        workbook.SaveAs(path);
        return Task.FromResult(path);
    }

    private static string ResolvePath(string path) =>
        Path.IsPathRooted(path) ? path : Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, path));
}
