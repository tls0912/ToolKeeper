# 003｜Encoding Tool（工作名稱）

## 文件狀態

- 產品代號：**003**。
- 正式產品名稱：**待定**。
- 目前工作名稱：**Encoding Tool**。
- 更新日期：2026-09-25。
- 狀態：**產品方向已定，細部功能與 UI 尚未定案**。
- 本階段先確認產品要解決的問題與核心能力，不提前凍結完整 V1 規格。

## 產品定位

003 是一款 Windows 上的 **文字編碼、位元組與文字格式診斷／轉換工具**。

它不是一般文字編輯器，也不是大型 IDE 的附屬功能集合。

主要解決以下實際問題：

- 不確定文字檔實際使用哪種 Encoding。
- 開啟舊檔案時遇到 Big5、ANSI、UTF-8、BOM 等造成的亂碼。
- 需要在不同 Encoding 之間安全轉換檔案。
- 需要確認 CRLF / LF、BOM 等文字檔格式。
- 工程開發或通訊除錯時，需要在 **Text 與 Bytes** 之間快速雙向轉換。
- 希望在轉換前先看清楚結果，而不是直接改檔後才發現內容損壞。

核心方向：

> **把文字、Encoding、Bytes 之間發生了什麼，一次看清楚。**

## 主要使用情境

003 優先服務 Windows 與工程環境中仍大量存在的文字格式問題，尤其包含：

- UTF-8
- UTF-8 BOM
- UTF-16 LE / BE
- Windows ANSI Code Page
- Big5
- 其他常見 Legacy Encoding

典型情境：

1. 收到一份 CSV / TXT / LOG，打開後中文亂碼，不知道原始編碼。
2. 舊設備或舊系統輸出 Big5 / ANSI 檔案，需要轉成 UTF-8。
3. 想確認檔案有沒有 BOM。
4. 想確認檔案使用 CRLF 還是 LF。
5. 通訊或 Protocol 開發時，需要把文字直接轉成 Hex Bytes。
6. 拿到一串 Hex Bytes，希望指定 Encoding 後還原成人類可讀文字。
7. 轉檔前希望知道是否存在無法表示或可能遺失的字元。

## 核心能力

目前方向分為三個相互關聯的功能區。

### 1. File

針對實體文字檔進行診斷與轉換。

預計能力：

- 開啟文字檔。
- 偵測可能的 Encoding。
- 顯示 Encoding 判斷結果與信心程度（若底層偵測器可提供）。
- 顯示 BOM 狀態。
- 顯示 Line Ending：
  - CRLF
  - LF
  - CR
  - Mixed
- 預覽目前內容。
- 指定來源 Encoding 重新解碼預覽。
- 轉換為指定 Encoding。
- 轉換前顯示結果預覽。
- 儘量在寫入前檢查是否存在無法表示或可能遺失的字元。

原則：

> **先診斷、再預覽、最後才寫入。**

不得把「猜到一個 Encoding」直接等同於「一定正確」。

### 2. Text

針對使用者貼上的文字進行 Encoding 與格式檢視。

預計能力：

- 輸入／貼上文字。
- 選擇 Encoding。
- 查看對應 Byte 表示。
- 查看字元數與 Byte 數。
- 顯示特殊控制字元，例如：
  - CR
  - LF
  - TAB
  - NUL
- 基本 Line Ending 轉換：
  - CRLF → LF
  - LF → CRLF
- BOM 與文字格式相關資訊可視化。

Text 區不是要發展成完整文字編輯器。

### 3. Bytes

提供 **Text ↔ Bytes 雙向轉換**。

#### Text → Bytes

輸入文字後，可依指定 Encoding 顯示：

- Hex
- Decimal
- Binary

預計至少支援常用表示格式：

```text
41 42 43
```

```text
0x41 0x42 0x43
```

```text
41,42,43
```

```text
\x41\x42\x43
```

也可顯示 Decimal 與 Binary。

範例：

```text
ABC中文
```

使用 UTF-8 時：

```text
41 42 43 E4 B8 AD E6 96 87
```

#### Bytes → Text

輸入 Byte 資料後，指定 Encoding 還原文字。

例如：

```text
41 42 43 E4 B8 AD E6 96 87
```

指定 UTF-8：

```text
ABC中文
```

需能處理常見 Byte 輸入格式，並在輸入格式不合法時指出錯誤位置，而不是靜默忽略。

## 工程使用情境

003 應特別考慮通訊與設備整合工作常見需求。

例如文字：

```text
ABC\r\n
```

應能顯示：

```text
41 42 43 0D 0A
```

並能辨識／呈現常見控制字元，例如：

