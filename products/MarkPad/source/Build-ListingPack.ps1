#Requires -Version 5.1
[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem
$packRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$info = Get-Content -LiteralPath (Join-Path $packRoot 'product-info.json') -Raw -Encoding UTF8 | ConvertFrom-Json
$captions = Get-Content -LiteralPath (Join-Path $packRoot 'screenshots/captions.json') -Raw -Encoding UTF8 | ConvertFrom-Json
$report = [Collections.Generic.List[object]]::new()
function Require([bool]$Condition, [string]$Message) {
    if (-not $Condition) { throw $Message }
    $report.Add([pscustomobject]@{ check=$Message; passed=$true })
}
function Html([string]$Text) { return [Net.WebUtility]::HtmlEncode($Text) }
function Read-Text([string]$Path) { return [IO.File]::ReadAllText((Join-Path $packRoot $Path)).TrimEnd() }
$sections = [Text.StringBuilder]::new()
foreach ($locale in $info.languages) {
    $folder = "listing/$locale"
    $description = Read-Text "$folder/description.txt"
    $short = Read-Text "$folder/short-description.txt"
    $privacy = Read-Text "$folder/privacy-policy.txt"
    $features = @(Get-Content -LiteralPath (Join-Path $packRoot "$folder/features.txt") -Encoding UTF8 | Where-Object { $_.Trim() })
    $keywords = @(Get-Content -LiteralPath (Join-Path $packRoot "$folder/keywords.txt") -Encoding UTF8 | Where-Object { $_.Trim() })
    $requirements = @(Get-Content -LiteralPath (Join-Path $packRoot "$folder/system-requirements.txt") -Encoding UTF8 | Where-Object { $_.Trim() })
    Require ($description.Length -gt 0 -and $description.Length -le 10000) "$locale description nonempty and <=10000 characters"
    Require ($description -notmatch 'https?://|<[^>]+>') "$locale description has no URLs or HTML"
    Require ($short.Length -gt 0 -and $short.Length -lt 270) "$locale short description below 270 characters"
    Require ($privacy.Length -gt 0 -and $privacy -notmatch '\{\{[^}]+\}\}') "$locale privacy policy text present"
    Require ($features.Count -ge 1 -and $features.Count -le 20 -and @($features | Where-Object { $_.Length -gt 200 -or $_ -match '^[-*•]' }).Count -eq 0) "$locale features satisfy count/length/no-bullet rules"
    Require ($keywords.Count -le 7 -and @($keywords | Where-Object { $_.Length -gt 40 }).Count -eq 0 -and @((($keywords -join ' ') -split '\s+') | Where-Object { $_ }).Count -le 21) "$locale keywords satisfy 7 phrases/40 chars/21 words limits"
    Require ((Read-Text "$folder/release-notes-update-only.txt").Length -le 1500) "$locale update-only notes below 1500 characters"
    Require ($requirements.Count -le 11 -and @($requirements | Where-Object { $_.Length -gt 200 }).Count -eq 0) "$locale system requirements satisfy listing limits"
    $captionItems = @($captions.$locale)
    Require ($captionItems.Count -eq 5) "$locale has five screenshot captions"
    $captionLines = foreach ($item in $captionItems) {
        Require ($item.caption.Length -gt 0 -and $item.caption.Length -le 200) "$locale/$($item.file) caption <=200 characters"
        "$($item.file)`t$($item.caption)"
    }
    $captionLines | Set-Content -LiteralPath (Join-Path $packRoot "$folder/screenshot-captions.txt") -Encoding UTF8
    $label = switch ($locale) { 'zh-TW' { '繁體中文' } 'en-US' { 'English' } 'ja-JP' { '日本語' } }
    [void]$sections.AppendLine("<section id='$locale'><div class='section-head'><span class='eyebrow'>$locale</span><h2>$label</h2><p>點一下文字框可全選，使用 Ctrl+C 複製。功能與關鍵字每行各填一格。</p></div>")
    foreach ($field in @(
        @{Name='說明 / Description'; File='description.txt'; Text=$description; Rows=13},
        @{Name='簡短說明 / Short description'; File='short-description.txt'; Text=$short; Rows=3},
        @{Name='產品功能 / Features'; File='features.txt'; Text=($features -join "`n"); Rows=12},
        @{Name='搜尋關鍵字 / Keywords'; File='keywords.txt'; Text=($keywords -join "`n"); Rows=7},
        @{Name='其他系統需求'; File='system-requirements.txt'; Text=($requirements -join "`n"); Rows=3},
        @{Name='隱私權原則 / Privacy policy'; File='privacy-policy.txt'; Text=$privacy; Rows=16}
    )) {
        $safe = Html $field.Text
        [void]$sections.AppendLine("<article class='text-card'><div class='row'><h3>$($field.Name)</h3><a href='$folder/$($field.File)' download>純文字檔 ↗</a></div><textarea readonly rows='$($field.Rows)' aria-label='$($field.Name) $locale' onclick='this.select()'>$safe</textarea></article>")
    }
    [void]$sections.AppendLine("<p class='notice'>首次上架「此版本的新增功能 / What’s new」請留白。<a href='$folder/release-notes-update-only.txt'>更新備稿</a>只供後續版本參考。</p><h3>程式截圖 · 1600 × 1000</h3><div class='gallery'>")
    foreach ($item in $captionItems) {
        $file = "screenshots/$locale/$($item.file)"; $safeCaption = Html $item.caption
        [void]$sections.AppendLine("<figure><a href='$file' target='_blank'><img loading='lazy' src='$file' width='1600' height='1000' alt='$safeCaption'></a><figcaption><a href='$file' download>$($item.file) ↗</a><textarea readonly rows='3' aria-label='截圖說明 $locale $($item.file)' onclick='this.select()'>$safeCaption</textarea></figcaption></figure>")
    }
    [void]$sections.AppendLine('</div></section>')
}
$template = @'
<!doctype html>
<html lang="zh-Hant"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width, initial-scale=1"><title>汗青 - Microsoft Store 產品說明包</title>
<style>
:root{color-scheme:light;--paper:#f4f1e8;--ink:#263c30;--muted:#637264;--line:#d5d2c4}*{box-sizing:border-box}body{margin:0;background:var(--paper);color:var(--ink);font-family:system-ui,"Microsoft JhengHei",sans-serif;line-height:1.65}header{padding:60px max(24px,calc((100vw - 1180px)/2));background:#243c2e;color:#f4f1e8;border-bottom:8px solid #bfad78}.brand{display:flex;gap:24px;align-items:center}.brand img{width:80px;height:80px}h1{font-size:clamp(28px,4vw,46px);margin:4px 0}header p{margin:8px 0;color:#d1d9c9}.eyebrow{text-transform:uppercase;letter-spacing:.16em;font-size:13px;font-weight:650}.badge{display:inline-block;border:1px solid #93a28b;border-radius:100px;padding:4px 14px;font-size:13px;margin-top:12px}nav{position:sticky;top:0;background:#e9e4d7eF;backdrop-filter:blur(10px);border-bottom:1px solid var(--line);display:flex;justify-content:center;gap:24px;padding:16px;z-index:1}a{color:inherit;text-underline-offset:4px}nav a{text-decoration:none;font-weight:650}main{max-width:1244px;padding:30px 32px;margin:auto}section{scroll-margin-top:80px;border-bottom:1px solid var(--line);padding:34px 0 50px}h2{font-size:32px;margin:6px 0}h3{font-size:18px;margin:14px 0}.section-head{margin-bottom:28px}.section-head p{color:var(--muted)}.row{display:flex;gap:16px;align-items:center;justify-content:space-between}.row a{font-size:13px;white-space:nowrap}.text-card{padding:12px 24px 24px;border:1px solid var(--line);background:#fffdf6;border-radius:12px;margin:18px 0}textarea{display:block;width:100%;resize:vertical;font-family:inherit;font-size:15px;line-height:1.65;color:var(--ink);padding:16px;border:1px solid var(--line);border-radius:6px;background:#faf9f5}textarea:focus{outline:2px solid #678467;outline-offset:2px}.notice{border-left:4px solid #8f7950;background:#e9e2d3;padding:16px 20px}.gallery{display:grid;grid-template-columns:repeat(2,minmax(0,1fr));gap:24px}figure{margin:0;border:1px solid var(--line);border-radius:10px;overflow:hidden;background:#fffdf6}figure img{display:block;width:100%;height:auto}figcaption{padding:16px;font-size:14px}figcaption textarea{margin-top:12px}.assets{display:grid;grid-template-columns:repeat(3,minmax(0,1fr));gap:24px}.assets img{max-height:230px;object-fit:contain;padding:14px;background:#eceadf}.hero{grid-column:span 2}.hero img{max-height:none;padding:0}.fine{color:var(--muted);font-size:14px}footer{padding:32px;text-align:center;color:var(--muted)}@media(max-width:750px){main{padding:20px 16px}header{padding:32px 20px}.brand img{width:56px;height:56px}.brand{gap:15px}nav{gap:16px;font-size:14px}.gallery,.assets{grid-template-columns:1fr}.hero{grid-column:auto}.text-card{padding:8px 16px 18px}.row{align-items:flex-start}}
</style></head><body>
<header><div class="brand"><img src="assets/app-icon-300.png" alt="汗青圖示"><div><span class="eyebrow">Microsoft Store · Listing kit</span><h1>汗青 - Markdown Writer</h1><p>三語產品說明、實際程式截圖與商店圖資</p></div></div><span class="badge">MSIX __PACKAGE_VERSION__ · 竹材 M · Windows x64</span></header>
<nav aria-label="素材語言"><a href="#zh-TW">繁體中文</a><a href="#en-US">English</a><a href="#ja-JP">日本語</a><a href="#assets">圖示與宣傳圖</a></nav>
<main><p class="notice">依語言將文案與截圖放入 Partner Center。首次上架的「此版本的新增功能」留白。<a href="README.md">欄位對照與使用說明</a> · <a href="submission-notes.md">runFullTrust 與送審備註</a></p>
__SECTIONS__
<section id="assets"><span class="eyebrow">Shared artwork</span><h2>圖示與宣傳圖</h2><p>這些圖資可供三語共用；程式截圖請使用上方各語言版本。</p><div class="assets">
<figure><a href="assets/app-icon-300.png" download><img src="assets/app-icon-300.png" alt="商店應用程式圖示"></a><figcaption><strong>App tile icon · 300 × 300</strong><br>建議上傳到商店 1:1 App tile icon。</figcaption></figure>
<figure class="hero"><a href="assets/optional-art/hero-art-1920x1080.png" download><img src="assets/optional-art/hero-art-1920x1080.png" alt="竹子與宣紙書頁宣傳圖"></a><figcaption><strong>Super hero art · 1920 × 1080</strong><br>選填宣傳圖。無標題、文字或程式 UI。</figcaption></figure>
<figure><a href="assets/optional-art/box-art-1080.png" download><img loading="lazy" src="assets/optional-art/box-art-1080.png" alt="備用方形品牌圖"></a><figcaption><strong>Box art · 1080 × 1080</strong><br>備用素材；主要供遊戲，汗青不必填。</figcaption></figure>
<figure><a href="assets/optional-art/poster-art-720x1080.png" download><img loading="lazy" src="assets/optional-art/poster-art-720x1080.png" alt="備用直式品牌圖"></a><figcaption><strong>Poster art · 720 × 1080</strong><br>備用素材；主要供遊戲，汗青不必填。</figcaption></figure>
</div><p class="fine">23 張竹材 M 圖示在 assets/msix-package，已與 __PACKAGE_VERSION__ 套件核對一致。原始 SVG／ICO 在 assets/source-brand。</p></section>
<section><h2>送出前核對</h2><p>隱私權原則已有三語文字，依發行者要求不列聯絡 Email。依目前 Partner Center 畫面，可選「提供隱私權原則文字」後貼入完整內容；若選 URL，仍須填發行者實際公開的政策網址。<a href="privacy-submission.md">隱私權欄位填寫說明</a>。此包沒有代填網址，也沒有執行商店提交。</p><p>實際 Store 授權、WACK 與已安裝套件仍需驗收。文案中的 7 天免費試用依發行者提供的商店設定。</p><p class="fine">規格來源：<a href="https://learn.microsoft.com/en-us/windows/apps/publish/publish-your-app/msix/add-and-edit-store-listing-info">MSIX 商店文案</a> · <a href="https://learn.microsoft.com/en-us/windows/apps/publish/publish-your-app/msix/screenshots-and-images">圖片規格</a></p></section>
</main><footer>汗青 · 本機素材包 · 三語各 5 張實際介面截圖</footer></body></html>
'@
$template.Replace('__SECTIONS__', $sections.ToString()).Replace('__PACKAGE_VERSION__', (Html $info.packageVersion)) | Set-Content -LiteralPath (Join-Path $packRoot 'index.html') -Encoding UTF8

$images = foreach ($file in Get-ChildItem -LiteralPath $packRoot -Filter *.png -File -Recurse) {
    $relative = $file.FullName.Substring($packRoot.Length + 1).Replace('\','/')
    $picture = [Drawing.Image]::FromFile($file.FullName)
    try {
        Require ($file.Length -lt 50 * 1024 * 1024) "$relative below 50 MB"
        if ($relative.StartsWith('screenshots/')) {
            Require ($picture.Width -eq 1600 -and $picture.Height -eq 1000) "$relative is 1600x1000"
        }
        [pscustomobject]@{file=$relative; width=$picture.Width; height=$picture.Height; bytes=$file.Length}
    } finally { $picture.Dispose() }
}
foreach ($expected in @(
    @{File='assets/app-icon-300.png';W=300;H=300},
    @{File='assets/optional-art/hero-art-1920x1080.png';W=1920;H=1080},
    @{File='assets/optional-art/box-art-1080.png';W=1080;H=1080},
    @{File='assets/optional-art/poster-art-720x1080.png';W=720;H=1080}
)) {
    $found = @($images | Where-Object { $_.file -eq $expected.File })
    Require ($found.Count -eq 1 -and $found[0].width -eq $expected.W -and $found[0].height -eq $expected.H) "$($expected.File) correct Store dimensions"
}
Require (@($images | Where-Object { $_.file.StartsWith('screenshots/') }).Count -eq 15) '15 localized screenshots present'
Require (@($images | Where-Object { $_.file.StartsWith('assets/msix-package/') }).Count -eq 23) '23 MSIX package logos present'
foreach ($locale in $info.languages) {
    Require (@($images | Where-Object { $_.file.StartsWith("screenshots/$locale/") }).Count -eq 5) "$locale five screenshots present"
}
# Verify every local HTML image and download link exists in the delivered folder.
$html = [IO.File]::ReadAllText((Join-Path $packRoot 'index.html'))
foreach ($match in [regex]::Matches($html, '(?:href|src)=["'']([^"'']+)["'']')) {
    $target = [Net.WebUtility]::HtmlDecode($match.Groups[1].Value)
    if ($target -match '^(https?://|#)') { continue }
    Require (Test-Path -LiteralPath (Join-Path $packRoot $target) -PathType Leaf) "preview link exists: $target"
}
Require (@(Get-ChildItem -LiteralPath $packRoot -File -Recurse | Where-Object { $_.FullName -match '[\\/](bin|obj|WebView2)[\\/]|\.pfx$|\.dll$|\.exe$' }).Count -eq 0) 'no runtime profiles, binaries or private signing material in the listing kit'
$verification = Join-Path $packRoot 'verification'
[IO.Directory]::CreateDirectory($verification) | Out-Null
[pscustomobject]@{generatedUtc=[DateTime]::UtcNow.ToString('o'); passed=$true; checks=$report; images=@($images)} |
    ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $verification 'validation.json') -Encoding UTF8
$files = foreach ($file in Get-ChildItem -LiteralPath $packRoot -File -Recurse | Sort-Object FullName) {
    if ($file.FullName -eq (Join-Path $packRoot 'file-manifest.json')) { continue }
    [pscustomobject]@{file=$file.FullName.Substring($packRoot.Length + 1).Replace('\','/');bytes=$file.Length;sha256=(Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash}
}
[pscustomobject]@{product=$info.productName; version=$info.packageVersion; files=@($files); excludes='file-manifest.json (self)'} |
    ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $packRoot 'file-manifest.json') -Encoding UTF8
$zipPath = Join-Path (Split-Path $packRoot -Parent) "MarkPad-StoreListing-$($info.packageVersion).zip"
$temporary = $zipPath + '.' + [Guid]::NewGuid().ToString('N') + '.tmp'
$outputArchive = [IO.Compression.ZipFile]::Open($temporary, [IO.Compression.ZipArchiveMode]::Create)
try {
    foreach ($file in Get-ChildItem -LiteralPath $packRoot -File -Recurse | Sort-Object FullName) {
        $entryName = 'MarkPad/' + $file.FullName.Substring($packRoot.Length + 1).Replace('\','/')
        [void][IO.Compression.ZipFileExtensions]::CreateEntryFromFile($outputArchive, $file.FullName, $entryName, [IO.Compression.CompressionLevel]::Optimal)
    }
} finally { $outputArchive.Dispose() }
Move-Item -LiteralPath $temporary -Destination $zipPath -Force
$archive = [IO.Compression.ZipFile]::OpenRead($zipPath)
try {
    Require ($archive.Entries.Count -eq @($files).Count + 1) 'ZIP contains every listing-kit file exactly once'
    foreach ($file in $files) {
        $entry = $archive.GetEntry('MarkPad/' + $file.file)
        if ($null -eq $entry) { throw "Missing ZIP entry: $($file.file)" }
        $stream = $entry.Open()
        $hasher = [Security.Cryptography.SHA256]::Create()
        try { $digest = [BitConverter]::ToString($hasher.ComputeHash($stream)).Replace('-', '') }
        finally { $stream.Dispose(); $hasher.Dispose() }
        if ($digest -ne $file.sha256) { throw "ZIP content hash mismatch: $($file.file)" }
    }
    Require ($null -ne $archive.GetEntry('MarkPad/file-manifest.json')) 'ZIP contains its file manifest'
} finally { $archive.Dispose() }
$hash = (Get-FileHash -LiteralPath $zipPath -Algorithm SHA256).Hash
"$hash  $([IO.Path]::GetFileName($zipPath))" | Set-Content -LiteralPath ($zipPath + '.sha256') -Encoding ASCII
Write-Output "Verified $($report.Count) checks; $(@($images).Count) PNGs; $(@($files).Count + 1) files."
Write-Output "Listing kit: $zipPath"
Write-Output "SHA256: $hash"
