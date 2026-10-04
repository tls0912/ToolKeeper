#requires -Version 7.0
[CmdletBinding()]
param([string]$OutputDirectory)

# A bounded anonymous source probe, not a production provider or CSV importer.
# No tokens, device impersonation, parallel requests, or retries.
$ErrorActionPreference = 'Stop'
$evidenceRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../artifacts/histolens-source-verification'))
if (!$OutputDirectory) { $OutputDirectory = Join-Path $evidenceRoot ('finmind-' + (Get-Date -Format 'yyyyMMdd-HHmmss-fff')) }
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
if (!$OutputDirectory.StartsWith($evidenceRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Evidence must stay under ignored artifacts/histolens-source-verification.'
}
if (Test-Path -LiteralPath $OutputDirectory) { throw 'Choose a fresh directory; evidence is never overwritten.' }
New-Item -ItemType Directory -Path $OutputDirectory | Out-Null
$queries = [ordered]@{
    'price-2330' = 'dataset=TaiwanStockPrice&data_id=2330&start_date=2016-01-01&end_date=2025-12-31'
    'price-6488' = 'dataset=TaiwanStockPrice&data_id=6488&start_date=2016-01-01&end_date=2025-12-31'
    'calendar' = 'dataset=TaiwanStockTradingDate'
    'identity' = 'dataset=TaiwanStockInfo'
    'dividend-2330' = 'dataset=TaiwanStockDividendResult&data_id=2330&start_date=2021-01-01&end_date=2025-12-31'
    'dividend-6488' = 'dataset=TaiwanStockDividendResult&data_id=6488&start_date=2021-01-01&end_date=2025-12-31'
    'per-2330' = 'dataset=TaiwanStockPER&data_id=2330&start_date=2026-09-28&end_date=2026-10-02'
    'per-6488' = 'dataset=TaiwanStockPER&data_id=6488&start_date=2026-09-28&end_date=2026-10-02'
    'no-price-2317' = 'dataset=TaiwanStockPrice&data_id=2317&start_date=2025-07-29&end_date=2025-07-31'
    'no-price-9929' = 'dataset=TaiwanStockPrice&data_id=9929&start_date=2025-07-31&end_date=2025-07-31'
    'capital-reduction-2327' = 'dataset=TaiwanStockCapitalReductionReferencePrice&data_id=2327&start_date=2017-01-01&end_date=2017-12-31'
    'par-value' = 'dataset=TaiwanStockParValueChange&start_date=2021-01-01&end_date=2025-12-31'
    'split' = 'dataset=TaiwanStockSplitPrice'
    'suspended' = 'dataset=TaiwanStockSuspended&start_date=2025-07-30&end_date=2025-07-30'
}
$requests = [Collections.Generic.List[object]]::new()
foreach ($query in $queries.GetEnumerator()) {
    $entry = [ordered]@{
        key = $query.Key; url = 'https://api.finmindtrade.com/api/v4/data?' + $query.Value
        requestedAtUtc = [DateTimeOffset]::UtcNow.ToString('o'); downloadedAtUtc = $null
        lastSuccessfulDownloadAtUtc = $null; httpStatus = $null; apiStatus = $null
        contentType = $null; etag = $null; lastModified = $null; bytes = $null; sha256 = $null
        count = $null; fields = @(); earliest = $null; latest = $null; nonIsoDateRows = $null; error = $null
    }
    try {
        $response = Invoke-WebRequest -Uri $entry.url -TimeoutSec 40 -SkipHttpErrorCheck
        $entry.httpStatus = [int]$response.StatusCode
        $entry.contentType = [string]$response.Headers['Content-Type']
        $entry.etag = [string]$response.Headers['ETag']
        $entry.lastModified = [string]$response.Headers['Last-Modified']
        $raw = if ($response.Content -is [byte[]]) { $response.Content } else { [Text.Encoding]::UTF8.GetBytes([string]$response.Content) }
        $file = Join-Path $OutputDirectory ($query.Key + '.json')
        [IO.File]::WriteAllBytes($file, $raw)
        $entry.bytes = $raw.Length
        $entry.sha256 = (Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash
        $entry.downloadedAtUtc = [DateTimeOffset]::UtcNow.ToString('o')
        if ($entry.httpStatus -ne 200) { throw "HTTP $($entry.httpStatus); no retry or bypass" }
        if ($entry.contentType -notmatch 'json') { throw 'Unexpected format; no challenge is bypassed' }
        $json = [Text.Encoding]::UTF8.GetString($raw) | ConvertFrom-Json
        $entry.apiStatus = $json.status
        if ($json.status -ne 200 -or $null -eq $json.data) { throw "API status $($json.status): $($json.msg)" }
        $rows = @($json.data)
        $entry.count = $rows.Count
        if ($rows.Count) {
            $entry.fields = @($rows[0].PSObject.Properties.Name)
            $dates = @($rows.date | Where-Object { $_ -match '^\d{4}-\d{2}-\d{2}$' } | Sort-Object)
            $entry.nonIsoDateRows = $rows.Count - $dates.Count
            if ($dates.Count) { $entry.earliest = $dates[0]; $entry.latest = $dates[-1] }
        }
        $entry.lastSuccessfulDownloadAtUtc = $entry.downloadedAtUtc
    } catch { $entry.error = $_.Exception.Message }
    $requests.Add([pscustomobject]$entry)
    $requests | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $OutputDirectory 'requests.json') -Encoding utf8
    Write-Output ($entry | ConvertTo-Json -Depth 5 -Compress)
    if ($entry.error) { Write-Warning 'Stopped on first failed request; remaining resources are untested.'; break }
    Start-Sleep -Seconds 1
}
Write-Output "EvidenceDirectory=$OutputDirectory"
