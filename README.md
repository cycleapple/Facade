# Facade — API 13 繁體中文版

此分支將 [keifufu/Facade](https://github.com/keifufu/Facade) 移植至 **Dalamud API 13 / .NET 9**，介面為 **繁體中文（zh-TW）**。原作者為 keifufu，保留 AGPL-3.0 授權。

Facade 可在本機自訂住宅外觀、各部件染色與住宅區節慶裝飾，也可儲存、匯入及匯出預設。走進地皮時會暫時還原原本外觀，保留原有碰撞判定。

## 安裝測試版

在 Dalamud 設定的「自訂插件儲存庫」新增以下網址並儲存，然後在插件安裝器搜尋 **Facade**：

```text
https://raw.githubusercontent.com/cycleapple/Facade/api13-tw/repo.json
```

也可從 [Releases](https://github.com/cycleapple/Facade/releases) 下載 ZIP，完整解壓縮後，將 `Facade.dll` 加入 Dalamud 的開發插件位置。請保留同一目錄的 `plots.json` 及所有相依 DLL。

載入後輸入 `/facade` 開啟介面，`/facade help` 查看指令。

## 相容性與驗證範圍

- 針對台版 Dalamud API 13，使用 .NET 9，並以台版 FFXIVClientStructs 結構編譯。
- 已驗證本機 API 13.0.0.8 與固定版本 API 13.0.0.16 的編譯；不支援缺少住宅外觀結構的早期 API 13 執行庫。
- 介面、提示、住宅區名稱及指令說明為 zh-TW；物品與染劑名稱使用遊戲客戶端目前的語言資料。
- 移除上游對英文住宅名稱的單字拆解，避免中文名稱造成索引錯誤。
- 節慶讀寫依 API 13 結構的 **4 組**欄位處理，外觀部件仍為 8 組。
- 已通過 Debug / Release 編譯與 922 項離線檢查，涵蓋全部 300 塊地皮的中心／邊界／緩衝判定、結構大小，以及繁中預設序列化。
- **尚未完成遊戲內驗證**：實際外觀套用、進出地皮還原、玩家隱藏、節慶切換及字型顯示仍需實機測試。上游節慶 ID 在不同遊戲資料版本可能沒有對應模型。

## 建置

需要 .NET 9 SDK，以及台版 API 13 的 Dalamud 執行庫。將 `DALAMUD_HOME` 設為包含 `Dalamud.dll` 和 `FFXIVClientStructs.dll` 的目錄：

```powershell
$env:DALAMUD_HOME = 'C:\path\to\dalamud-api13'
dotnet restore Facade.csproj --locked-mode
dotnet build Facade.csproj -c Debug --no-restore
dotnet build Facade.csproj -c Release --no-restore
dotnet run --project tests/Facade.SmokeTests.csproj -c Release
```

安裝包位於 `bin/Release/Facade/latest.zip`。

CI 固定使用 [yanmucorp/Dalamud 26-07-28-02](https://github.com/yanmucorp/Dalamud/releases/tag/26-07-28-02) 的 API 13.0.0.16，並驗證封裝的 SHA-256：

```text
9cbe2ebc5287b7ceaf1c5d8eaea41135324809ebfcb921819480201634cbfb96
```

請勿直接使用國際版 `latest` 執行庫；它可能是不同 API 或 .NET 版本。

## 授權

[AGPL-3.0](LICENSE)。本 Fork 以 `api13-tw` 分支提供完整修改原始碼。
