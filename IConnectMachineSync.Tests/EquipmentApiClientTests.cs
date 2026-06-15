using System.Net;
using System.Text;
using System.Text.Json;
using IConnectMachineSync.Configuration;
using IConnectMachineSync.DataModels;
using IConnectMachineSync.Services.Service;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace IConnectMachineSync.Tests;

public sealed class EquipmentApiClientTests
{
    [Fact]
    public async Task SendStatusAsync_LogsInAndSendsMappedPayload()
    {
        var handler = new RecordingHandler();
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://example.test/") };
        var options = Options.Create(new AppOptions
        {
            Api = new ApiOptions { BaseUrl = "https://example.test/", Account = "api-user", Password = "api-password", ReasonNo = "99", RetryCount = 1 }
        });
        var client = new EquipmentApiClient(httpClient, options, NullLogger<EquipmentApiClient>.Instance);
        var machine = new MachineSnapshot("1", "S1", "Operation", "", "", "", "");

        await client.SendStatusAsync(machine, "JET-S1", DateTimeOffset.Parse("2026-06-11T10:00:00+08:00"), CancellationToken.None);

        Assert.Equal(2, handler.Requests.Count);
        Assert.Equal("/Security/Login/login", handler.Requests[0].Path);
        Assert.Equal("/api/Eqm/EqmStatus/StatusChange", handler.Requests[1].Path);
        Assert.Contains("\"EQM_NO\":\"JET-S1\"", handler.Requests[1].Body);
        Assert.Contains("\"EQM_STATUS_NO\":\"Run\"", handler.Requests[1].Body);
        Assert.Contains("\"REASON_NO\":\"99\"", handler.Requests[1].Body);
        Assert.DoesNotContain("\"DATA_LINK_SID\":0", handler.Requests[1].Body);
        Assert.Equal("Bearer test-token", handler.Requests[1].Authorization);
    }

    [Fact]
    public async Task SendQuantityAsync_SendsWipQtyPayloadWithBearerToken()
    {
        var handler = new RecordingHandler();
        var client = CreateClient(handler);

        await client.SendQuantityAsync("JET-S1", 1020m, CancellationToken.None);

        Assert.Equal(2, handler.Requests.Count);
        Assert.Equal("/api/Eqm/EqmAutoDc/AutoDcUpload", handler.Requests[1].Path);
        Assert.Contains("EQP_NO=JET-S1", handler.Requests[1].Query);
        Assert.Contains("VALUE=Qty%3A1020", handler.Requests[1].Query);
        Assert.Contains("UNIT=pcs", handler.Requests[1].Query);
        Assert.Contains("AutoIdle=FALSE", handler.Requests[1].Query);
        Assert.Contains("SameChange=FALSE", handler.Requests[1].Query);
        Assert.DoesNotContain("Mode=", handler.Requests[1].Query);
        Assert.Empty(handler.Requests[1].Body);
        Assert.Equal("Bearer test-token", handler.Requests[1].Authorization);
    }

    [Fact]
    public async Task SendQuantityAsync_ThrowsWhenApiReturnsUnsuccessfulResult()
    {
        var handler = new RecordingHandler("""{"IsSuccess":false,"Data":false,"Code":"BAD_QTY","Message":"invalid quantity"}""");
        var client = CreateClient(handler);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => client.SendQuantityAsync("JET-S1", 1020m, CancellationToken.None));

        Assert.Contains("BAD_QTY", exception.Message);
        Assert.Contains("invalid quantity", exception.Message);
    }

    private static EquipmentApiClient CreateClient(RecordingHandler handler)
    {
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://example.test/") };
        var options = Options.Create(new AppOptions
        {
            Api = new ApiOptions { BaseUrl = "https://example.test/", Account = "api-user", Password = "api-password", ReasonNo = "99", RetryCount = 1 }
        });
        return new EquipmentApiClient(httpClient, options, NullLogger<EquipmentApiClient>.Instance);
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        private readonly string _apiResponse;

        public RecordingHandler(string apiResponse = """{"IsSuccess":true,"Data":true,"Code":"","Message":""}""")
        {
            _apiResponse = apiResponse;
        }

        public List<(string Path, string Query, string Body, string? Authorization)> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken);
            Requests.Add((request.RequestUri!.AbsolutePath, request.RequestUri.Query, body, request.Headers.Authorization?.ToString()));
            var response = request.RequestUri.AbsolutePath.Contains("/login", StringComparison.OrdinalIgnoreCase)
                ? """{"IsSuccess":true,"Data":{"tokenInfo":{"TOKEN_KEY":"test-token","TOKEN_EXPIRY":"2099-01-01T00:00:00Z"}}}"""
                : _apiResponse;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(response, Encoding.UTF8, "application/json")
            };
        }
    }
}
