# IConnect Machine Sync (.NET 8)

背景 Worker Service，每分鐘從 i-Connect 讀取 39 台機台資訊，選擇性輸出 Excel，並將機況變動送到 Weyu Cloud Eqm API。

## 設定

編輯 `appsettings.json`：

- `IConnect`: i-Connect 登入資訊。
- `ConnectionStrings:Connection`: JET SQL Server。
- `Api:Account` / `Api:Password`: Weyu Cloud API 登入資訊。目前預設留空，填入後才能執行正式同步。
- `ExcelExport:Enabled`: 是否每輪產生 Excel。
- `MachineSeed:Enabled`: 是否允許 `--seed-only` 從指定 Excel 建立缺少的機台。

## 建立機台主檔

```powershell
dotnet run --project .\IConnectMachineSync\IConnectMachineSync.csproj -- --seed-only
```

此命令只新增 `EQM_MASTER_NO` 不存在的機台，可以安全重複執行。

## 正常執行

```powershell
dotnet run --project .\IConnectMachineSync\IConnectMachineSync.csproj
```

正常執行前必須先填入 `Api:Account` 和 `Api:Password`。

## 只測試抓取

不呼叫 API，只登入 i-Connect 抓取一次，並依 `ExcelExport:Enabled` 決定是否產生 Excel：

```powershell
dotnet run --project .\IConnectMachineSync\IConnectMachineSync.csproj -- --scrape-once
```

加上 `--no-excel` 可暫時關閉 Excel：

```powershell
dotnet run --project .\IConnectMachineSync\IConnectMachineSync.csproj -- --scrape-once --no-excel
```

`--run-once` 會執行一次完整流程（包含 API），方便正式帳密填入後測試。

## 安裝 Playwright Chromium

首次還原並建置後執行：

```powershell
$env:PLAYWRIGHT_BROWSERS_PATH = "C:\Users\Andy\Desktop\iconnect-machine-exporter\playwright-browsers"
pwsh .\IConnectMachineSync\bin\Debug\net8.0\playwright.ps1 install chromium
```

## Windows Service

先 publish，再由系統管理員 PowerShell 建立服務：

```powershell
dotnet publish .\IConnectMachineSync\IConnectMachineSync.csproj -c Release -r win-x64 --self-contained false -o .\publish
sc.exe create IConnectMachineSync binPath= "C:\Users\Andy\Desktop\iconnect-machine-exporter\publish\IConnectMachineSync.exe"
sc.exe start IConnectMachineSync
```

Log 會寫入執行目錄下的 `Logs`。
