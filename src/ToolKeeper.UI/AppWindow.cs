using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;

namespace ToolKeeper.UI;

/// <summary>
/// Common application shell. Set Workspace for product content and HeaderActions
/// for optional controls beside the product description; Content belongs to the shell.
/// Native window chrome preserves Windows resizing, snapping and caption actions.
/// </summary>
public class AppWindow : Window
{
    public static readonly DependencyProperty MainNameProperty = DependencyProperty.Register(
        nameof(MainName), typeof(string), typeof(AppWindow), new PropertyMetadata("", IdentityChanged));

    public static readonly DependencyProperty SubNameProperty = DependencyProperty.Register(
        nameof(SubName), typeof(string), typeof(AppWindow), new PropertyMetadata("", IdentityChanged));

    public static readonly DependencyProperty DescriptionProperty = DependencyProperty.Register(
        nameof(Description), typeof(string), typeof(AppWindow), new PropertyMetadata("", DescriptionChanged));

    public static readonly DependencyProperty WorkspaceProperty = DependencyProperty.Register(
        nameof(Workspace), typeof(UIElement), typeof(AppWindow), new PropertyMetadata(null, WorkspaceChanged));

    public static readonly DependencyProperty HeaderActionsProperty = DependencyProperty.Register(
        nameof(HeaderActions), typeof(UIElement), typeof(AppWindow), new PropertyMetadata(null, HeaderActionsChanged));

    public static readonly DependencyProperty SelectedThemeProperty = DependencyProperty.Register(
        nameof(SelectedTheme), typeof(string), typeof(AppWindow), new PropertyMetadata("Light", PreferencesChanged),
        value => value is string theme && UiTheme.IsSupported(theme));

    public static readonly DependencyProperty SelectedLanguageProperty = DependencyProperty.Register(
        nameof(SelectedLanguage), typeof(string), typeof(AppWindow), new PropertyMetadata("System", PreferencesChanged),
        value => value is string language && UiLanguage.IsSupported(language));

    private readonly TextBlock _heading;
    private readonly TextBlock _description;
    private readonly Border _workspaceHost;
    private readonly Border _actionsHost;
    private readonly Button _themeButton;
    private readonly Button _languageButton;
    private readonly Button _aboutButton;
    private bool _ready, _batchPreferences, _closed, _systemEventsAttached;
    private ContextMenu? _preferenceMenu;
    private Popup? _aboutPopup;

    /// <summary>Null disables persistence. Set before showing the window, using the product's own data directory.</summary>
    public string? PreferencesPath { get; set; }
    public string AboutAuthor { get; set; } = "";
    public string AboutVersion => GetType().Assembly.GetName().Version?.ToString(3) ?? "";
    public string ResolvedLanguage => UiLanguage.Resolve(SelectedLanguage);
    public bool IsDarkTheme => UiTheme.IsDark(SelectedTheme);
    public event EventHandler? UiPreferencesChanged;

    public AppWindow()
    {
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        WindowStyle = WindowStyle.SingleBorderWindow;
        ResizeMode = ResizeMode.CanResize;
        EnsureSharedResources();
        SetResourceReference(FontFamilyProperty, "UiFontFamily");
        SetResourceReference(FontSizeProperty, "UiFontSize");
        SetResourceReference(BackgroundProperty, "WindowBackground");
        SetResourceReference(ForegroundProperty, "TextBrush");
        UseLayoutRounding = true;

        var shell = new Grid { Name = "SharedWindowShell", Margin = new Thickness(28, 0, 28, 16) };
        shell.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        shell.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        var header = new Grid { Name = "ProductHeader", Margin = new Thickness(0, 0, 0, 20) };
        header.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        header.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _heading = new TextBlock
        {
            Name = "ProductHeading", FontSize = 30, FontWeight = FontWeights.SemiBold,
            TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 16, 0)
        };
        _heading.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush");
        AutomationProperties.SetHeadingLevel(_heading, AutomationHeadingLevel.Level1);
        _description = new TextBlock
        {
            Name = "ProductDescription", FontSize = 14,
            Margin = new Thickness(0, 4, 0, 0), TextWrapping = TextWrapping.Wrap
        };
        _description.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
        header.Children.Add(_heading);
        Grid.SetRow(_description, 1);
        header.Children.Add(_description);

        var preferences = new StackPanel
        {
            Name = "SharedHeaderPreferences", Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center
        };
        _themeButton = HeaderButton("SharedThemeButton", ShowThemeMenu);
        _languageButton = HeaderButton("SharedLanguageButton", ShowLanguageMenu);
        _aboutButton = HeaderButton("SharedAboutButton", ShowAbout);
        preferences.Children.Add(_themeButton);
        preferences.Children.Add(_languageButton);
        preferences.Children.Add(_aboutButton);
        Grid.SetColumn(preferences, 1);
        header.Children.Add(preferences);

        _actionsHost = new Border { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Right };
        Grid.SetRow(_actionsHost, 1);
        Grid.SetColumn(_actionsHost, 1);
        header.Children.Add(_actionsHost);
        shell.Children.Add(header);

