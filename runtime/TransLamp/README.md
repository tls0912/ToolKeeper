# TransLamp 本機翻譯 runtime

這是 Windows x64 CPU 原型。`translate.py` 以 CTranslate2 int8 與 SentencePiece 執行真實 OPUS-MT 模型，不呼叫雲端翻譯、下載器或全域 Python。GUI 與語言包驗證／匯入由 C# 主程式負責。

## 重建與驗證

在專案根目錄執行：

```powershell
powershell -ExecutionPolicy Bypass -File scripts/Prepare-TransLampResources.ps1
# 已有完整且 SHA256 正確的下載快取時，全程不下載：
powershell -ExecutionPolicy Bypass -File scripts/Prepare-TransLampResources.ps1 -Offline
```

腳本只寫入指定輸出目錄，預設為 `artifacts/translamp`。下載版本、大小、SHA256 與來源固定在 `resources.lock.json`；下載中斷可重跑續傳，未通過雜湊的檔案不會被使用。PyPI wheel 雜湊採上游發布值；Python／Argos 基準值由官方 HTTPS 下載後量得，不等同發布者簽章。

完成後包含：

- `Runtime/`：Python 3.12.10 embedded、CTranslate2 4.8.2、SentencePiece 0.2.2、NumPy 2.5.3、PyYAML 6.0.3 與 worker。
- `LanguagePacks/*.tlpack`：中英兩方向的獨立資料 ZIP，無執行碼、無 directory entries，每個 payload 都有大小與 SHA256。
- `Models/{en-zh,zh-en}/`：相同語言包的解包版本，供測試使用。
- `Prerequisites/VC_redist.x64.exe`：官方 Microsoft 簽章的 Visual C++ x64 執行元件安裝器。目標機缺少此元件時需先安裝；建置腳本不會啟動安裝器或改動系統。安裝可能需要管理員權限。
- `resource-build.json`、`verification.json`：來源、實際大小、協定驗證與已知品質問題。

Python 的 `._pth` 僅包含自身目錄及 app-local packages，未開啟 `import site`。啟動使用 `-I`；使用者 `PYTHONHOME`／`PYTHONPATH` 不會決定套件來源。

## Process contract

```text
Runtime/python.exe -I Runtime/translate.py
```

stdin 接收一行 UTF-8 JSON：`text`、絕對路徑 `modelPath`、`sourceLanguage`、`targetLanguage`。`modelPath` 是 `manifest.json` 所在的語言包根目錄。可用方向為 `en → zh`、`zh → en`。

stdout 中途回傳 `{ "progress": 0, "total": 2 }`，最後一行為 `{ "text": "..." }`；失敗則 `{ "error": "..." }` 與非零 exit code。Host 應終止 process tree 以取消工作，不應顯示半成品為完整翻譯。程式不寫翻譯歷史，也不將 native exception 的原文或路徑透出。

每次上限 20,000 字元，依換行與標點切段後以最多 256 個 source tokens 分塊，偏好單字邊界。換行與行首／尾空白保留；單一超長識別字可能在 subword 邊界切開。模型到達硬性輸出限制卻沒有結束符號時回報失敗，不冒充完整譯文。

## 實測與已知限制

2026-10-03 在目前開發機執行 11 個 worker 單元測試、10 個真實 worker／錯誤處理檢查，以及兩個 ZIP 的完整 SHA256 清單驗證。`verify_translamp_runtime.py` 同時檢查獨立 Python 環境及 socket audit hook。這些是 runtime／協定測試，沒有替代人工翻譯品質驗收，也沒有做全機封包擷取。

已修正 `SentencePiece.decode` 對這組 Argos 詞彙表可能留下 `▁` 的相容問題。只正規化這個分詞標記，不把一般 `_` 改成空白；使用者原文刻意包含 `▁` 的完整往返保真尚未支援。

**已知模型品質缺陷，正式發布前需改善或明確處理：**

```text
原文：Please check the network cable.
譯文：请检查access-date=中的日期值 (帮助) 网络电缆.
```

模型會憑空插入與原意無關的日期欄位片段。已比較原始 beam=2／length penalty=1 與 Argos 的 beam=4／length penalty=0.2，兩者均重現；沒有以字串特判刪除內容。完整自行撰寫的 40 組工程與一般中英例句可重跑：

```powershell
artifacts/translamp/Runtime/python.exe -I -B tests/runtime/sample_translamp_quality.py --resources artifacts/translamp
```

輸出為 `quality-samples.json`，逐句保留原文、譯文與耗時，內容不含使用者資料。2026-10-03 的 40 個 process 均正常回傳，但人工檢視另確認以下缺陷，已另存 `quality-review.json`：

| 樣本 | 實際缺陷 |
| --- | --- |
| `en-zh-02` | 插入 `access-date=中的日期值 (帮助)` 幻覺文字。 |
| `zh-en-04` | 原文 `3000 ms` 變為 `30000 ms`，數值錯誤。 |
| `zh-en-05` | IP `192.168.1.10` 變為 `192168.1.10`，並省略 port `502`。 |
| `en-zh-11` | 檔案路徑 `C:\Logs\alarm_2026.txt` 被加入空白。 |
| `en-zh-13` | 識別字 `retry_count` 被翻譯為 `重试_ 计数`。 |
| `en-zh-06`、`zh-en-10` | `motor` 被譯成「运动」，「韌體」被譯成 `body`。 |
| `zh-en-20` | 「明天」的時間資訊遺失。 |

上述問題均未以硬編碼輸出掩蓋，不應把 40 次成功執行當作 40 句語意正確。方向 `en-zh` 輸出簡體中文；繁體中文輸入已有實例通過，但不是完整繁體模型品質驗收。技術 token、單位與檔案路徑的正確性不保證。

只有一台現代開發機的量測；乾淨 Windows VM、舊 CPU、SSE4.1 及 4 GB RAM 尚未驗證。CPU 支援條件參閱 [CTranslate2 hardware support](https://opennmt.net/CTranslate2/hardware_support.html)。完整第三方 native binary 授權盤點及 Python 安全維護版本更新仍是正式發布門檻。

## 來源與授權

[Argos 官方索引](https://github.com/argosopentech/argospm-index) 提供兩個模型的下載位置。實際 `1.9` 包內 README 標示原始 OPUS-MT 模型採 CC-BY 4.0，已將其原文作者／論文引用保留在 `NOTICE`。另依 [Argos 維護者對模型授權的說明](https://github.com/argosopentech/argos-translate/issues/533) 附加 Argos MIT 許可；manifest 明列 `CC-BY-4.0 AND MIT`，不以程式碼 MIT 取代來源權重的 CC-BY 條件。

`licenses/` 保存完整 [CC-BY 4.0](https://creativecommons.org/licenses/by/4.0/legalcode.txt)、Argos MIT、[CTranslate2 MIT](https://github.com/OpenNMT/CTranslate2/blob/v4.8.2/LICENSE)、[SentencePiece Apache-2.0](https://github.com/google/sentencepiece/blob/v0.2.2/LICENSE)。NumPy／PyYAML 上游 notices 隨 wheel 保留。Python 與各 native runtime 的散布條件仍須依各自文件遵守；本原型不代表 Store 上架或正式發布授權審查完成。
