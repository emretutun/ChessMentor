using ChessMentor.App.Services;
using ChessMentor.App.ViewModels;
using ChessMentor.Mentor;

namespace ChessMentor.App.Views;

/// <summary>Uzun vadeli gelişim: doğruluk, hamle dağılımı ve en sık yapılan hata türleri.</summary>
public sealed class StatsPage : ContentPage
{
    private readonly AppSettings _settings;
    private readonly VerticalStackLayout _content;

    public StatsPage(AppSettings settings)
    {
        _settings = settings;
        Title = "İstatistiklerim";
        BackgroundColor = Palette.Background;
        _content = new VerticalStackLayout { Padding = 24, Spacing = 16, MaximumWidthRequest = 820 };
        Content = new ScrollView { Content = _content };
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        Render();
    }

    private void Render()
    {
        var stats = PlayerStats.Load(_settings.StatsPath);
        _content.Children.Clear();
        _content.Children.Add(Heading("📊 İstatistiklerim", 26));

        if (stats.GamesPlayed == 0)
        {
            _content.Children.Add(Text("Henüz tamamlanmış bir oyunun yok. Bir oyun bitirdiğinde gelişimin burada görünecek.", Palette.Muted));
            return;
        }

        // Özet kutucukları
        var tiles = new Grid { ColumnDefinitions = Columns(4), ColumnSpacing = 12 };
        tiles.Add(Tile("Oyun", stats.GamesPlayed.ToString()), 0);
        tiles.Add(Tile("G / B / M", $"{stats.Wins} / {stats.Draws} / {stats.Losses}"), 1);
        tiles.Add(Tile("Ort. doğruluk", $"%{stats.AverageAccuracy:0}"), 2);
        tiles.Add(Tile("İncelenen hamle", stats.MovesReviewed.ToString()), 3);
        _content.Children.Add(tiles);

        // Son oyunlardaki doğruluk
        if (stats.RecentAccuracy.Count > 0)
        {
            _content.Children.Add(Heading("Son oyunlardaki doğruluk", 18));
            var bars = new HorizontalStackLayout { Spacing = 6, HeightRequest = 130 };
            foreach (var acc in stats.RecentAccuracy.TakeLast(20))
            {
                bars.Children.Add(new VerticalStackLayout
                {
                    VerticalOptions = LayoutOptions.End,
                    Spacing = 2,
                    Children =
                    {
                        new Label { Text = $"{acc:0}", FontSize = 10, TextColor = Palette.Muted, HorizontalTextAlignment = TextAlignment.Center },
                        new BoxView { WidthRequest = 24, HeightRequest = Math.Max(4, acc), Color = AccuracyColor(acc), CornerRadius = 3 }
                    }
                });
            }
            _content.Children.Add(Card(bars));
        }

        // Hamle dağılımı
        _content.Children.Add(Heading("Hamle dağılımı", 18));
        int total = Math.Max(1, stats.QualityCounts.Values.Sum());
        var dist = new VerticalStackLayout { Spacing = 6 };
        foreach (var q in Enum.GetValues<MoveQuality>())
        {
            int c = stats.QualityCounts.GetValueOrDefault(q);
            var row = new Grid { ColumnDefinitions = [new(140), new(GridLength.Star), new(50)], ColumnSpacing = 10 };
            row.Add(new Label { Text = $"{q.Icon()} {q.Label()}", TextColor = Palette.Text, VerticalOptions = LayoutOptions.Center }, 0);
            var barHost = new Grid { ColumnDefinitions = [new(new GridLength(Math.Max(0.001, c), GridUnitType.Star)), new(new GridLength(Math.Max(0.001, total - c), GridUnitType.Star))] };
            barHost.Add(new BoxView { Color = Color.FromArgb(q.ColorHex()), HeightRequest = 14, CornerRadius = 3 }, 0);
            row.Add(barHost, 1);
            row.Add(new Label { Text = c.ToString(), TextColor = Palette.Muted, HorizontalTextAlignment = TextAlignment.End }, 2);
            dist.Children.Add(row);
        }
        _content.Children.Add(Card(dist));

        // En sık hatalar ve tavsiyeler
        var top = stats.TopMotifs().ToList();
        if (top.Count > 0)
        {
            _content.Children.Add(Heading("🎯 Üzerinde çalışman gerekenler", 18));
            foreach (var (motif, count) in top)
            {
                _content.Children.Add(Card(new VerticalStackLayout
                {
                    Spacing = 4,
                    Children =
                    {
                        new Label { Text = $"{motif.Label()} · {count} kez", FontAttributes = FontAttributes.Bold, TextColor = Palette.Orange, FontSize = 15 },
                        Text(motif.Tip(), Palette.Text)
                    }
                }));
            }
        }

        var reset = new Button { Text = "İstatistikleri sıfırla", BackgroundColor = Color.FromArgb("#3C3A36"), TextColor = Palette.Text, HorizontalOptions = LayoutOptions.Start, Margin = new Thickness(0, 12, 0, 0) };
        reset.Clicked += async (_, _) =>
        {
            if (!await DisplayAlertAsync("Sıfırla", "Tüm istatistikler silinsin mi?", "Evet", "Hayır")) return;
            new PlayerStats().Save(_settings.StatsPath);
            Render();
        };
        _content.Children.Add(reset);
    }

    private static Color AccuracyColor(double acc) => acc switch
    {
        >= 85 => Palette.Green,
        >= 70 => Color.FromArgb("#9CB86B"),
        >= 55 => Palette.Orange,
        _ => Palette.Red
    };

    private static ColumnDefinitionCollection Columns(int n)
    {
        var c = new ColumnDefinitionCollection();
        for (int i = 0; i < n; i++) c.Add(new ColumnDefinition(GridLength.Star));
        return c;
    }

    private static Label Heading(string text, double size) =>
        new() { Text = text, FontSize = size, FontAttributes = FontAttributes.Bold, TextColor = Palette.Text };

    private static Label Text(string text, Color color) =>
        new() { Text = text, TextColor = color, FontSize = 14, LineBreakMode = LineBreakMode.WordWrap };

    private static Border Card(View content) => new()
    {
        BackgroundColor = Palette.Panel,
        StrokeThickness = 0,
        Padding = 14,
        StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 10 },
        Content = content
    };

    private static Border Tile(string label, string value) => Card(new VerticalStackLayout
    {
        Spacing = 2,
        Children =
        {
            new Label { Text = label, TextColor = Palette.Muted, FontSize = 12 },
            new Label { Text = value, TextColor = Palette.Text, FontSize = 24, FontAttributes = FontAttributes.Bold }
        }
    });
}