        _workspaceHost = new Border();
        Grid.SetRow(_workspaceHost, 1);
        shell.Children.Add(_workspaceHost);
        Content = shell;
        IsVisibleChanged += (_, _) =>
        {
            if (IsVisible) return;
            if (_preferenceMenu is not null) _preferenceMenu.IsOpen = false;
            if (_aboutPopup is not null) _aboutPopup.IsOpen = false;
        };
        _ready = true;
        ApplyUiPreferences();
    }

    public string SelectedTheme
    {
        get => (string)GetValue(SelectedThemeProperty);
        set => SetValue(SelectedThemeProperty, value);
    }

    public string SelectedLanguage
    {
        get => (string)GetValue(SelectedLanguageProperty);
        set => SetValue(SelectedLanguageProperty, value);
    }

    protected string T(string english, string chinese, string japanese) =>
        UiLanguage.Text(ResolvedLanguage, english, chinese, japanese);

    public void ApplyUiPreferences()
    {
        if (!_ready || _closed) return;
        EnsureSharedResources();
        UiTheme.ApplyResources(Resources, SelectedTheme);
        UiTypography.ApplyResources(Resources,
            new FontFamily(UiTypography.ResolveInterfaceFont("", ResolvedLanguage, UiTheme.IsInk(SelectedTheme))), 14);
        Resources["SuccessBrush"] = Brush(IsDarkTheme ? "#80C99D" : "#1C704D");
        Resources["ErrorBrush"] = Brush(IsDarkTheme ? "#F19A97" : "#AA3434");
        _themeButton.Content = T("Style", "風格", "スタイル");
        _languageButton.Content = T("Language", "語言", "言語");
        _aboutButton.Content = T("About", "關於", "情報");
        foreach (var button in new[] { _themeButton, _languageButton, _aboutButton })
            AutomationProperties.SetName(button, (string)button.Content);
        _themeButton.ToolTip = $"{_themeButton.Content}: {UiTheme.Choices(ResolvedLanguage).Single(c => c.Value == SelectedTheme).Label}";
        _languageButton.ToolTip = $"{_languageButton.Content}: {UiLanguage.Choices(ResolvedLanguage).Single(c => c.Value == SelectedLanguage).Label}";
        UiPreferencesChanged?.Invoke(this, EventArgs.Empty);
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        if (!string.IsNullOrWhiteSpace(PreferencesPath)) SetPreferences(AppWindowPreferences.Load(PreferencesPath));
        else ApplyUiPreferences();
        SystemEvents.UserPreferenceChanged += SystemPreferenceChanged;
        _systemEventsAttached = true;
    }

    protected override void OnClosed(EventArgs e)
    {
        _closed = true;
        if (_systemEventsAttached) SystemEvents.UserPreferenceChanged -= SystemPreferenceChanged;
        if (_preferenceMenu is not null) _preferenceMenu.IsOpen = false;
        if (_aboutPopup is not null) _aboutPopup.IsOpen = false;
        base.OnClosed(e);
    }

    private void SystemPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        if (!_closed && !Dispatcher.HasShutdownStarted)
            Dispatcher.BeginInvoke(new Action(() => { if (SelectedTheme == "System") ApplyUiPreferences(); }));
    }

    private void EnsureSharedResources()
    {
        if (Resources.Contains("UiButtonStyle")) return;
        Resources.MergedDictionaries.Insert(0, new ResourceDictionary
        {
            Source = new Uri("/ToolKeeper.UI;component/Resources/UiStyles.xaml", UriKind.Relative)
        });
    }

    private Button HeaderButton(string name, Action action)
    {
        var button = new Button
        {
            Name = name, MinHeight = 34, Padding = new Thickness(10, 5, 10, 5),
            Margin = new Thickness(3, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center
        };
        button.SetResourceReference(StyleProperty, "UiButtonStyle");
        button.Click += (_, _) => action();
        return button;
    }

    private static void PreferencesChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
    {
        var window = (AppWindow)sender;
        if (!window._batchPreferences) window.ApplyUiPreferences();
    }

    private void SetPreferences(AppWindowPreferences preferences)
    {
        _batchPreferences = true;
        try { SelectedTheme = preferences.Theme; SelectedLanguage = preferences.Language; }
        finally { _batchPreferences = false; }
        ApplyUiPreferences();
    }

    private void ChoosePreference(string? theme = null, string? language = null)
    {
        if (_preferenceMenu is not null) _preferenceMenu.IsOpen = false;
        var preferences = new AppWindowPreferences(theme ?? SelectedTheme, language ?? SelectedLanguage);
        SetPreferences(preferences);
        if (string.IsNullOrWhiteSpace(PreferencesPath)) return;
        try
        {
            preferences.Save(PreferencesPath);
            if (Application.Current is null) return;
            var currentPath = Path.GetFullPath(PreferencesPath);
            foreach (var window in Application.Current.Windows.OfType<AppWindow>())
                if (window != this && !string.IsNullOrWhiteSpace(window.PreferencesPath) &&
                    string.Equals(currentPath, Path.GetFullPath(window.PreferencesPath), StringComparison.OrdinalIgnoreCase))
                    window.SetPreferences(preferences);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            MessageBox.Show(this,
                T("The preference is applied, but could not be saved.", "已套用偏好，但無法保存。", "設定は適用されましたが、保存できませんでした。") + "\n" + ex.Message,
                MainName, MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private ContextMenu NewPreferenceMenu(Button target)
    {
        if (_aboutPopup is not null) _aboutPopup.IsOpen = false;
        if (_preferenceMenu is not null) _preferenceMenu.IsOpen = false;
        var menu = new ContextMenu { Resources = Resources, PlacementTarget = target, Placement = PlacementMode.Bottom };
        menu.Closed += (_, _) => target.Focus();
        _preferenceMenu = menu;
        return menu;
    }

    private void ShowThemeMenu()
    {
        var menu = NewPreferenceMenu(_themeButton);
        PreferenceMenus.AddThemeChoices(menu.Items, SelectedTheme, ResolvedLanguage, value => ChoosePreference(theme: value));
        menu.IsOpen = true;
    }

    private void ShowLanguageMenu()
    {
        var menu = NewPreferenceMenu(_languageButton);
        PreferenceMenus.AddLanguageChoices(menu.Items, SelectedLanguage, ResolvedLanguage, value => ChoosePreference(language: value));
        menu.IsOpen = true;
    }

    private void ShowAbout()
    {
        if (_preferenceMenu is not null) _preferenceMenu.IsOpen = false;
        if (_aboutPopup is not null) _aboutPopup.IsOpen = false;
        var popup = new Popup
        {
            PlacementTarget = this, Placement = PlacementMode.Center,
            StaysOpen = false, AllowsTransparency = true, PopupAnimation = PopupAnimation.Fade
        };
        var info = new AboutInfo(MainName, AboutVersion, Description, AboutAuthor,
            T("ToolKeeper", "工具番 · ToolKeeper", "ToolKeeper"), Icon);
        var content = AboutContent.Create(info, ResolvedLanguage, () => popup.IsOpen = false, 14);
        content.Width = Math.Max(140, Math.Min(content.Width, ActualWidth - 84));
        var border = new Border
        {
            Resources = Resources, CornerRadius = new CornerRadius(6), BorderThickness = new Thickness(1),
            Child = new ScrollViewer
            {
                Content = content, MaxHeight = Math.Max(160, ActualHeight - 80),
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
            }
        };
        border.SetResourceReference(Border.BackgroundProperty, "SurfaceBrush");
        border.SetResourceReference(Border.BorderBrushProperty, "LineBrush");
        border.SetResourceReference(System.Windows.Documents.TextElement.ForegroundProperty, "TextBrush");
        border.SetResourceReference(System.Windows.Documents.TextElement.FontFamilyProperty, "UiFontFamily");
        border.SetResourceReference(System.Windows.Documents.TextElement.FontSizeProperty, "UiFontSize");
        if (content.FindName("AboutCloseButton") is Button close)
        {
            close.SetResourceReference(StyleProperty, "UiButtonStyle");
            popup.Opened += (_, _) => close.Focus();
        }
        popup.Child = border;
        popup.Closed += (_, _) => { if (!_closed) _aboutButton.Focus(); };
        _aboutPopup = popup;
        popup.IsOpen = true;
    }

    public string MainName
    {
        get => (string)GetValue(MainNameProperty);
        set => SetValue(MainNameProperty, value);
    }

    public string SubName
    {
        get => (string)GetValue(SubNameProperty);
        set => SetValue(SubNameProperty, value);
    }

    public string Description
    {
        get => (string)GetValue(DescriptionProperty);
        set => SetValue(DescriptionProperty, value);
    }

    public UIElement? Workspace
    {
        get => (UIElement?)GetValue(WorkspaceProperty);
        set => SetValue(WorkspaceProperty, value);
    }

    public UIElement? HeaderActions
    {
        get => (UIElement?)GetValue(HeaderActionsProperty);
        set => SetValue(HeaderActionsProperty, value);
    }

    private static void IdentityChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
    {
        var window = (AppWindow)sender;
        window.Title = $"{window.MainName} - {window.SubName}";
        window._heading.Text = window.MainName;
    }

    private static void DescriptionChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args) =>
        ((AppWindow)sender)._description.Text = (string)args.NewValue;

    private static void WorkspaceChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args) =>
        ((AppWindow)sender)._workspaceHost.Child = (UIElement?)args.NewValue;

    private static void HeaderActionsChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
    {
        var host = ((AppWindow)sender)._actionsHost;
        host.Child = (UIElement?)args.NewValue;
        host.Margin = args.NewValue is null ? new Thickness(0) : new Thickness(24, 0, 0, 0);
    }

    private static SolidColorBrush Brush(string value)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(value));
        brush.Freeze();
        return brush;
    }
}