- NUL = `00`
- STX = `02`
- ETX = `03`
- TAB = `09`
- LF = `0A`
- CR = `0D`

這些能力仍屬於「Text / Encoding / Bytes 的可視化與轉換」，不代表 003 要發展成 Serial Port、Socket、PLC 或 Protocol 測試軟體。

## 與一般文字清理工具的界線

曾考慮過獨立的文字清理工具，例如：

- Trim
- Remove blank lines
- Deduplicate
- Sort
- Upper / Lower
- Prefix / Suffix
- Tabs / Spaces

目前不把這類功能作為 003 的核心。

理由：

- 市面上已有大量免費 Text Cleaner。
- 容易讓產品變成沒有明確邊界的文字瑞士刀。
- 與 Encoding / Bytes 問題相比，產品差異化較弱。

但與文字格式診斷直接相關的能力可以納入，例如：

- CRLF / LF 轉換。
- BOM 處理。
- TAB / Space 的可視化。
- 控制字元顯示。

原則：

> **只收與 Encoding、文字格式、Bytes 直接相關的文字處理功能。**

## 市場切入點

現有工具大致分為：

1. Notepad++ 等綜合文字編輯器。
2. 線上 Encoding Detector / Converter。
3. 單一功能的 Text Cleaner。
4. 工程師使用的 Hex / Byte 轉換網站或小工具。

003 不以「功能比它們全部更多」為目標。

產品切入點是把以下流程集中在一個簡單、離線的 Windows 工具中：

```text
Detect
  ↓
Inspect
  ↓
Preview
  ↓
Convert
```

加上：

```text
Text ↔ Bytes
```

尤其優先考慮：

- Windows 使用者。
- 亞洲 Legacy Encoding。
- Big5 / ANSI / UTF 系列共存環境。
- 離線環境。
- 工程與設備整合工作。

## V1 初步邊界

### V1 優先考慮

- File Encoding 偵測。
- Encoding 手動指定。
- BOM 顯示。
- CRLF / LF / Mixed 顯示。
- Encoding 轉換前預覽。
- Encoding 轉換。
- Text → Bytes。
- Bytes → Text。
- Hex / Decimal / Binary 顯示。
- 常見 Byte 輸入格式解析。
- 常見控制字元可視化。
- 全部本機處理。

### V1 暫不納入

- 完整文字編輯器。
- Hex Editor。
- Binary File Editor。
- Serial Port Terminal。
- Socket Client。
- PLC Protocol Tool。
- Base64 / URL / JSON / XML 等與 Encoding 核心無關的大型 Converter 集合。
- AI。
- Cloud。
- Account / Login。
- Plugin System。
- 大型批次處理流程。

上述不是永久排除；目前只是防止 003 在產品方向尚未成熟前膨脹。

## 隱私與離線原則

003 的處理預設全部在本機完成。

原則：

- 不需要登入。
- 不依賴雲端。
- 不上傳使用者文字。
- 不上傳檔案內容。
- 不需要 AI 才能完成核心功能。

這一點對工程文件、設備 Log、內部 CSV 與離線產線環境尤其重要。

## 技術方向

技術方案尚未凍結。

目前原則：

- Windows 原生桌面工具。
- 延續 ToolKeeper 微型工具的簡單架構。
- 不因 Encoding 偵測需求自行重新發明成熟演算法。
- 優先評估成熟的 Charset / Encoding Detection Library。
- Encoding Detection 必須被視為「推測」，UI 不得把不確定結果包裝成絕對正確。
- Text / Bytes 轉換應使用平台標準 Encoding 能力並對無法表示字元提供明確提示。

## 產品原則

003 必須維持工具番產品的共同方向：

- 問題小而明確。
- 開啟就能用。
- 不要求帳號。
- 不依賴雲端。
- 不做大型工作區。
- 不為「以後也許會用」提前建立龐大架構。
- 不追求功能數量。
- 優先解決真正遇過的麻煩。

目前一句話定義：

> **003 是一款把文字檔 Encoding、文字格式與 Bytes 之間的關係看清楚，並安全完成雙向轉換的 Windows 小工具。**

## 待定項目

後續規劃再確認：

- 正式產品名稱。
- V1 支援的 Encoding 清單。
- Encoding Detection Library。
- 是否支援批次檔案轉換。
- File / Text / Bytes 三區的實際 UI。
- 轉換時採覆寫、另存新檔或兩者皆提供。
- Large File 行為。
- Microsoft Store 售價與上架方式。

## 文件編號規則

本文件為產品 **003**。

正式名稱尚未決定，但產品編號已固定。

未來命名後：

```text
003_正式名稱.md
```

只修改檔名與文件標題，不重新排序產品編號。
