using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Threading;
using ToolKeeper.Services;
using ToolKeeper.UI;
using Xunit;

namespace ToolKeeper.Tests;

public sealed class MainWindowIntegrationTests
{
    [Fact]
    public Task CatalogRendersAvailabilityAndRoutesProductIdsWithoutReplacingItsSource() => OnSta(() =>
    {
        var window = CreateWindow();
        try
        {
            var products = Get<ItemsControl>(window, "Products");
            var originalSource = products.ItemsSource;
            var host = HostWindowContent(window);
            Layout(host);
            Assert.Equal(7, products.Items.Count);
            Assert.All(ProductButtons(products), button =>
            {
                Assert.False(button.IsEnabled);
                Assert.Equal("Unavailable", button.Content);
            });
            var requested = new List<string>();
            window.ProductActivationRequested += requested.Add;
            foreach (var button in ProductButtons(products)) Click(button);
            Assert.Empty(requested);

            window.SetProducts([
                new(ProductCatalogService.Definitions.Single(product => product.Id == "001"), ProductAvailability.Get, "ms-windows-store://test"),
                new(ProductCatalogService.Definitions.Single(product => product.Id == "003"), ProductAvailability.Available, "test.exe")
            ]);
            Layout(host);
            Assert.Same(originalSource, products.ItemsSource);
            var items = products.Items.Cast<object>().ToArray();
            foreach (var language in new[] { "en", "zh-TW", "ja" })
            {
                window.SelectedLanguage = language;
                Layout(host);
                var actions = ProductButtons(products);
                Assert.Equal(UiLanguage.Text(language, "Get", "取得", "入手"), actions[0].Content);
                Assert.Equal(UiLanguage.Text(language, "Open", "開啟", "開く"), actions[1].Content);
                Assert.All(actions, button => Assert.True(button.IsEnabled));
                Assert.Same(items[0], products.Items[0]);
                Assert.Same(items[1], products.Items[1]);
            }
            foreach (var button in ProductButtons(products)) Click(button);
            Assert.Equal(new[] { "001", "003" }, requested);

            window.SetProducts(ProductCatalogService.Definitions.Where(product => product.Id is "001" or "003").Select(product =>
                new ProductStatus(product, ProductAvailability.Unavailable, null)).ToArray());
            Layout(host);
            Assert.Same(originalSource, products.ItemsSource);
            Assert.Same(items[0], products.Items[0]);
            Assert.Same(items[1], products.Items[1]);
            foreach (var button in ProductButtons(products))
            {
                Assert.False(button.IsEnabled);
                Click(button);
            }
            Assert.Equal(new[] { "001", "003" }, requested);

            // Desktop settings remain reachable through the ordinary 002 catalog entry.
            var desktop = ProductCatalogService.Definitions.Single(product => product.Id == "002");
            window.SetProducts([new(desktop, ProductAvailability.Available, desktop.ActivationUri)]);
            Layout(host);
            var desktopAction = Assert.Single(ProductButtons(products));
            Assert.True(desktopAction.IsEnabled);
            Click(desktopAction);
            Assert.Equal(new[] { "001", "003", "002" }, requested);

            var histolens = ProductCatalogService.Definitions.Single(product => product.Id == "006");
            window.SetProducts([new(histolens, ProductAvailability.Available, histolens.ActivationUri)]);
            foreach (var language in new[] { "en", "zh-TW", "ja" })
            {
                window.SelectedLanguage = language;
                Layout(host);
                var previewAction = Assert.Single(ProductButtons(products));
                Assert.True(previewAction.IsEnabled);
                Assert.Equal(UiLanguage.Text(language, "Open", "開啟", "開く"), previewAction.Content);
                Assert.Contains(Descendants<TextBlock>(products), text => text.Text == UiLanguage.Text(language,
                    histolens.DescriptionEnglish, histolens.DescriptionChinese, histolens.DescriptionJapanese));
                Click(previewAction);
            }
            Assert.Equal(new[] { "001", "003", "002", "006", "006", "006" }, requested);
        }
        finally { window.Close(); }
    });

    [Fact]
    public Task HostErrorsCanBeShownAndClearedWithoutChangingCatalogState() => OnSta(() =>
    {
        var window = CreateWindow();
        try
        {
            var source = Get<ItemsControl>(window, "Products").ItemsSource;
            var status = Get<TextBlock>(window, "PlatformStatus");
            Assert.Equal(Visibility.Collapsed, status.Visibility);
            window.SetPlatformStatus("Could not open the requested application.");
            Assert.Equal(Visibility.Visible, status.Visibility);
            Assert.Equal("Could not open the requested application.", status.Text);
            Assert.Equal(status.Text, status.ToolTip);
            window.SelectedTheme = "Dark";
            Assert.Equal(status.Text, status.ToolTip);
            Assert.Same(source, Get<ItemsControl>(window, "Products").ItemsSource);
            Assert.Null(window.FindName("ExpectedHash"));
            Assert.Null(window.FindName("IconResults"));
            window.SetPlatformStatus(null);
            Assert.Equal(Visibility.Collapsed, status.Visibility);
            Assert.Empty(status.Text);
        }
        finally { window.Close(); }
    });

    private static MainWindow CreateWindow() => new()
    {
        ShowActivated = false, ShowInTaskbar = false, PreferencesPath = null, SelectedLanguage = "en"
    };

    private static T Get<T>(MainWindow window, string name) where T : FrameworkElement =>
        Assert.IsAssignableFrom<T>(window.FindName(name));

    private static void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

    private static Button[] ProductButtons(ItemsControl products) => Enumerable.Range(0, products.Items.Count)
        .Select(index => Assert.Single(Descendants<Button>(
            Assert.IsAssignableFrom<DependencyObject>(products.ItemContainerGenerator.ContainerFromIndex(index)))))
        .ToArray();

    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            if (child is T match) yield return match;
            foreach (var descendant in Descendants<T>(child)) yield return descendant;
        }
    }

    private static FrameworkElement HostWindowContent(Window window)
    {
        var content = (FrameworkElement)window.Content;
        window.Content = null;
        var host = new Border { Background = window.Background, Child = content, Resources = window.Resources };
        TextElement.SetFontFamily(host, window.FontFamily);
        TextElement.SetFontSize(host, window.FontSize);
        TextElement.SetForeground(host, window.Foreground);
        return host;
    }

    private static void Layout(FrameworkElement host)
    {
        host.Measure(new Size(1100, 850));
        host.Arrange(new Rect(0, 0, 1100, 850));
        host.UpdateLayout();
        Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
        host.UpdateLayout();
    }

    private static Task OnSta(Action action)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try { action(); completion.SetResult(); }
            catch (Exception exception) { completion.SetException(exception); }
            finally { Dispatcher.CurrentDispatcher.InvokeShutdown(); }
        }) { IsBackground = true, Name = "ToolKeeper hidden integration UI" };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return completion.Task.WaitAsync(TimeSpan.FromSeconds(30));
    }
}
