using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using HistoLens.Core;

namespace HistoLens;

public sealed partial class MainWindow
{
    private static readonly SimilarityIndicator[] IndicatorOrder =
    [
        SimilarityIndicator.PricePath, SimilarityIndicator.HighPosition, SimilarityIndicator.LowPosition,
        SimilarityIndicator.Slope, SimilarityIndicator.Volatility, SimilarityIndicator.Rsi,
        SimilarityIndicator.MovingAverageDeviation, SimilarityIndicator.BollingerBandwidth,
        SimilarityIndicator.NormalizedAtr, SimilarityIndicator.VolumePath
    ];
    private readonly Dictionary<SimilarityIndicator, CheckBox> _indicatorChoices = [];
    private readonly Dictionary<SimilarityIndicator, DataGridColumn> _indicatorColumns = [];
    private GroupBox _indicatorSelection = null!;
    private IndicatorPreferencesStore? _indicatorPreferences;

    private SimilarityIndicator SelectedIndicators => _indicatorChoices.Aggregate((SimilarityIndicator)0,
        (mask, item) => item.Value.IsChecked == true ? mask | item.Key : mask);

    private void BuildIndicatorSelection(Panel settings)
    {
        var choices = new StackPanel();
        _indicatorSelection = Section(choices, "Compare indicators", "比對指標", "比較する指標");
        settings.Children.Add(_indicatorSelection);
        var note = Text(); note.FontSize = 12;
        Bind(() => note.Text = T("Select at least one. Selected scores have equal weight. N is your recent window; every feature uses only that window.",
            "至少勾選一項，總分採已選項目平均。N 為近期日數，各項只使用該區間內資料。",
            "1項目以上を選択。選択した得点を等分で平均します。Nは直近日数で、期間内のデータだけを使います。"));
        choices.Children.Add(note);
        foreach (var indicator in IndicatorOrder)
        {
            var check = new CheckBox
            {
                IsChecked = SimilarityIndicator.Default.HasFlag(indicator),
                Margin = new Thickness(2, 4, 2, 4), VerticalContentAlignment = VerticalAlignment.Center
            };
            check.SetResourceReference(Control.ForegroundProperty, "TextBrush");
            Bind(() =>
            {
                check.Content = IndicatorName(indicator); check.ToolTip = IndicatorHelp(indicator);
                System.Windows.Automation.AutomationProperties.SetName(check, IndicatorName(indicator));
            });
            _indicatorChoices.Add(indicator, check); choices.Children.Add(check);
            check.Checked += (_, _) => IndicatorSelectionEdited();
            check.Unchecked += (_, _) => IndicatorSelectionEdited();
        }
        var actions = new WrapPanel();
        actions.Children.Add(Button("Select all", "全選", "すべて選択", (_, _) => SetIndicatorSelection(SimilarityIndicator.All)));
        actions.Children.Add(Button("Default five", "恢復原五項", "元の5項目", (_, _) => SetIndicatorSelection(SimilarityIndicator.Default)));
        choices.Children.Add(actions);
        Bind(UpdateIndicatorSelectionHeader);
    }

    private void SetIndicatorSelection(SimilarityIndicator selected)
    {
        var updating = _updating; _updating = true;
        try { foreach (var (indicator, check) in _indicatorChoices) check.IsChecked = selected.HasFlag(indicator); }
        finally { _updating = updating; }
        IndicatorSelectionEdited();
    }

    private void LoadIndicatorPreferences(string directory)
    {
        _indicatorPreferences = new IndicatorPreferencesStore(directory);
        var selected = SimilarityIndicator.Default;
        try { selected = _indicatorPreferences.Load(); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            SetStatus(() => T("Saved comparison indicators could not be read. The default five are selected.",
                "無法讀取已保存的比對指標，目前使用原五項。", "保存した比較指標を読み込めません。元の5項目を選択しました。"));
        }
        var updating = _updating; _updating = true;
        try { foreach (var (indicator, check) in _indicatorChoices) check.IsChecked = selected.HasFlag(indicator); }
        finally { _updating = updating; }
        UpdateIndicatorSelectionHeader(); UpdateIndicatorColumns();
    }

