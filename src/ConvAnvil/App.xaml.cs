using System.Windows;

namespace ConvAnvil;

public partial class App : Application
{
    private async void OnStartup(object sender, StartupEventArgs e)
    {
        var window = new MainWindow();
        MainWindow = window;
        window.Show();
        if (e.Args.Length > 0) await window.LoadFileAsync(e.Args[0]);
    }
}
