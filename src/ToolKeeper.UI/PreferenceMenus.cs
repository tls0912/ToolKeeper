using System.Windows.Controls;

namespace ToolKeeper.UI;

/// <summary>Builds preference choices while leaving persistence and menu hosting to the application.</summary>
public static class PreferenceMenus
{
    public static void AddLanguageChoices(ItemCollection items, string selected, string language,
        Action<string> select, Action<Exception>? onError = null) =>
        AddChoices(items, UiLanguage.Choices(language), selected, select, onError);

    public static void AddThemeChoices(ItemCollection items, string selected, string language,
        Action<string> select, Action<Exception>? onError = null) =>
        AddChoices(items, UiTheme.Choices(language), selected, select, onError);

    private static void AddChoices(ItemCollection items, IReadOnlyList<UiChoice> choices,
        string selected, Action<string> select, Action<Exception>? onError)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(select);

        foreach (var choice in choices)
        {
            var item = new MenuItem
            {
                Header = choice.Label,
                IsCheckable = true,
                IsChecked = selected == choice.Value
            };
            item.Click += (_, e) =>
            {
                e.Handled = true;
                try { select(choice.Value); }
                catch (Exception ex) when (onError is not null) { onError(ex); }
            };
            items.Add(item);
        }
    }
}
