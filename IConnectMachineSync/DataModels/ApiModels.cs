using System.Text.Json.Serialization;

namespace IConnectMachineSync.DataModels;

public sealed record LoginRequest(string Account, string Password);

public sealed class LoginResult
{
    public bool IsSuccess { get; init; }
    public LoginData? Data { get; init; }
    public string? Message { get; init; }
}

public sealed class LoginData
{
    [JsonPropertyName("tokenInfo")]
    public TokenInfo? TokenInfo { get; init; }
}

public sealed class TokenInfo
{
    [JsonPropertyName("TOKEN_KEY")]
    public string? TokenKey { get; init; }

    [JsonPropertyName("TOKEN_EXPIRY")]
    public DateTimeOffset? TokenExpiry { get; init; }
}

public sealed record StatusChangeRequest(
    [property: JsonPropertyName("DATA_LINK_SID")] decimal DataLinkSid,
    [property: JsonPropertyName("EQM_NO")] string EquipmentNo,
    [property: JsonPropertyName("EQM_STATUS_NO")] string EquipmentStatusNo,
    [property: JsonPropertyName("REASON_NO")] string? ReasonNo,
    [property: JsonPropertyName("REPORT_TIME")] DateTimeOffset ReportTime,
    [property: JsonPropertyName("INPUT_FORM_NAME")] string InputFormName,
    [property: JsonPropertyName("UPDATE_EQM_MASTER")] bool UpdateEquipmentMaster);

public sealed class AutoDcUploadResponse
{
    public bool IsSuccess { get; init; }
    public bool? Data { get; init; }
    public string? Code { get; init; }
    public string? Message { get; init; }
}
