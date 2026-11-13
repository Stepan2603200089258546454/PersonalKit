using System.Windows;
using DevHome.Wpf.Services;
using Microsoft.Extensions.DependencyInjection;

namespace DevHome.Wpf;

/// <summary>
/// Точка входа WPF Blazor Hybrid-приложения.
/// </summary>
public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var services = new ServiceCollection();
        services.AddWpfBlazorWebView();
        services.AddSingleton(OidcSettings.Load());
        services.AddScoped<AuthenticationService>();

        Resources.Add("services", services.BuildServiceProvider());

        new MainWindow().Show();
    }
}
