using ChessMentor.App.Services;
using Microsoft.Extensions.DependencyInjection;

namespace ChessMentor.App;

public partial class App : Application
{
    private readonly IServiceProvider _services;

    public App(IServiceProvider services)
    {
        InitializeComponent();
        _services = services;
        UserAppTheme = AppTheme.Dark;
    }

    protected override Window CreateWindow(IActivationState? activationState)
    {
        var window = new Window(_services.GetRequiredService<AppShell>())
        {
            Title = "ChessMentor · Satranç Koçun",
            Width = 1320,
            Height = 880,
            MinimumWidth = 1000,
            MinimumHeight = 680
        };

        // Uygulama kapanırken Stockfish süreçlerini de kapat
        window.Destroying += async (_, _) => await _services.GetRequiredService<EngineHost>().DisposeAsync();
        return window;
    }
}
