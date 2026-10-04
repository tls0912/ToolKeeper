using System.Globalization;
using System.IO;
using System.Windows.Controls;
using HistoLens.Core;
using HistoLens.Data;

namespace HistoLens;

public sealed partial class MainWindow
{
    private readonly ICurrentValuationProvider? _valuationProvider;
    private readonly TextBlock _valuationInfo = Text();
    private CancellationTokenSource? _valuationWork;
    private ValuationState _valuationState;
    private string? _valuationError;
    public CurrentValuation? Valuation { get; private set; }
    internal Task ValuationRefreshTask { get; private set; } = Task.CompletedTask;

    private void BuildValuationInfo(Panel settings)
    {
        _valuationInfo.FontSize = 12;
        _valuationInfo.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
        Bind(UpdateValuationInfo);
        settings.Children.Add(_valuationInfo);
    }

    private void ResetValuation()
    {
        CancelValuationRefresh();
        Valuation = null; _valuationError = null; _valuationState = ValuationState.NotRequested;
        UpdateValuationInfo();
    }

    private void CancelValuationRefresh()
    {
        _valuationWork?.Cancel();
        if (_valuationState == ValuationState.Loading)
        {
            _valuationState = ValuationState.Cancelled;
            if (!_closed) UpdateValuationInfo();
        }
    }

    private void StartValuationRefresh(DataSnapshot snapshot, int generation)
    {
        ResetValuation();
        if (snapshot.IsSynthetic || _valuationProvider is null) return;
        var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(12));
        _valuationWork = cancellation;
        _valuationState = ValuationState.Loading; UpdateValuationInfo();
        // A missing/slow quote must not hold up the historical scan or replace its result.
        ValuationRefreshTask = RefreshValuationAsync(snapshot, generation, cancellation);
    }

    private async Task RefreshValuationAsync(DataSnapshot snapshot, int generation, CancellationTokenSource cancellation)
    {
        bool IsCurrent() => !_closed && generation == _generation && ReferenceEquals(Snapshot, snapshot)
            && ReferenceEquals(_valuationWork, cancellation);
        try
        {
            var value = await _valuationProvider!.GetLatestAsync(snapshot.Instrument, cancellation.Token)
                .WaitAsync(cancellation.Token);
            cancellation.Token.ThrowIfCancellationRequested();
            if (!IsCurrent()) return;
            if (value is not null && (value.Code != snapshot.Instrument.Code ||
                !string.Equals(value.Market, snapshot.Instrument.Market, StringComparison.OrdinalIgnoreCase)))
                throw new InvalidDataException("本益比來源回傳不同市場或股票。");
            Valuation = value;
            _valuationState = value is null ? ValuationState.Unavailable : ValuationState.Ready;
        }
        catch (OperationCanceledException)
        {
            if (!IsCurrent()) return;
            // An explicit user cancellation is already labelled by CancelValuationRefresh.
            if (_valuationState != ValuationState.Cancelled)
            {
                _valuationState = ValuationState.Failed;
                _valuationError = T("The request timed out.", "查詢逾時。", "取得がタイムアウトしました。");
            }
        }
        catch (Exception error)
        {
            if (!IsCurrent()) return;
            _valuationState = ValuationState.Failed; _valuationError = error.Message;
        }
        finally
        {
            if (IsCurrent()) UpdateValuationInfo();
            if (ReferenceEquals(_valuationWork, cancellation)) _valuationWork = null;
            cancellation.Dispose();
            if (!_closed) SetEnabled();
        }
    }

    private void UpdateValuationInfo()
    {
        _valuationInfo.ToolTip = T("Latest published P/E, fetched when analysis starts. It is not a historical P/E and is not used in similarity scores.",
            "開始分析時查詢最新公布本益比；這不是歷史期間的本益比，不參與相似度計算。",
            "分析開始時の最新公表PERです。過去のPERではなく、類似度の計算には使用しません。");
        var prefix = T("P/E: ", "本益比：", "PER：");
        if (Snapshot?.IsSynthetic == true)
        {
            _valuationInfo.Text = prefix + T("Not applicable to synthetic data", "合成資料不適用", "合成データは対象外");
            return;
        }
        _valuationInfo.Text = prefix + (_valuationState switch
        {
            ValuationState.Loading => T("Fetching latest published data…", "查詢最新公布資料中…", "最新公表データを取得中…"),
            ValuationState.Cancelled => T("Query cancelled", "已取消查詢", "取得をキャンセルしました"),
            ValuationState.Failed => T("Unavailable; historical analysis continues", "查詢失敗；歷史分析照常執行", "取得失敗。履歴分析は続行します"),
            ValuationState.Unavailable => T("No published value for this stock", "來源未提供此股票資料", "この銘柄の公表値はありません"),
            ValuationState.Ready when Valuation is { } value =>
                (value.PriceEarningsRatio is { } ratio ? ratio.ToString("0.##", CultureInfo.InvariantCulture) + T("×", " 倍", "倍")
                    : T("Not provided / not applicable", "未提供／不適用", "未提供／対象外")) +
                $"\n{value.DataDate:yyyy-MM-dd} · " + T("latest published", "最新公布", "最新公表") + " · " + MarketLabel(value.Market),
            _ => T("Fetched when analysis starts", "開始分析時查詢", "分析開始時に取得")
        });
        if (Valuation is { } quote)
            _valuationInfo.ToolTip += "\n" + T("Source: ", "資料來源：", "取得元：") + quote.SourceId +
                $"\n{quote.ResourceUrl}\n" +
                T("Downloaded: ", "本機取得時間：", "取得時刻：") + quote.RetrievedAtUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss zzz");
        else if (_valuationError is not null) _valuationInfo.ToolTip += "\n" + _valuationError;
    }

    private enum ValuationState { NotRequested, Loading, Ready, Unavailable, Failed, Cancelled }
}
