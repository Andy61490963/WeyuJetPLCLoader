using System.Text.Json;
using IConnectMachineSync.Configuration;
using IConnectMachineSync.DataModels;
using IConnectMachineSync.Services.Interface;
using Microsoft.Extensions.Options;
using Microsoft.Playwright;

namespace IConnectMachineSync.Services.Service;

public sealed class IConnectClient(IOptions<AppOptions> options, ILogger<IConnectClient> logger) : IIConnectClient
{
    private readonly IConnectOptions _options = options.Value.IConnect;
    private IPlaywright? _playwright;
    private IBrowser? _browser;
    private IPage? _page;

    public async Task<MachineBatch> ReadMachinesAsync(CancellationToken cancellationToken)
    {
        Exception? lastError = null;
        for (var attempt = 1; attempt <= 2; attempt++)
        {
            try
            {
                await EnsureLoggedInAsync(cancellationToken);
                return await ReadCurrentBatchAsync(cancellationToken);
            }
            catch (Exception ex) when (ex is PlaywrightException or TimeoutException)
            {
                lastError = ex;
                logger.LogWarning(ex, "i-Connect attempt {Attempt}/2 failed; rebuilding browser session.", attempt);
                await ResetAsync();
            }
        }
        throw new InvalidOperationException("i-Connect failed after two browser sessions.", lastError);
    }

    private async Task EnsureLoggedInAsync(CancellationToken cancellationToken)
    {
        ValidateOptions();
        if (_page is not null && !_page.IsClosed && _page.Url.Contains("frm_menu.cgi", StringComparison.OrdinalIgnoreCase))
            return;

        if (!string.IsNullOrWhiteSpace(_options.BrowserPath))
            Environment.SetEnvironmentVariable("PLAYWRIGHT_BROWSERS_PATH", _options.BrowserPath);
        _playwright ??= await Playwright.CreateAsync();
        _browser ??= await _playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions { Headless = _options.Headless });
        _page = await _browser.NewPageAsync(new BrowserNewPageOptions { ViewportSize = new ViewportSize { Width = 1920, Height = 950 } });
        _page.Dialog += async (_, dialog) => await dialog.AcceptAsync();
        await _page.GotoAsync(_options.LoginUrl, new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded, Timeout = 45_000 });
        await _page.FillAsync("#id_i_userid", _options.UserId);
        await _page.FillAsync("#id_i_password", _options.Password);
        await _page.ClickAsync("#id_i_login");
        await _page.WaitForSelectorAsync(
            "iframe[name=\"frm_menu\"]",
            new PageWaitForSelectorOptions { Timeout = _options.MaxWaitSeconds * 1000 });
        cancellationToken.ThrowIfCancellationRequested();
        logger.LogInformation("Logged in to i-Connect.");
    }

    private async Task<MachineBatch> ReadCurrentBatchAsync(CancellationToken cancellationToken)
    {
        var menu = _page!.Frames.FirstOrDefault(frame => frame.Name == "frm_menu")
            ?? throw new PlaywrightException("frm_menu frame not found.");

        await menu.WaitForSelectorAsync("#id_div_1", new FrameWaitForSelectorOptions { Timeout = 45_000 });
        await menu.ClickAsync("#id_div_1");
        await menu.ClickAsync("#id_l_radio1_1");

        var deadline = DateTimeOffset.UtcNow.AddSeconds(_options.MaxWaitSeconds);
        while (DateTimeOffset.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var count = await menu.EvaluateAsync<int>(
                "() => window.jQuery && window.jQuery.fn && window.jQuery('#id_tbl').dataTable ? window.jQuery('#id_tbl').dataTable().fnGetData().length : 0");
            if (count > 0) break;
            await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
        }

        var raw = await menu.EvaluateAsync<JsonElement>(
            """
            () => {
            const clean = value => {
              const div = document.createElement('div');
              div.innerHTML = String(value ?? '');
              return (div.textContent || div.innerText || '').replace(/\s+/g, ' ').trim();
            };
            const rows = window.jQuery('#id_tbl').dataTable().fnGetData().map(row => Array.from(row).slice(0, 7).map(clean));
            return {
              rows,
              counts: {
                all: clean(document.querySelector('#id_i_allcnt')?.innerHTML),
                operation: clean(document.querySelector('#id_i_kadoucnt')?.innerHTML),
                stop: clean(document.querySelector('#id_i_stopcnt')?.innerHTML),
                abnormal: clean(document.querySelector('#id_i_errcnt')?.innerHTML),
                powerOff: clean(document.querySelector('#id_i_offcnt')?.innerHTML)
              }
            };
            }
            """);

        var machines = raw.GetProperty("rows").EnumerateArray()
            .Select(row =>
            {
                var values = row.EnumerateArray().Select(value => value.GetString() ?? "").ToArray();
                return new MachineSnapshot(values[0], values[1], values[2], values[3], values[4], values[5], values[6]);
            })
            .OrderBy(machine => int.TryParse(machine.SourceId, out var id) ? id : int.MaxValue)
            .ToArray();
        if (machines.Length == 0) throw new TimeoutException("i-Connect machine table remained empty.");

        var counts = raw.GetProperty("counts");
        return new MachineBatch(DateTimeOffset.Now, machines, new MachineCounts(
            ParseCount(counts, "all"), ParseCount(counts, "operation"), ParseCount(counts, "stop"),
            ParseCount(counts, "abnormal"), ParseCount(counts, "powerOff")));
    }

    private static int ParseCount(JsonElement counts, string property) =>
        int.TryParse(counts.GetProperty(property).GetString(), out var count) ? count : 0;

    private void ValidateOptions()
    {
        if (string.IsNullOrWhiteSpace(_options.LoginUrl) ||
            string.IsNullOrWhiteSpace(_options.UserId) ||
            string.IsNullOrWhiteSpace(_options.Password))
            throw new InvalidOperationException("IConnect LoginUrl/UserId/Password are required in appsettings.json.");
    }

    private async Task ResetAsync()
    {
        if (_page is not null) await _page.CloseAsync();
        if (_browser is not null) await _browser.CloseAsync();
        _page = null;
        _browser = null;
    }

    public async ValueTask DisposeAsync()
    {
        await ResetAsync();
        _playwright?.Dispose();
    }
}
