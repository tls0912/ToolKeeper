using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using MarkPad.Models;
using MarkPad.Services;
using ToolKeeper.UI;

namespace MarkPad;

/// <summary>A real HWND for Store APIs, created without opening documents or the editor.</summary>
public sealed class StartupLicenseWindow : Window, IStartupLicenseDialogs
{
    private readonly string _language;

    public StartupLicenseWindow(AppSettings settings)
    {
        _language = UiLanguage.Resolve(settings.Language);
        Title = "汗青 - Markdown Writer";
        Width = 440;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        FontFamily = new FontFamily(UiTypography.ResolveInterfaceFont(settings.UiFontFamily, _language, UiTheme.IsInk(settings.Theme)));
        FontSize = settings.UiFontSize;
        UiAppearance.ApplyResources(Resources, settings.Theme, _language, settings.UiFontFamily, settings.UiFontSize,
            settings.InterfaceTextShadowEnabled, settings.InterfaceTextShadowThickness);
        Background = (Brush)Resources["PaperBackgroundBrush"];
        Foreground = (Brush)Resources["TextBrush"];
        var workspace = new Border
        {
            Background = (Brush)Resources["PaperBackgroundBrush"],
            BorderBrush = (Brush)Resources["ChromeBackgroundBrush"], BorderThickness = new Thickness(8),
            Child = new TextBlock
            {
                Margin = new Thickness(24), TextWrapping = TextWrapping.Wrap,
                Text = T("Checking your Microsoft Store license…", "正在檢查 Microsoft Store 授權…", "Microsoft Store のライセンスを確認しています…")
            }
        };
        var frame = new WindowFrame(this) { Workspace = workspace };
        frame.ApplyMetrics(settings.UiFontSize);
        frame.ApplyLanguage(_language);
        Content = frame;
    }

    public async Task<bool> CheckAsync(StoreLicenseService service)
    {
        using var cancellation = new CancellationTokenSource();
        EventHandler closed = (_, _) => cancellation.Cancel();
        Closed += closed;
        try
        {
            Show();
            var owner = new WindowInteropHelper(this).EnsureHandle();
            return await new StartupLicenseGate(token => service.CheckAsync(owner, token), this, service.OpenStore, LocalLog.Write)
                .CanStartAsync(cancellation.Token);
        }
        finally
        {
            Closed -= closed;
            Close();
        }
    }

    public bool RetryCheck() => MessageBox.Show(this,
        T("Your license could not be verified. Connect to the internet and check that Microsoft Store is signed in to the account used to acquire this app.\n\nTry again? Choose No to close Hanqing.",
          "目前無法確認授權。請連線至網際網路，並確認 Microsoft Store 已登入取得此應用程式的帳號。\n\n要重試嗎？選擇「否」將關閉汗青。",
          "ライセンスを確認できませんでした。インターネットに接続し、Microsoft Store にアプリを取得したアカウントでサインインしてください。\n\n再試行しますか？「いいえ」を選ぶと汗青を終了します。"),
        Title, MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes;

    public bool OfferPurchase(AppLicenseState state)
    {
        var reason = state == AppLicenseState.Expired
            ? T("Your Hanqing license has expired.", "汗青授權已到期。", "汗青のライセンスが期限切れです。")
            : T("No valid Hanqing license was found.", "找不到有效的汗青授權。", "有効な汗青のライセンスが見つかりません。");
        return MessageBox.Show(this, reason + "\n\n" +
            T("Purchase Hanqing in Microsoft Store to continue. Open the Store now?\n\nHanqing will close. After purchasing, launch it again. If already purchased, check your Store account.",
              "請至 Microsoft Store 購買後繼續使用。現在開啟商店嗎？\n\n汗青即將關閉，完成購買後請重新啟動。若已購買，請確認商店登入的帳號。",
              "続けるには Microsoft Store で汗青を購入してください。ストアを開きますか？\n\n汗青を終了します。購入後にもう一度起動してください。購入済みの場合はストアのアカウントを確認してください。"),
            Title, MessageBoxButton.YesNo, MessageBoxImage.Information) == MessageBoxResult.Yes;
    }

    public void ShowPackageRequired() => MessageBox.Show(this,
        T("Please launch the Microsoft Store installation of Hanqing. This Store build cannot run outside its matching installed package. Hanqing will close.",
          "請啟動從 Microsoft Store 安裝的汗青。此商店版本必須在對應的已安裝套件中執行，汗青即將關閉。",
          "Microsoft Store からインストールした汗青を起動してください。このストア版は対応するインストール済みパッケージ内で実行する必要があります。汗青を終了します。"),
        Title, MessageBoxButton.OK, MessageBoxImage.Information);

    public void ShowStoreOpenFailed() => MessageBox.Show(this,
        T("Microsoft Store could not be opened. Open it manually and search for 汗青 - Markdown Writer. Hanqing will close.",
          "無法開啟 Microsoft Store。請手動開啟商店並搜尋「汗青 - Markdown Writer」。汗青即將關閉。",
          "Microsoft Store を開けませんでした。手動でストアを開き、「汗青 - Markdown Writer」を検索してください。汗青を終了します。"),
        Title, MessageBoxButton.OK, MessageBoxImage.Warning);

    private string T(string english, string chinese, string japanese) => UiLanguage.Text(_language, english, chinese, japanese);
}
