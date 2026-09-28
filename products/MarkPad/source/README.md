# 素材重建

產生器需要在本儲存庫內執行。產品說明、範例文案、原始品牌與宣傳圖均為本專案素材，未下載第三方圖片。

## 商店圖標與宣傳圖

在 Windows 上執行：

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts/New-MarkPadIcon.ps1
powershell.exe -NoProfile -ExecutionPolicy Bypass -File products/MarkPad/source/Generate-StoreArtwork.ps1 -PackageAssetsDirectory artifacts/msix/Hanqing-1.0.2.0-x64-store/Layout/Assets
```

`scripts/MarkPadIconRenderer.cs` 是共用向量來源：保留藍底與短線，M 由四段帶皮青綠竹材組成。程式 ICO／SVG、商店 300×300 圖示、方形／直式宣傳圖與 23 張 MSIX PNG 使用相同圖形。新圖示已封入 `Hanqing_1.0.2.0_x64.msix`；上述命令將新包的 23 張 PNG 逐檔複製到 `assets/msix-package/`，並比對 SHA256。舊 1.0.1.0 包保留原樣。

若省略 `-PackageAssetsDirectory`，產生器會直接從目前向量來源重產 23 張套件圖示；要重建與本次已封裝版本完全一致的交接素材，請使用上述指定 1.0.2.0 套件的命令。

## 三語程式截圖

截圖器引用目前 MarkPad 專案，建立真實 MainWindow／AvalonEdit／WebView2；不呼叫 App.OnStartup 或 Store 授權，不能用來證明正式套件已通過授權驗收。範例檔存於 `samples/`，設定／復原／WebView2 暫存隔離於 `artifacts/markpad-store-capture/`。建置產物也只寫 artifacts。

三語各擷取 5 張 1600×1000 PNG，共 15 張，固定順序為竹子亮色預覽、竹子深色預覽、一般亮色預覽、一般深色預覽、竹子亮色編輯。檔名依序為 `01-preview-bamboo-light.png`、`02-preview-bamboo-dark.png`、`03-preview-light.png`、`04-preview-dark.png`、`05-edit-bamboo-light.png`；程式選單維持「竹子（亮色）」名稱。更新圖示後須先重新建置，再擷取新圖示實際入窗的畫面。

```powershell
$env:DOTNET_CLI_HOME = Join-Path (Get-Location) '.packages/dotnet-home'
$env:APPDATA = Join-Path (Get-Location) '.packages/appdata'
$env:NUGET_PACKAGES = Join-Path (Get-Location) '.packages'
dotnet restore products/MarkPad/source/Screenshots/Screenshots.csproj --configfile packaging/MarkPad/NuGet.Config --packages .packages
dotnet run --project products/MarkPad/source/Screenshots/Screenshots.csproj --no-restore
```

需要可正常建立 WebView2 子程序的 Windows 執行環境。此程式以離屏視窗擷取 PNG，未加入行銷文字、浮水印或虛構 UI。請勿與其他 MarkPad/WPF 建置同時執行。

## 預覽頁與交付包

修改文字或圖資後執行：

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File products/MarkPad/source/Build-ListingPack.ps1
```

這會重新產生 `index.html`、每語言截圖說明、驗證／SHA256 清單，以及 `products/MarkPad-StoreListing-1.0.2.0.zip`。ZIP 用於交接，Partner Center 各欄位仍須分別上傳；不把整包 ZIP 當成 MSIX。
