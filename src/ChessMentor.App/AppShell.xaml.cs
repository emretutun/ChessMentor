using ChessMentor.App.Views;

namespace ChessMentor.App;

public partial class AppShell : Shell
{
    public AppShell(MainPage main, StatsPage stats, SettingsPage settings)
    {
        InitializeComponent();
        Items.Add(new TabBar
        {
            Items =
            {
                new ShellContent { Title = "♟ Oyna", Content = main, Route = "play" },
                new ShellContent { Title = "📊 İstatistiklerim", Content = stats, Route = "stats" },
                new ShellContent { Title = "⚙ Ayarlar", Content = settings, Route = "settings" }
            }
        });
    }
}
