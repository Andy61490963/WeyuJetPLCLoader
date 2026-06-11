namespace IConnectMachineSync.Configuration;

public sealed class AppOptions
{
    public IConnectOptions IConnect { get; init; } = new();
    public ApiOptions Api { get; init; } = new();
    public SyncOptions Sync { get; init; } = new();
    public MachineSeedOptions MachineSeed { get; init; } = new();
    public ExcelExportOptions ExcelExport { get; init; } = new();
    public AppLoggingOptions Logging { get; init; } = new();
}

public sealed class IConnectOptions
{
    public string LoginUrl { get; init; } = "";
    public string UserId { get; init; } = "";
    public string Password { get; init; } = "";
    public int MaxWaitSeconds { get; init; } = 90;
    public bool Headless { get; init; } = true;
    public string BrowserPath { get; init; } = "";
}

public sealed class ApiOptions
{
    public string BaseUrl { get; init; } = "";
    public string Account { get; init; } = "";
    public string Password { get; init; } = "";
    public string ReasonNo { get; init; } = "99";
    public string InputFormName { get; init; } = "iConnectMachineSync";
    public int RetryCount { get; init; } = 3;
}

public sealed class SyncOptions
{
    public int IntervalSeconds { get; init; } = 60;
    public string EquipmentPrefix { get; init; } = "JET-";
    public bool SendStartupFullSync { get; init; } = true;
}

public sealed class MachineSeedOptions
{
    public bool Enabled { get; init; }
    public string SourceExcelPath { get; init; } = "";
}

public sealed class ExcelExportOptions
{
    public bool Enabled { get; init; }
    public string OutputDirectory { get; init; } = "";
}

public sealed class AppLoggingOptions
{
    public string ApplicationName { get; init; } = "IConnectMachineSync";
    public string MinimumLevel { get; init; } = "Information";
    public FileLoggingOptions File { get; init; } = new();
    public SeqLoggingOptions Seq { get; init; } = new();
}

public sealed class FileLoggingOptions
{
    public bool Enabled { get; init; } = true;
    public int RetainDays { get; init; } = 30;
    public int FileSizeLimitMB { get; init; } = 50;
}

public sealed class SeqLoggingOptions
{
    public bool Enabled { get; init; }
    public string ServerUrl { get; init; } = "";
    public string BufferRelativePath { get; init; } = "seq-buffer";
    public int PeriodSeconds { get; init; } = 5;
}
