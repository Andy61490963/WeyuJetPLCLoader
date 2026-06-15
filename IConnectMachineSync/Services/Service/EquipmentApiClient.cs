using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Globalization;
using IConnectMachineSync.Configuration;
using IConnectMachineSync.DataModels;
using IConnectMachineSync.Services.Interface;
using Microsoft.Extensions.Options;

namespace IConnectMachineSync.Services.Service;

public sealed class EquipmentApiClient(
    HttpClient httpClient,
    IOptions<AppOptions> options,
    ILogger<EquipmentApiClient> logger) : IEquipmentApiClient
{
    private readonly ApiOptions _options = options.Value.Api;
    private string? _token;
    private DateTimeOffset _tokenExpiry;

    public async Task<decimal> SendStatusAsync(
        MachineSnapshot machine,
        string equipmentNo,
        DateTimeOffset reportTime,
        CancellationToken cancellationToken)
    {
        ValidateOptions();
        var status = MachineDataFunctions.MapStatus(machine.MachineCondition);
        var dataLinkSid = MachineDataFunctions.CreateSid(DateTimeOffset.Now, Random.Shared.Next(0, 999999));
        var request = new StatusChangeRequest(dataLinkSid, equipmentNo, status.StatusNo, _options.ReasonNo, reportTime, _options.InputFormName, true);

        Exception? lastError = null;
        for (var attempt = 1; attempt <= _options.RetryCount; attempt++)
        {
            try
            {
                await EnsureTokenAsync(cancellationToken);
                using var response = await httpClient.PostAsJsonAsync("api/Eqm/EqmStatus/StatusChange", request, cancellationToken);
                if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
                {
                    // Token can expire while the worker is running; clear it so the retry path logs in again.
                    _token = null;
                    throw new HttpRequestException("Equipment API token was rejected.");
                }
                if (!response.IsSuccessStatusCode)
                {
                    var body = await response.Content.ReadAsStringAsync(cancellationToken);
                    var message = $"Equipment API returned {(int)response.StatusCode} {response.ReasonPhrase} for {equipmentNo}. Body: {body}";
                    if ((int)response.StatusCode < 500) throw new InvalidOperationException(message);
                    throw new HttpRequestException(message);
                }
                // Return the generated DATA_LINK_SID so the repository can enrich the same
                // EQM_STATUS_CHANGE_HIST row with ZZ_CH1_VALUE through ZZ_CH4_VALUE.
                return dataLinkSid;
            }
            catch (Exception ex) when (attempt < _options.RetryCount && IsTransient(ex))
            {
                lastError = ex;
                logger.LogWarning(ex, "Equipment API attempt {Attempt}/{RetryCount} failed for {EquipmentNo}.", attempt, _options.RetryCount, equipmentNo);
                await Task.Delay(TimeSpan.FromSeconds(attempt * 2), cancellationToken);
            }
        }
        throw new HttpRequestException($"Equipment API failed for {equipmentNo} after {_options.RetryCount} attempts.", lastError);
    }

    public async Task SendQuantityAsync(
        string equipmentNo,
        decimal quantity,
        CancellationToken cancellationToken)
    {
        ValidateOptions();
        var quantityValue = $"Qty:{quantity.ToString(CultureInfo.InvariantCulture)}";
        var requestUri =
            $"api/Eqm/EqmAutoDc/AutoDcUpload?EQP_NO={Uri.EscapeDataString(equipmentNo)}" +
            $"&VALUE={Uri.EscapeDataString(quantityValue)}&UNIT=pcs&AutoIdle=FALSE&SameChange=FALSE";

        for (var attempt = 1; attempt <= _options.RetryCount; attempt++)
        {
            try
            {
                await EnsureTokenAsync(cancellationToken);
                // The POST endpoint currently fails in the server JSON formatter because its DTO
                // exposes both Value and VALUE. Use the documented GET compatibility endpoint.
                using var response = await httpClient.GetAsync(requestUri, cancellationToken);
                if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
                {
                    _token = null;
                    throw new HttpRequestException("Equipment API token was rejected.");
                }

                var body = await response.Content.ReadAsStringAsync(cancellationToken);
                if (!response.IsSuccessStatusCode)
                {
                    var message = $"AutoDC API returned {(int)response.StatusCode} {response.ReasonPhrase} for {equipmentNo}, Qty {quantity}. Body: {body}";
                    if ((int)response.StatusCode < 500) throw new InvalidOperationException(message);
                    throw new HttpRequestException(message);
                }

                var result = await response.Content.ReadFromJsonAsync<AutoDcUploadResponse>(cancellationToken: cancellationToken);
                if (result?.IsSuccess != true)
                    throw new InvalidOperationException($"AutoDC API rejected {equipmentNo}, Qty {quantity}. Code: {result?.Code}; Message: {result?.Message}; Body: {body}");
                return;
            }
            catch (Exception ex)
            {
                if (attempt >= _options.RetryCount || !IsTransient(ex)) throw;
                logger.LogWarning(ex, "AutoDC API attempt {Attempt}/{RetryCount} failed for {EquipmentNo}, Qty {Quantity}.", attempt, _options.RetryCount, equipmentNo, quantity);
                await Task.Delay(TimeSpan.FromSeconds(attempt * 2), cancellationToken);
            }
        }

        throw new HttpRequestException($"AutoDC API failed for {equipmentNo}, Qty {quantity} after {_options.RetryCount} attempts.");
    }

    private static bool IsTransient(Exception exception) =>
        exception is HttpRequestException or TaskCanceledException;

    private async Task EnsureTokenAsync(CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(_token) && _tokenExpiry > DateTimeOffset.UtcNow.AddMinutes(1))
        {
            httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _token);
            return;
        }

        // The API uses a short-lived bearer token. Cache it until shortly before expiry to avoid
        // logging in for every one of the 39 machines.
        using var response = await httpClient.PostAsJsonAsync("Security/Login/login", new LoginRequest(_options.Account, _options.Password), cancellationToken);
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<LoginResult>(cancellationToken: cancellationToken);
        _token = result?.Data?.TokenInfo?.TokenKey;
        _tokenExpiry = result?.Data?.TokenInfo?.TokenExpiry ?? DateTimeOffset.UtcNow.AddMinutes(5);
        if (string.IsNullOrWhiteSpace(_token))
            throw new InvalidOperationException($"API login did not return a token. {result?.Message}");
        httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _token);
    }

    private void ValidateOptions()
    {
        if (string.IsNullOrEmpty(_options.BaseUrl) ||
            string.IsNullOrEmpty(_options.Account) ||
            string.IsNullOrEmpty(_options.Password) ||
            string.IsNullOrEmpty(_options.ReasonNo))
            throw new InvalidOperationException("Api BaseUrl/Account/Password/ReasonNo are required in appsettings.json before status synchronization can run.");
    }
}
