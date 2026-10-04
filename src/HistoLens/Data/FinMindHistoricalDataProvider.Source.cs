using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace HistoLens.Data;

public sealed partial class FinMindHistoricalDataProvider
{
    private async Task<DownloadedResponse> GetResponseAsync(string dataset, IReadOnlyDictionary<string, string>? parameters,
        CancellationToken cancellationToken)
    {
        var url = Endpoint + "?dataset=" + Uri.EscapeDataString(dataset);
        if (parameters is not null)
            url += string.Concat(parameters.Select(pair => "&" + Uri.EscapeDataString(pair.Key) + "=" + Uri.EscapeDataString(pair.Value)));
        await _requestGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_requestInterval > TimeSpan.Zero) await _delay(_requestInterval, cancellationToken).ConfigureAwait(false);
            using var timeout = new CancellationTokenSource(_requestTimeout, _timeProvider);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, url);
                request.Headers.Accept.ParseAdd("application/json");
                request.Headers.UserAgent.ParseAdd("ToolKeeper-HistoLens/0.1 (+https://github.com/tls0912/ToolKeeper)");
                using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, linked.Token).ConfigureAwait(false);
                if (!response.IsSuccessStatusCode)
                    throw new HttpRequestException($"FinMind {dataset} 回覆 HTTP {(int)response.StatusCode}；未重試或繞過來源限制。", null, response.StatusCode);
                if (response.Content.Headers.ContentType?.MediaType is not ("application/json" or "text/json"))
                    throw InvalidSource(dataset, "回應不是 JSON 格式，可能為維護或限制頁面");
                if (response.Content.Headers.ContentLength > MaximumResponseBytes)
                    throw InvalidSource(dataset, "回應超過 8 MiB 上限");
                await using var input = await response.Content.ReadAsStreamAsync(linked.Token).ConfigureAwait(false);
                using var output = new MemoryStream();
                var buffer = new byte[16 * 1024];
                while (true)
                {
                    var count = await input.ReadAsync(buffer, linked.Token).ConfigureAwait(false);
                    if (count == 0) break;
                    if (output.Length + count > MaximumResponseBytes) throw InvalidSource(dataset, "回應超過 8 MiB 上限");
                    output.Write(buffer, 0, count);
                }
                if (output.Length == 0) throw InvalidSource(dataset, "回應空白");
                string json;
                try { json = new UTF8Encoding(false, true).GetString(output.ToArray()); }
                catch (DecoderFallbackException exception) { throw new InvalidDataException($"FinMind {dataset} 不是合法 UTF-8 JSON。", exception); }
                return new(json, url, _timeProvider.GetUtcNow());
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                throw new TimeoutException($"FinMind {dataset} 下載超過 {_requestTimeout.TotalSeconds:0} 秒或 HTTP 連線逾時；本次未完成。");
            }
        }
        finally { _requestGate.Release(); }
    }

    private static JsonDocument ParseResponse(DownloadedResponse response, string dataset, List<SourceResponseEvidence> evidence)
    {
        JsonDocument document;
        try { document = JsonDocument.Parse(response.RawJson); }
        catch (JsonException exception) { throw new InvalidDataException($"FinMind {dataset} 未回傳合法 JSON。", exception); }
        try
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("status", out var status) ||
                status.ValueKind != JsonValueKind.Number || !status.TryGetInt32(out var code))
                throw InvalidSource(dataset, "缺少 API 狀態碼");
            if (code != 200)
                throw new HttpRequestException($"FinMind {dataset} 回覆 API 狀態 {code}；未重試或繞過來源限制。", null,
                    code is >= 400 and <= 599 ? (HttpStatusCode)code : null);
            if (!root.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array)
                throw InvalidSource(dataset, "缺少 data 資料陣列");
            if (data.EnumerateArray().Any(row => row.ValueKind != JsonValueKind.Object))
                throw InvalidSource(dataset, "資料列不是物件");
            evidence.Add(new(response.ResourceUrl, dataset, response.RetrievedAtUtc, response.Hash, data.GetArrayLength(), response.RawJson));
            // Return a document with the data array as its root, keeping caller-side parsing explicit.
            var result = JsonDocument.Parse(data.GetRawText());
            document.Dispose();
            return result;
        }
        catch { document.Dispose(); throw; }
    }

    private static string ReadString(JsonElement row, string name, string dataset)
    {
        if (!row.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.String)
            throw InvalidSource(dataset, $"缺少文字欄位 {name}");
        return value.GetString()!;
    }

    private static DateOnly ReadDate(JsonElement row, string name, string dataset)
    {
        var value = ReadString(row, name, dataset);
        if (!DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
            throw InvalidSource(dataset, $"{name} 日期格式不符");
        return date;
    }

    private static decimal? ReadNumber(JsonElement row, string name, string dataset)
    {
        if (!row.TryGetProperty(name, out var value)) throw InvalidSource(dataset, $"缺少數值欄位 {name}");
        if (value.ValueKind == JsonValueKind.Null) return null;
        if (value.ValueKind == JsonValueKind.Number && value.TryGetDecimal(out var number)) return number;
        if (value.ValueKind == JsonValueKind.String)
        {
            var text = value.GetString()!.Trim();
            if (text is "" or "--" or "None" or "N/A") return null;
            if (decimal.TryParse(text, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out number)) return number;
        }
        throw InvalidSource(dataset, $"{name} 數值格式不符");
    }

    private sealed record DownloadedResponse(string RawJson, string ResourceUrl, DateTimeOffset RetrievedAtUtc)
    {
        public string Hash => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(RawJson)));
    }
}