    private void IndicatorSelectionEdited()
    {
        if (_updating) return;
        SimilaritySettingsEdited();
        try { _indicatorPreferences?.Save(SelectedIndicators); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            SetStatus(() => T("Your current indicators are applied, but could not be saved. Check that the data folder is writable.",
                "目前勾選已套用，但無法保存比對指標；請確認資料目錄可寫入。",
                "現在の選択は適用しましたが保存できません。データフォルダーの書き込み権限を確認してください。"));
        }
    }

    private void UpdateIndicatorSelectionHeader()
    {
        var count = _indicatorChoices.Count(item => item.Value.IsChecked == true);
        _indicatorSelection.Header = T($"Compare indicators ({count}/10)", $"比對指標（{count}/10）", $"比較する指標（{count}/10）");
    }

    private void SimilaritySettingsEdited()
    {
        if (_updating) return;
        InvalidateWork(); _similarityStale = SimilarityResult is not null;
        UpdateIndicatorSelectionHeader(); UpdateIndicatorColumns(); UpdateSimilaritySummary();
        SetStatus(() => SelectedIndicators == 0
            ? T("Select at least one comparison indicator.", "請至少勾選一項比對指標。", "比較する指標を1項目以上選択してください。")
            : T("Scan settings changed. Run again to update the results.", "掃描設定已變更，請重新執行；舊結果仍保留原設定。", "検索条件を変更しました。再実行してください。以前の結果は元の条件のままです。"));
        SetEnabled();
    }

    private void BuildIndicatorColumns()
    {
        foreach (var indicator in IndicatorOrder)
        {
            // Result properties mirror the stable enum names. Sort uses unrounded score values.
            Column(_similarities, indicator.ToString(), "", "", "", $"Match.Scores.{indicator}", numeric: true);
            var column = _similarities.Columns[^1]; _indicatorColumns.Add(indicator, column);
            Bind(() => column.Header = IndicatorName(indicator) + " %");
        }
        UpdateIndicatorColumns();
    }

    private void UpdateIndicatorColumns()
    {
        // Editing inputs must never relabel or hide the prior run's scores before a new run completes.
        var selected = SimilarityResult?.Definition.SelectedIndicators ?? SelectedIndicators;
        foreach (var (indicator, column) in _indicatorColumns)
            column.Visibility = selected.HasFlag(indicator) ? Visibility.Visible : Visibility.Collapsed;
    }

    private string IndicatorName(SimilarityIndicator indicator) => indicator switch
    {
        SimilarityIndicator.PricePath => T("Price path", "價格路徑", "価格経路"),
        SimilarityIndicator.HighPosition => T("High position", "相對高點", "高値位置"),
        SimilarityIndicator.LowPosition => T("Low position", "相對低點", "安値位置"),
        SimilarityIndicator.Slope => T("Slope", "斜率", "傾き"),
        SimilarityIndicator.Volatility => T("Volatility", "波動度", "変動性"),
        SimilarityIndicator.Rsi => T("Window RSI", "區間 RSI", "期間 RSI"),
        SimilarityIndicator.MovingAverageDeviation => T("SMA deviation", "均線乖離", "移動平均乖離"),
        SimilarityIndicator.BollingerBandwidth => T("Bollinger width", "布林帶寬", "ボリンジャー幅"),
        SimilarityIndicator.NormalizedAtr => T("Normalized ATR", "正規化 ATR", "正規化 ATR"),
        SimilarityIndicator.VolumePath => T("Volume shape", "成交量形態", "出来高形状"),
        _ => indicator.ToString()
    };

