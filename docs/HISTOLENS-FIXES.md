# 006 HistoLens 檢查修正紀錄

日期：2026-10-03。依使用者「提交後實作」指示，先提交合成資料預覽及[檢查報告](HISTOLENS-OPTIMIZATION-REVIEW.md)，再實作修正。

基線提交：`725169e`（`feat(histolens): add synthetic research preview and review baseline`）。基線只納入 006；共用目錄的其他產品變更沒有一併提交。本輪後續修正依使用者「提交推送」指示另行提交，納入驗證所需的宿主 WPF 測試隔離設定。

## 修正範圍

| 報告項目 | 處理方式 |
| --- | --- |
| O01 數值排序 | 顯示文字與排序原值分離；含多種計數而沒有單一意義的欄位不提供誤導性排序 |
| O02 阻擋研究保留成果 | 完整研究與新執行的阻擋原因分開，失敗不替換前次案例及統計 |
| O03 原子載入 | 保存層驗證結構；UI 預先驗證可編輯模板及準備顯示資料，成功後才替換工作，更新旗標保證復原 |
| O04 檔案一致性 | 核對研究與快照身份、日曆／公司行動版本、日期、觀察期、集合及計數；不重跑舊引擎改寫歷史結果 |
| O05 日期順序 | 案例原始表格及圖表依日期排序，不受快照輸入順序影響 |
| O06 三態 AND | 缺量只使需要成交量的條件 Unknown，false AND unknown 為 false；OHLC／停牌／公司行動品質閘門保留 |
| O07 大量保存 | v1 精簡 JSON，保留舊版讀取；背景執行驗證／hash／JSON／I/O，串流限制保存大小並保留取消及暫存清理 |
| O08 取消狀態 | 沒有舊结果的首次執行因編輯取消，也更新待執行狀態 |
| O09 語言刷新 | 切換語言時更新選中案例的說明，保留研究與選取 |
| O10 極端門檻 | 驗證百分比可表示範圍；超出 decimal 可表示價格的上方目標不可能被有效 decimal 行情觸及，回報無觸及 |
| O11 配置量 | 每次研究只計算一次各日期的品質原因，重疊窗口重用；不建立跨研究長期快取 |
| O12 宿主測試 | 工作目錄已有的 WPF UI collection 將四個共用 WPF 狀態的測試類別依序執行；本輪納入整合驗證 |
| O13 可重建預覽 | 新增 `scripts/Publish-HistoLensPreview.ps1`，串接測試、六張圖片、TRX、合成範例、publish 及隔離啟動設定 |

引擎版本改為 `0.1.1-m0`，用來區分 O06 的判定修正。一般價格門檻保留原本 decimal 乘法及捨入；不以任意市場漲跌幅當輸入上限。

本輪不更改 `Status=Traded, Volume=0` 的既有核心政策；Provider 的零成交／參考價正規化契約仍待接入時固定。真實供應商、資料下載、正式封裝與商業決策延續[既有待辦](HISTOLENS-IMPLEMENTATION.md)，不因本次修正而視為完成。

## 驗證

- Release HistoLens **105／105 通過**（核心 59、UI 16、保存 30）；新增 52 項回歸案例。最終結果含保留舊載入方法簽章的修改。
- 宿主定向測試採現有 WPF collection 隔離後，連續三次 **94／94 通過**，未使用全套禁用平行的 `.runsettings`。支持此次隔離有效；三次通過不是永不再發生競態的保證。
- `Publish-HistoLensPreview.ps1` 完整執行成功：測試、六張離屏圖片、兩個合成範例、TRX、publish、隔離 profile／CMD 及檔案雜湊 manifest 均產生。抽查繁中最小視窗案例頁與英文深色摘要；未啟動正式桌面或寫入正式偏好。
- 實際讀取前次交付的 `0.1.0-m0` 研究檔，再由新保存層重存及載入；完整研究及快照序列化內容一致，保留原引擎版本、15 個事件及相同快照 hash。原檔 863,769 bytes，精簡檔 487,212 bytes。
- 10,000 筆／9,880 事件／6 條件／4 期間可完整保存及載入：**35,823,806 bytes（約 34.16 MiB）**，低於固定 64 MiB 上限。第一輪整合量測回傳 Task 前 0.02 ms、保存 472.14 ms、保存加載入 1,782.52 ms；預覽重建輪為 0.02／330.21／1,397.04 ms。均為單次診斷樣本；與舊報告的一條件保存探針不是同一 fixture，不據此宣稱保存加速倍率。
- 額外核對：來源目錄輸出防護、manifest 所記檔案 SHA-256、預覽的 `DesktopEnabled=false`、無暫存寫入殘留回歸、原始碼差異空白檢查。

