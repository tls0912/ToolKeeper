using System.Diagnostics.CodeAnalysis;
using System.IO;
using HistoLens.Core;

namespace HistoLens;

/// <summary>Checks a saved result's structure and internal references, without rerunning historical calculations.</summary>
internal static class SavedResearchValidator
{
    internal static void Validate(DataSnapshot snapshot, ResearchRun run, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Require(snapshot is not null && run is not null, "Missing snapshot or run.");
        Require(snapshot.Instrument is not null && snapshot.IsSynthetic == run.IsSynthetic,
            "Research and snapshot data types do not match.");
        SupportedResearchData.ValidateIdentity(snapshot);
        Require(snapshot.SnapshotId is not null && snapshot.ContentHash is not null && snapshot.Instrument.Name is not null &&
            snapshot.Instrument.Currency is not null, "Missing snapshot metadata.");
        Require(snapshot.Calendar is not null && snapshot.ActionCoverage is not null &&
            snapshot.Calendar.TradingDates is not null, "Missing snapshot coverage.");
        Items(snapshot.Bars, "bars"); Items(snapshot.CorporateActions, "corporate actions");
        Items(snapshot.ActionCoverage.Gaps, "coverage gaps");
        if (snapshot.ComparabilityCoverage is { } comparison) Items(comparison.Gaps, "comparison coverage gaps");
        Require(!SnapshotValidator.Validate(snapshot, cancellationToken).Any(issue => issue.BlocksResearch), "Snapshot structure or identity is invalid.");
        var hash = SnapshotFingerprint.Compute(snapshot);
        cancellationToken.ThrowIfCancellationRequested();
        Require(string.Equals(hash, run.DataContentHash, StringComparison.OrdinalIgnoreCase) &&
            (string.IsNullOrEmpty(snapshot.ContentHash) || string.Equals(hash, snapshot.ContentHash, StringComparison.OrdinalIgnoreCase)) &&
            run.DataSnapshotId == (string.IsNullOrEmpty(snapshot.SnapshotId) ? hash : snapshot.SnapshotId), "Research and snapshot hashes or IDs do not match.");
        Require(run.Instrument == snapshot.Instrument && run.SourceId == snapshot.SourceId &&
            run.CalendarVersion == snapshot.Calendar.Version && run.CorporateActionVersion == snapshot.ActionCoverage.Version &&
            run.PriceComparisonVersion == snapshot.ComparabilityCoverage?.Version,
            "Research and snapshot identities or coverage versions do not match.");
        Require(!string.IsNullOrWhiteSpace(run.EngineVersion) && run.Definition is not null && run.Funnel is not null,
            "Missing research metadata.");
        try { ResearchEngine.ValidateDefinition(run.Definition); }
        catch (ArgumentException error) { throw new InvalidDataException("Saved research settings are invalid.", error); }
        var definition = run.Definition;
        Require(definition.Name is not null && definition.DataAsOf <= snapshot.DataAsOf &&
            definition.EventStart >= snapshot.Calendar.CoverageStart && definition.EventEnd <= snapshot.Calendar.CoverageEnd,
            "Research dates exceed the snapshot coverage.");
        Require(run.IsResearchAllowed && Items(run.BlockingReasons, "blocking reasons").Count == 0,
            "Only complete, allowed research results can be saved.");
        Require(Items(run.Diagnostics, "diagnostics").All(issue => !issue.BlocksResearch &&
            !string.IsNullOrWhiteSpace(issue.Code) && issue.Message is not null), "Invalid research diagnostics.");

        var evaluations = Items(run.Evaluations, "evaluations");
        var cases = Items(run.Cases, "cases");
        var statistics = Items(run.Statistics, "statistics");
        var dates = snapshot.Calendar.TradingDates.Order().ToArray();
        var dateIndexes = dates.Select((date, index) => (date, index)).ToDictionary(item => item.date, item => item.index);
        var candidateDates = dates.Where(date => date >= definition.EventStart && date <= definition.EventEnd).ToHashSet();
        var evaluationsByDate = new Dictionary<DateOnly, EventEvaluation>();
        foreach (var evaluation in evaluations)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Require(candidateDates.Contains(evaluation.Date) && evaluationsByDate.TryAdd(evaluation.Date, evaluation), "Invalid or duplicate evaluation date.");
            Features(evaluation.Features, definition);
            Reasons(evaluation.Reasons);
            Require(Enum.IsDefined(evaluation.State) && evaluation.State == Combine(evaluation.Features), "Evaluation state does not match its feature states.");
            Require(evaluation.IsSampled
                ? evaluation.State == MatchState.Matched && evaluation.SamplingExclusion is null
                : evaluation.State == MatchState.Matched
                    ? evaluation.SamplingExclusion is ExclusionReason.EventStartUnknown or ExclusionReason.ConsecutiveMatch or ExclusionReason.OverlappingWindow
                    : evaluation.SamplingExclusion is null, "Invalid sampling state.");
        }
        var funnel = run.Funnel;
        Require(evaluations.Count == candidateDates.Count && funnel.CandidateDates == evaluations.Count &&
            funnel.DeterminableDates == evaluations.Count(item => item.State != MatchState.Unknown) &&
            funnel.RawMatchDays == evaluations.Count(item => item.State == MatchState.Matched) &&
            funnel.SampledEvents == evaluations.Count(item => item.IsSampled) && funnel.SampledEvents == cases.Count,
            "Research funnel counts do not match the saved evaluations and cases.");