    private string IndicatorHelp(SimilarityIndicator indicator) => indicator switch
    {
        SimilarityIndicator.Rsi => T("Momentum balance: RSI seeded with the N−1 in-window changes. Flat=50. Difference scale: 20 RSI points.",
            "比較漲跌動能：用區間內 N−1 次變動的平均漲幅／跌幅計算 RSI 初值，全平盤為 50。差異尺度：20 RSI 點。",
            "N−1回の価格変化の平均上昇・下落からRSI初期値を計算。横ばい=50。差の尺度：20 RSIポイント。"),
        SimilarityIndicator.MovingAverageDeviation => T("Last close / N-day simple average − 1. Compares position relative to the average. Scale: 5 percentage points.",
            "末日收盤 ÷ N 日簡單均價 − 1，比較價格離均線的距離。差異尺度：5 個百分點。",
            "最終終値÷N日単純平均−1。平均からの位置を比較。尺度：5ポイント。"),
        SimilarityIndicator.BollingerBandwidth => T("N closes, population standard deviation, bands at ±2σ. Width=4σ/mean. Compares contraction/expansion. Scale: 10 percentage points.",
            "N 日收盤、母體標準差、上下各 2σ；帶寬＝4σ÷均價，比較盤整／擴張程度。差異尺度：10 個百分點。",
            "N終値の母標準偏差、上下±2σ。幅=4σ÷平均。収縮・拡大を比較。尺度：10ポイント。"),
        SimilarityIndicator.NormalizedAtr => T("Mean of N−1 in-window true ranges / final close. Includes gaps; uses the ATR seed average, without earlier smoothing. Scale: 2 percentage points.",
            "區間內 N−1 個真實振幅平均 ÷ 末日收盤，涵蓋跳空；使用 ATR 初始平均，不延續區間外平滑。差異尺度：2 個百分點。",
            "期間内N−1個の真の値幅の平均÷最終終値。ギャップを含み、期間外の平滑値は使いません。尺度：2ポイント。"),
        SimilarityIndicator.VolumePath => T("Daily volume / window mean. Compares the entire shape by RMSE (scale 0.5×), not raw share counts. Missing volume or a zero mean makes the window unavailable.",
            "每日成交量 ÷ 區間均量，比較整段放量／縮量形態的 RMSE（尺度 0.5 倍）；缺量或均量為零時無法比對。明細顯示末日相對量。",
            "日次出来高÷期間平均。形状全体をRMSEで比較（尺度0.5倍）。欠損または平均ゼロは比較不可。詳細は最終日の相対量。"),
        _ => T("Compare this feature of the two selected windows. Each selected indicator has equal weight; see How similarity is calculated.",
            "比較兩段區間的此項特徵。所有勾選項目等權平均；詳細口徑見「相似度怎麼算」。",
            "2期間のこの特徴を比較します。選択した項目は等分で平均。詳細は類似度の計算方法を参照。")
    };

    private string IndicatorValue(SimilarityIndicator indicator, SimilarityFeatures value) => indicator switch
    {
        SimilarityIndicator.PricePath => NormalizedEnd(value),
        SimilarityIndicator.HighPosition => Fraction(value.DistanceFromHigh),
        SimilarityIndicator.LowPosition => Fraction(value.DistanceFromLow),
        SimilarityIndicator.Slope => ((double)value.Slope * 100).ToString("0.000", CultureInfo.InvariantCulture),
        SimilarityIndicator.Volatility => Fraction(value.Volatility),
        SimilarityIndicator.Rsi => value.Rsi?.ToString("0.00", CultureInfo.InvariantCulture) ?? "—",
        SimilarityIndicator.MovingAverageDeviation => value.MovingAverageDeviation is { } deviation ? Fraction(deviation) : "—",
        SimilarityIndicator.BollingerBandwidth => value.BollingerBandwidth is { } width ? Fraction(width) : "—",
        SimilarityIndicator.NormalizedAtr => value.NormalizedAtr is { } atr ? Fraction(atr) : "—",
        SimilarityIndicator.VolumePath => value.EndRelativeVolume?.ToString("0.00'×'", CultureInfo.InvariantCulture) ?? "—",
        _ => "—"
    };

    private string IndicatorValueLabel(SimilarityIndicator indicator) => indicator switch
    {
        SimilarityIndicator.PricePath => T("Path end (start=100)", "路徑末值（起點100）", "終値（起点100）"),
        SimilarityIndicator.Slope => T("Slope (pp/day)", "斜率（百分點／日）", "傾き（ポイント/日）"),
        SimilarityIndicator.VolumePath => T("Volume shape (last / mean)", "成交量形態（末日／均量）", "出来高形状（最終日/平均）"),
        _ => IndicatorName(indicator)
    };
}