最終 TRX 與畫面位於 [verification](../artifacts/histolens-fixes/verification/)；舊檔相容性記錄位於 [legacy-roundtrip.json](../artifacts/histolens-fixes/legacy/roundtrip/legacy-roundtrip.json)。重建輸出為 [Start-HistoLens.cmd](../artifacts/histolens-preview/Start-HistoLens.cmd)，日誌為 [publish.log](../artifacts/histolens-fixes/publish.log)。

相同核心探針、相同 fixture、Release、各程序先暖機，再比較第二次量測：

| 10,000 筆／6 條件 | 原配置 bytes | 修正後配置 bytes | 原時間 | 修正後時間 |
| --- | ---: | ---: | ---: | ---: |
| 回看 120；觀察 5／10／20／60 | 384,663,112 | 44,747,752 | 172.3 ms | 110.8 ms |
| 回看 2,500；觀察 5／10／20／60 | 3,149,266,464 | 35,674,496 | 823.7 ms | 193.5 ms |
| 回看 120；觀察 5／10／20／2,500 | 3,743,075,912 | 45,862,680 | 1,201.9 ms | 451.7 ms |

三組事件數與最長觀察期有效 N 前後相同。配置量是執行緒累計配置，並非常駐記憶體或峰值；此為本機單組診斷樣本，不能推廣為所有資料／機器的保證。取消探針三次延遲為 1.675／0.102／0.180 ms。未測真實網路下載或實機多螢幕 DPI。

量測環境延續原檢查：Intel Core Ultra 7 255HX、20 核心／20 邏輯處理器、約 31.4 GiB RAM、ZHITAI TiPlus7100s 2TB NVMe、Windows build 26200、.NET 10.0.12、x64。其他檢查可能同時執行，效能時間不是隔離硬體基準。

核心前後證據位於 `artifacts/histolens-fixes/core/`，保留 `performance-before.jsonl`、`performance-after.jsonl`、`correctness-before.jsonl`、`correctness-after.jsonl`、`cancellation-after.jsonl` 與探針原碼 hash。這些是本機 artifacts，新增正式回歸測試位於 `tests/HistoLens.Tests/`。

## 重建與操作

從專案根目錄執行：

```powershell
./scripts/Publish-HistoLensPreview.ps1
```

預設輸出 `artifacts/histolens-preview/`，證據置於 `artifacts/histolens-verification/<時間>/`。亦可指定兩個互不巢狀的 artifacts 子目錄：

```powershell
./scripts/Publish-HistoLensPreview.ps1 -OutputDirectory artifacts/histolens-fixes/preview -EvidenceDirectory artifacts/histolens-fixes/verification
```

腳本會驗證測試成功及八個範例／圖片檔存在後才 publish，保留原有預覽研究及偏好，並將該預覽宿主的桌面接管設為關閉。啟動檔只使用產物內 profile 與空桌面來源，不註冊系統協定；腳本不會自行啟動 GUI。需 Windows 與 .NET 10 Desktop Runtime。

`verification-manifest.json` 記錄證據檔案大小及 SHA-256，供比對產物；它不是發行簽章。若要保留舊預覽二進位檔，使用新的輸出目錄，勿覆蓋同一路徑。

效率紀錄主任務：`TaskRunId=943`。主代理負責基線提交、整合、腳本、文件及驗證；UI／Store／Core 三個子代理各自維護不重疊檔案。實際執行模型未由工具確認者保留未知；代理完成狀態可確認，獨立 close-agent 介面不可用時 `ClosedNormally` 保留 NULL。
