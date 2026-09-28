using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using ToolKeeper.Services;
using ToolKeeper.UI;

namespace ToolKeeper;

public partial class MainWindow
{
    // Construction uses catalog metadata only. The host supplies machine-specific availability.
    private readonly ObservableCollection<ProductItem> _products = new(
        ProductCatalogService.Definitions.Select(product =>
            new ProductItem(new ProductStatus(product, ProductAvailability.Unavailable, null))));
    public event Action<string>? ProductActivationRequested;

    public void SetProducts(IReadOnlyList<ProductStatus> products)
    {
        ArgumentNullException.ThrowIfNull(products);
        for (var index = 0; index < products.Count; index++)
        {
            var status = products[index];
            var item = _products.FirstOrDefault(product => product.Id == status.Id);
            if (item is null)
            {
                item = new ProductItem(status);
                _products.Insert(index, item);
            }
            else
            {
                _products.Move(_products.IndexOf(item), index);
                item.SetStatus(status);
            }
            item.Translate(ResolvedLanguage);
        }
        while (_products.Count > products.Count) _products.RemoveAt(_products.Count - 1);
    }

    public void SetPlatformStatus(string? message)
    {
        PlatformStatus.Text = message ?? "";
        PlatformStatus.ToolTip = message;
        PlatformStatus.Visibility = string.IsNullOrWhiteSpace(message) ? Visibility.Collapsed : Visibility.Visible;
    }

    private void ProductActivationClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: ProductItem { CanActivate: true } item })
            ProductActivationRequested?.Invoke(item.Id);
    }

    private sealed class ProductItem(ProductStatus status) : INotifyPropertyChanged
    {
        private ProductStatus _status = status;
        private string _language = "en";

        public string Id => _status.Id;
        public string Name => _status.Name;
        public string Description => UiLanguage.Text(_language, _status.Product.DescriptionEnglish,
            _status.Product.DescriptionChinese, _status.Product.DescriptionJapanese);
        public bool CanActivate => _status.CanActivate;
        public string ActionText => _status.CanLaunch
            ? UiLanguage.Text(_language, "Open", "開啟", "開く")
            : _status.CanAcquire
                ? UiLanguage.Text(_language, "Get", "取得", "入手")
                : UiLanguage.Text(_language, "Unavailable", "未提供", "未提供");
        public string ActionName => $"{ActionText} {Name}";
        public string ActionHelp => CanActivate ? ActionName : UiLanguage.Text(_language,
            "No installed app or download is currently available.",
            "目前未找到已安裝的程式，也沒有可用的下載入口。",
            "インストール済みのアプリやダウンロード先が見つかりません。");
        public event PropertyChangedEventHandler? PropertyChanged;

        public void SetStatus(ProductStatus value)
        {
            _status = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
        }

        public void Translate(string language)
        {
            _language = language;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
        }
    }
}
