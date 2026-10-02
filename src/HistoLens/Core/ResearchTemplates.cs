namespace HistoLens.Core;

public static class ResearchTemplates
{
    public static IReadOnlyList<ResearchTemplate> All { get; } = Array.AsReadOnly(new[]
    {
        new ResearchTemplate
        {
            Id = "HL-R001", Name = "接近歷史區間低點", DefaultLookback = 120, DefaultThreshold = 0.10m,
            Description = "事件收盤介於前 N 個市場交易日最低價的 0% 至指定距離；不含事件日。",
            Formula = "0 ≤ C[t] / min(L[t-N..t-1]) − 1 ≤ 距離上限"
        },
        new ResearchTemplate
        {
            Id = "HL-R002", Name = "連續下跌", DefaultLookback = 3,
            Description = "連續 N 個市場交易日收盤低於前一日；平盤中斷，缺值不可判定。",
            Formula = "C[t] < C[t-1] < … < C[t-N]"
        },
        new ResearchTemplate
        {
            Id = "HL-R003", Name = "量增上漲", DefaultLookback = 20, DefaultThreshold = 1.5m,
            Description = "事件收盤高於前一日，事件成交量至少為前 N 日均量的指定倍數。",
            Formula = "C[t] / C[t-1] − 1 > 0 AND V[t] / mean(V[t-N..t-1]) ≥ 倍數"
        },
        new ResearchTemplate
        {
            Id = "HL-R004", Name = "收盤突破前高", DefaultLookback = 60,
            Description = "事件收盤嚴格高於前 N 個市場交易日最高價；相等不算突破。",
            Formula = "C[t] > max(H[t-N..t-1])"
        }
    });

    public static ResearchDefinition CreateDefinition(string templateId, DateOnly eventStart,
        DateOnly eventEnd, DateOnly dataAsOf, int? lookback = null, decimal? threshold = null)
    {
        var template = All.FirstOrDefault(t => t.Id == templateId)
            ?? throw new ArgumentException("找不到指定的研究模板。", nameof(templateId));
        var n = lookback ?? template.DefaultLookback;
        if (n is < 1 or > ResearchEngine.MaxLookback)
            throw new ArgumentOutOfRangeException(nameof(lookback), $"回看日數須介於 1 至 {ResearchEngine.MaxLookback}。");
        var parameter = threshold ?? template.DefaultThreshold;
        if (parameter is < 0 || parameter.HasValue && parameter != decimal.Round(parameter.Value, 8))
            throw new ArgumentOutOfRangeException(nameof(threshold), "模板參數不得為負，最多 8 位小數。");
        if (templateId == "HL-R003" && parameter <= 0)
            throw new ArgumentOutOfRangeException(nameof(threshold), "相對成交量倍數須大於零。");
        if (threshold.HasValue && template.DefaultThreshold is null)
            throw new ArgumentException("此模板沒有額外門檻參數，請調整回看日數。", nameof(threshold));

        ResearchCondition Condition(ResearchFeature feature, int days, ComparisonOperator op, decimal value) =>
            new() { Feature = feature, Lookback = days, Operator = op, Value = value };
        ResearchCondition[] conditions = templateId switch
        {
            "HL-R001" =>
            [
                Condition(ResearchFeature.DistanceFromPriorLow, n, ComparisonOperator.GreaterThanOrEqual, 0),
                Condition(ResearchFeature.DistanceFromPriorLow, n, ComparisonOperator.LessThanOrEqual, parameter!.Value)
            ],
            "HL-R002" => [Condition(ResearchFeature.ConsecutiveDeclines, n, ComparisonOperator.GreaterThanOrEqual, n)],
            "HL-R003" =>
            [
                Condition(ResearchFeature.PriceChange, 1, ComparisonOperator.GreaterThan, 0),
                Condition(ResearchFeature.RelativeVolume, n, ComparisonOperator.GreaterThanOrEqual, parameter!.Value)
            ],
            "HL-R004" => [Condition(ResearchFeature.RelativeToPriorHigh, n, ComparisonOperator.GreaterThan, 0)],
            _ => throw new ArgumentException("不支援的研究模板。", nameof(templateId))
        };
        return new ResearchDefinition
        {
            TemplateId = template.Id, TemplateVersion = template.Version, Name = template.Name,
            EventStart = eventStart, EventEnd = eventEnd, DataAsOf = dataAsOf, Conditions = conditions
        };
    }
}
