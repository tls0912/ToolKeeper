using System.IO;
using CabiDock.Models;

namespace CabiDock.Services;

public sealed class ClassificationService
{
    public string Classify(DesktopItem item, CabiDockConfiguration configuration, string? manualCategoryId = null)
    {
        if (manualCategoryId is not null && configuration.Categories.Any(category => category.Id == manualCategoryId))
            return manualCategoryId;

        foreach (var rule in configuration.KeywordRules)
        {
            if (item.Name.Contains(rule.Keyword, StringComparison.OrdinalIgnoreCase))
                return rule.CategoryId;
        }

        // Folder names can contain periods, but folders have no file-extension classification.
        if (item.IsDirectory)
            return configuration.Categories.Single(category => category.Kind == CategoryKind.Folder).Id;

        var extension = ConfigurationService.NormalizeExtension(Path.GetExtension(item.Name));
        if (extension.Length > 0)
        {
            var match = configuration.Categories
                .Where(category => category.Kind == CategoryKind.Extension)
                .OrderByDescending(category => category.IsCustom)
                .FirstOrDefault(category => category.Extensions.Any(value =>
                    string.Equals(ConfigurationService.NormalizeExtension(value), extension, StringComparison.OrdinalIgnoreCase)));
            if (match is not null)
                return match.Id;
        }

        return configuration.Categories.Single(category => category.Kind == CategoryKind.Fallback).Id;
    }

    /// <summary>Reconciles a complete successful desktop snapshot; failed scans leave all records intact.</summary>
    public bool Reconcile(IEnumerable<DesktopItem> items, CabiDockState state, CabiDockConfiguration configuration,
        bool scanSucceeded = true, bool rulesChanged = false)
    {
        if (!scanSucceeded)
            return false;

        var paths = state.Items.GroupBy(item => item.FullPath, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
        var identities = state.Items.Where(item => !string.IsNullOrEmpty(item.Identity))
            .GroupBy(item => item.Identity!, StringComparer.Ordinal)
            .Where(group => group.Count() == 1)
            .ToDictionary(group => group.Key, group => group.Single(), StringComparer.Ordinal);
        var categories = configuration.Categories.Select(category => category.Id).ToHashSet(StringComparer.Ordinal);
        var consumed = new HashSet<ClassifiedItem>();
        var reconciled = new List<ClassifiedItem>();
        var changed = false;
        var snapshot = items.DistinctBy(item => item.FullPath, StringComparer.OrdinalIgnoreCase).ToList();
        var uniqueIdentities = snapshot.Where(item => !string.IsNullOrEmpty(item.Identity))
            .GroupBy(item => item.Identity!, StringComparer.Ordinal).Where(group => group.Count() == 1)
            .Select(group => group.Key).ToHashSet(StringComparer.Ordinal);

        foreach (var item in snapshot)
        {
            paths.TryGetValue(item.FullPath, out var existing);
            if (existing is not null && existing.Identity is not null && item.Identity is not null && existing.Identity != item.Identity)
                existing = null;
            if (existing is null && item.Identity is not null && uniqueIdentities.Contains(item.Identity))
                identities.TryGetValue(item.Identity, out existing);
            if (existing is not null && !consumed.Add(existing))
                existing = null;

            var valid = existing is not null && categories.Contains(existing.CategoryId) && Enum.IsDefined(existing.Source);
            var classify = !valid || (rulesChanged && existing!.Source == ClassificationSource.Auto);
            var next = new ClassifiedItem
            {
                FullPath = item.FullPath,
                Identity = item.Identity ?? existing?.Identity,
                CategoryId = classify ? Classify(item, configuration) : existing!.CategoryId,
                Source = classify ? ClassificationSource.Auto : existing!.Source
            };
            changed |= existing is null || !SameRecord(existing, next);
            reconciled.Add(next);
        }

        changed |= reconciled.Count != state.Items.Count;
        if (changed)
            state.Items = reconciled;
        return changed;
    }

    public bool AssignManually(DesktopItem item, string categoryId, CabiDockState state, CabiDockConfiguration configuration)
    {
        if (!configuration.Categories.Any(category => category.Id == categoryId))
            throw new ArgumentException("目標分類不存在。", nameof(categoryId));

        var existing = state.Items.FirstOrDefault(value => PathEquals(value.FullPath, item.FullPath));
        if (existing is null)
        {
            state.Items.Add(new ClassifiedItem
            {
                FullPath = item.FullPath,
                Identity = item.Identity,
                CategoryId = categoryId,
                Source = ClassificationSource.Manual
            });
            return true;
        }
        if (existing.CategoryId == categoryId && existing.Source == ClassificationSource.Manual)
            return false;
        existing.CategoryId = categoryId;
        existing.Source = ClassificationSource.Manual;
        existing.Identity = item.Identity ?? existing.Identity;
        return true;
    }

    /// <summary>Use only for a rename confirmed by the file-system watcher or a stable identity.</summary>
    public bool Rename(string oldPath, DesktopItem item, CabiDockState state)
    {
        var existing = state.Items.FirstOrDefault(value => PathEquals(value.FullPath, oldPath));
        if (existing is null)
            return false;
        state.Items.RemoveAll(value => value != existing && PathEquals(value.FullPath, item.FullPath));
        existing.FullPath = item.FullPath;
        existing.Identity = item.Identity ?? existing.Identity;
        return true;
    }

    public bool Remove(string path, CabiDockState state) => state.Items.RemoveAll(item => PathEquals(item.FullPath, path)) > 0;

    private static bool PathEquals(string first, string second) => string.Equals(first, second, StringComparison.OrdinalIgnoreCase);

    private static bool SameRecord(ClassifiedItem first, ClassifiedItem second) =>
        first.FullPath == second.FullPath && first.Identity == second.Identity && first.CategoryId == second.CategoryId && first.Source == second.Source;
}
