# IConnect Machine Sync (.NET 8)

這是 .NET 8 Worker Service，用來登入 i-Connect、讀取 39 台機台狀態、更新 JET 資料庫，並同步 Weyu Cloud Eqm API。

## 資料寫入規則

`EQM_MASTER` 儲存每台機台最近一次從 i-Connect 讀到的資料：

- `CUR_MOLD_NO`: 保留 i-Connect ch2 原始文字，例如 `Mold TOP`、`Mold ABS`、`Mold`
- `CUR_USE_COUNT`: 從 ch3 解析 shots 數字
- `ZZ_CH1_VALUE`: 最近一次 ch1 value
- `ZZ_CH2_VALUE`: 最近一次 ch2 value
- `ZZ_CH3_VALUE`: 最近一次 ch3 value
- `ZZ_CH4_VALUE`: 最近一次 ch4 value

`EQM_STATUS_CHANGE_HIST` 儲存每次狀態變更 API 成功時的歷史快照：

- `ZZ_CH1_VALUE`
- `ZZ_CH2_VALUE`
- `ZZ_CH3_VALUE`
- `ZZ_CH4_VALUE`

## 設定

主要設定在 `appsettings.json`：

- `IConnect`: i-Connect 登入網址、帳號、密碼、等待秒數
- `ConnectionStrings:Connection`: JET SQL Server 連線字串
- `Api:Account` / `Api:Password`: Weyu Cloud API 帳密
- `Api:ReasonNo`: 狀態變更預設原因
- `ExcelExport:Enabled`: 是否輸出 Excel
- `MachineSeed:Enabled`: 是否啟動時建立缺少的 `EQM_MASTER` 機台主檔

## 常用指令

只建立缺少的機台主檔：

```powershell
dotnet run --project .\IConnectMachineSync\IConnectMachineSync.csproj -- --seed-only
```

只抓 i-Connect 並視設定輸出 Excel，不呼叫 API：

```powershell
dotnet run --project .\IConnectMachineSync\IConnectMachineSync.csproj -- --scrape-once
```

執行一次完整同步：

```powershell
dotnet run --project .\IConnectMachineSync\IConnectMachineSync.csproj -- --run-once
```

正常 Worker / Windows Service 模式：

```powershell
dotnet run --project .\IConnectMachineSync\IConnectMachineSync.csproj
```

## Publish

```powershell
dotnet publish .\IConnectMachineSync\IConnectMachineSync.csproj -c Release -r win-x64 --self-contained false -o .\publish
```

發佈後的執行檔：

```text
C:\Users\Andy\Desktop\iconnect-machine-exporter\publish\IConnectMachineSync.exe
```

Log 會寫在執行目錄底下的 `Logs` 資料夾。