        var bars = snapshot.Bars.ToDictionary(bar => bar.Date);
        var caseDates = new HashSet<DateOnly>();
        var lookback = definition.Conditions.Max(condition => condition.Lookback);
        foreach (var item in cases)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Require(caseDates.Add(item.EventDate) && evaluationsByDate.TryGetValue(item.EventDate, out var evaluation) && evaluation.IsSampled,
                "Case does not reference a unique sampled evaluation.");
            var index = dateIndexes[item.EventDate];
            Require(index >= lookback && item.ConditionWindowStart == dates[index - lookback] && item.BaseClose > 0 &&
                bars.TryGetValue(item.EventDate, out var bar) && item.BaseClose == bar.Close, "Invalid case date, history window or base price.");
            Features(item.Features, definition);
            Require(item.Features.SequenceEqual(evaluationsByDate[item.EventDate].Features), "Case features do not match the sampled evaluation.");
            var outcomes = Items(item.Outcomes, "outcomes");
            Horizons(outcomes.Select(outcome => outcome.Horizon), definition);
            foreach (var outcome in outcomes) Outcome(outcome, index, dates, definition.DataAsOf);
        }
        Horizons(statistics.Select(item => item.Horizon), definition);
        foreach (var item in statistics)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Statistics(item, cases);
        }
    }

    private static void Features(IReadOnlyList<FeatureValue> features, ResearchDefinition definition)
    {
        Require(Items(features, "features").Count == definition.Conditions.Count, "Feature count does not match the definition.");
        for (var index = 0; index < features.Count; index++)
            Require(features[index].Condition == definition.Conditions[index] && Enum.IsDefined(features[index].State) &&
                (features[index].State == MatchState.Unknown ? features[index].Value is null : features[index].Value is not null),
                "Feature condition, state or value is invalid.");
    }

    private static MatchState Combine(IReadOnlyList<FeatureValue> features) =>
        features.Any(item => item.State == MatchState.NotMatched) ? MatchState.NotMatched :
        features.Any(item => item.State == MatchState.Unknown) ? MatchState.Unknown : MatchState.Matched;

    private static void Horizons(IEnumerable<int> horizons, ResearchDefinition definition) =>
        Require(horizons.Order().SequenceEqual(definition.Horizons.Order()), "Result horizons do not match the definition.");

    private static void Outcome(HorizonOutcome outcome, int index, DateOnly[] dates, DateOnly dataAsOf)
    {
        Reasons(outcome.Exclusions);
        var expectedEnd = index + outcome.Horizon < dates.Length ? dates[index + outcome.Horizon] : (DateOnly?)null;
        Require(outcome.EndDate == expectedEnd && (outcome.PrimaryExclusion is null
            ? outcome.Exclusions.Count == 0 : outcome.Exclusions.Count > 0 && outcome.PrimaryExclusion == outcome.Exclusions[0]),
            "Invalid outcome end date or primary exclusion.");
        var values = new[] { outcome.PriceChange, outcome.HighestPriceChange, outcome.LowestPriceChange, outcome.CloseMaxDrawdown };
        if (!outcome.IsValid)
        {
            Require(values.All(value => value is null) && outcome.UpperFirstHitTradingDay is null && outcome.LowerFirstHitTradingDay is null &&
                outcome.UpperFirstHitDate is null && outcome.LowerFirstHitDate is null && outcome.ThresholdOrder is null &&
                ((expectedEnd is null || expectedEnd > dataAsOf) == outcome.Exclusions.Contains(ExclusionReason.InsufficientFutureData)),
                "Excluded outcome contains results or inconsistent future coverage.");
            return;
        }
        // Positive decimal prices can yield a ratio rounded to zero, and therefore a -1 change.
        Require(expectedEnd is not null && expectedEnd <= dataAsOf && values.All(value => value is not null && value >= -1) &&
            outcome.CloseMaxDrawdown <= 0 && outcome.LowestPriceChange <= outcome.PriceChange && outcome.HighestPriceChange >= outcome.PriceChange &&
            outcome.ThresholdOrder is not null && Enum.IsDefined(outcome.ThresholdOrder.Value), "Valid outcome is incomplete or outside its numeric range.");
        Hit(outcome.UpperFirstHitTradingDay, outcome.UpperFirstHitDate, outcome.Horizon, index, dates);
        Hit(outcome.LowerFirstHitTradingDay, outcome.LowerFirstHitDate, outcome.Horizon, index, dates);
        var upper = outcome.UpperFirstHitTradingDay;
        var lower = outcome.LowerFirstHitTradingDay;
        var order = upper is null && lower is null ? ThresholdOrder.Neither : upper == lower ? ThresholdOrder.SameDayUnknown :
            lower is null || upper.HasValue && upper < lower ? ThresholdOrder.UpperFirst : ThresholdOrder.LowerFirst;
        Require(outcome.ThresholdOrder == order, "Threshold order does not match the saved hit days.");
    }

    private static void Hit(int? day, DateOnly? date, int horizon, int index, DateOnly[] dates) =>
        Require(day is null ? date is null : day >= 1 && day <= horizon && index + day.Value < dates.Length && date == dates[index + day.Value],
            "Threshold hit day or date is invalid.");

    private static void Statistics(HorizonStatistics item, IReadOnlyList<ResearchCase> cases)
    {
        var all = cases.Select(@case => (@case.EventDate, Outcome: @case.Outcomes.Single(outcome => outcome.Horizon == item.Horizon))).ToArray();
        var valid = all.Where(pair => pair.Outcome.IsValid).ToArray();
        var excluded = all.Where(pair => !pair.Outcome.IsValid).GroupBy(pair => pair.Outcome.PrimaryExclusion!.Value)
            .ToDictionary(group => group.Key, group => group.Count());
        var exclusions = Items(item.Exclusions, "statistical exclusions");
        Require(item.ValidCount == valid.Length && item.ExcludedCount == all.Length - valid.Length &&
            exclusions.Count == excluded.Count && exclusions.Select(exclusion => exclusion.Reason).Distinct().Count() == exclusions.Count &&
            exclusions.All(exclusion => excluded.TryGetValue(exclusion.Reason, out var count) && exclusion.Count == count),
            "Horizon sample or exclusion counts do not match its outcomes.");
        Require(item.UpCount == valid.Count(pair => pair.Outcome.PriceChange > 0) &&
            item.FlatCount == valid.Count(pair => pair.Outcome.PriceChange == 0) && item.DownCount == valid.Count(pair => pair.Outcome.PriceChange < 0) &&
            item.UpperHitCount == valid.Count(pair => pair.Outcome.UpperFirstHitTradingDay.HasValue) &&
            item.LowerHitCount == valid.Count(pair => pair.Outcome.LowerFirstHitTradingDay.HasValue) &&
            item.UpperFirstCount == valid.Count(pair => pair.Outcome.ThresholdOrder == ThresholdOrder.UpperFirst) &&
            item.LowerFirstCount == valid.Count(pair => pair.Outcome.ThresholdOrder == ThresholdOrder.LowerFirst) &&
            item.SameDayUnknownCount == valid.Count(pair => pair.Outcome.ThresholdOrder == ThresholdOrder.SameDayUnknown) &&
            item.NeitherHitCount == valid.Count(pair => pair.Outcome.ThresholdOrder == ThresholdOrder.Neither), "Horizon result counts do not match its outcomes.");
        var rates = new[] { item.UpRate, item.FlatRate, item.DownRate, item.UpperHitRate, item.LowerHitRate };
        var changes = new[] { item.MeanPriceChange, item.MedianPriceChange, item.P10, item.P25, item.P75, item.P90,
            item.BestPriceChange, item.WorstPriceChange, item.MeanHighestPriceChange, item.MeanLowestPriceChange, item.MeanCloseMaxDrawdown };
        if (valid.Length == 0)
        {
            Require(rates.Concat(changes).All(value => value is null) && item.BestEventDate is null && item.WorstEventDate is null,
                "Empty statistics contain numeric results.");
            return;
        }
        Require(rates.All(value => value is >= 0 and <= 1) && changes.All(value => value is not null && value >= -1) &&
            item.MeanCloseMaxDrawdown <= 0 && item.MeanLowestPriceChange <= item.MeanHighestPriceChange &&
            item.WorstPriceChange == valid.Min(pair => pair.Outcome.PriceChange) && item.BestPriceChange == valid.Max(pair => pair.Outcome.PriceChange) &&
            valid.Any(pair => pair.EventDate == item.BestEventDate && pair.Outcome.PriceChange == item.BestPriceChange) &&
            valid.Any(pair => pair.EventDate == item.WorstEventDate && pair.Outcome.PriceChange == item.WorstPriceChange), "Statistical values or event dates are invalid.");
        var percentiles = new[] { item.WorstPriceChange, item.P10, item.P25, item.MedianPriceChange, item.P75, item.P90, item.BestPriceChange };
        Require(percentiles.SequenceEqual(percentiles.Order()) && item.MeanPriceChange >= item.WorstPriceChange && item.MeanPriceChange <= item.BestPriceChange,
            "Statistical order or mean is outside the saved outcome range.");
    }

    private static void Reasons(IReadOnlyList<ExclusionReason> reasons) =>
        Require(reasons is not null && reasons.All(reason => Enum.IsDefined(reason)) && reasons.Distinct().Count() == reasons.Count, "Invalid exclusion reasons.");

    private static IReadOnlyList<T> Items<T>(IReadOnlyList<T>? items, string name) where T : class
    {
        Require(items is not null && items.All(item => item is not null), $"Missing or null {name}.");
        return items;
    }

    private static void Require([DoesNotReturnIf(false)] bool condition, string message)
    {
        if (!condition) throw new InvalidDataException(message);
    }
}
