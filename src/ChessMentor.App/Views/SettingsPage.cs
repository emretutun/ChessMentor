using ChessMentor.App.Services;
using ChessMentor.App.ViewModels;
using ChessMentor.Core;

namespace ChessMentor.App.Views;

public sealed class SettingsPage : ContentPage
{
    private readonly AppSettings _settings;
    private readonly EngineHost _engines;
    private readonly Entry _pathEntry;
    private readonly Label _engineStatus;
    private readonly Label _timeLabel;

    public SettingsPage(AppSettings settings, EngineHost engines)
    {
        _settings = settings;
        _engines = engines;
        Title = "Ayarlar";
        BackgroundColor = Palette.Background;

        _pathEntry = new Entry { Placeholder = "Boş bırakılırsa otomatik bulunur (engine klasörü veya PATH)", Text = settings.EnginePath, TextColor = Palette.Text };
        _engineStatus = new Label { TextColor = Palette.Muted, FontSize = 13, LineBreakMode = LineBreakMode.WordWrap };

        var browse = SmallButton("📂 Gözat…");
        browse.Clicked += async (_, _) => await BrowseAsync();
        var auto = SmallButton("🔎 Otomatik bul");
        auto.Clicked += async (_, _) => { _pathEntry.Text = ""; await ApplyEngineAsync(); };
        var apply = SmallButton("✔ Uygula");
        apply.BackgroundColor = Color.FromArgb("#629924");
        apply.Clicked += async (_, _) => await ApplyEngineAsync();

        // Analiz süresi
        _timeLabel = new Label { TextColor = Palette.Text };
        var slider = new Slider { Minimum = 300, Maximum = 3000, Value = settings.AnalysisTimeMs, MinimumTrackColor = Palette.Green };
        slider.ValueChanged += (_, e) =>
        {
            int ms = (int)(Math.Round(e.NewValue / 100) * 100);
            _settings.AnalysisTimeMs = ms;
            UpdateTimeLabel(ms);
        };
        UpdateTimeLabel(settings.AnalysisTimeMs);

        // Notasyon
        NotationLanguage[] notationValues = [NotationLanguage.Easy, NotationLanguage.Turkish, NotationLanguage.English];
        var notation = new Picker
        {
            ItemsSource = new[] { "Kolay okunur: At g1 → f3 (önerilen)", "Türkçe kısaltma: Af3", "Uluslararası: Nf3" },
            SelectedIndex = Array.IndexOf(notationValues, settings.Notation),
            TextColor = Palette.Text
        };
        notation.SelectedIndexChanged += (_, _) => _settings.Notation = notationValues[Math.Max(0, notation.SelectedIndex)];

        var threats = new Switch { IsToggled = settings.ShowThreats, OnColor = Palette.Green };
        threats.Toggled += (_, e) => _settings.ShowThreats = e.Value;

        Content = new ScrollView
        {
            Content = new VerticalStackLayout
            {
                Padding = 24,
                Spacing = 16,
                MaximumWidthRequest = 820,
                Children =
                {
                    Heading("⚙️ Ayarlar", 26),

                    Section("Satranç motoru (Stockfish)",
                        Text("ChessMentor ücretsiz ve açık kaynak Stockfish motorunu kullanır. Kurulum için README'deki adımları izle ya da stockfish.exe dosyasını buradan seç."),
                        _pathEntry,
                        new HorizontalStackLayout { Spacing = 8, Children = { browse, auto, apply } },
                        _engineStatus),

                    Section("Mentor",
                        _timeLabel,
                        slider,
                        Text("Süre uzadıkça analiz daha doğru olur ama her hamlede biraz daha beklersin."),
                        new Label { Text = "Notasyon", TextColor = Palette.Muted, FontSize = 12, Margin = new Thickness(0, 8, 0, 0) },
                        notation,
                        new HorizontalStackLayout
                        {
                            Spacing = 10,
                            Margin = new Thickness(0, 8, 0, 0),
                            Children = { threats, new Label { Text = "Rakibin tehditlerini göster", TextColor = Palette.Text, VerticalOptions = LayoutOptions.Center } }
                        }),

                    Section("Hakkında",
                        Text("ChessMentor tamamen ücretsizdir ve internet bağlantısı gerektirmez. Tüm analizler bilgisayarında yapılır; istatistiklerin sadece bu cihazda saklanır."),
                        Text("Stockfish, GPLv3 lisanslı ayrı bir programdır: https://stockfishchess.org"))
                }
            }
        };
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        UpdateEngineStatus();
    }

    private void UpdateTimeLabel(int ms) => _timeLabel.Text = $"Analiz süresi (hamle başına): {ms / 1000.0:0.0} sn";

    private void UpdateEngineStatus()
    {
        _engineStatus.Text = _engines.IsReady
            ? $"✅ Çalışıyor: {_engines.Analyst?.Name}\n{_engines.EnginePath}"
            : $"❌ Motor çalışmıyor. {_engines.LastError}";
        _engineStatus.TextColor = _engines.IsReady ? Palette.Green : Palette.Orange;
    }

    private async Task BrowseAsync()
    {
        var types = new FilePickerFileType(new Dictionary<DevicePlatform, IEnumerable<string>>
        {
            [DevicePlatform.WinUI] = [".exe"]
        });
        var result = await FilePicker.Default.PickAsync(new PickOptions { PickerTitle = "stockfish.exe dosyasını seç", FileTypes = types });
        if (result is null) return;
        _pathEntry.Text = result.FullPath;
        await ApplyEngineAsync();
    }

    private async Task ApplyEngineAsync()
    {
        _settings.EnginePath = string.IsNullOrWhiteSpace(_pathEntry.Text) ? null : _pathEntry.Text.Trim().Trim('"');
        _engineStatus.Text = "Motor yeniden başlatılıyor…";
        _engineStatus.TextColor = Palette.Muted;
        await _engines.RestartAsync(_settings.EnginePath);
        UpdateEngineStatus();
    }

    private static Button SmallButton(string text) => new()
    {
        Text = text,
        BackgroundColor = Color.FromArgb("#3C3A36"),
        TextColor = Palette.Text,
        FontSize = 13,
        CornerRadius = 6
    };

    private static Label Heading(string text, double size) =>
        new() { Text = text, FontSize = size, FontAttributes = FontAttributes.Bold, TextColor = Palette.Text };

    private static Label Text(string text) =>
        new() { Text = text, TextColor = Palette.Muted, FontSize = 13, LineBreakMode = LineBreakMode.WordWrap };

    private static Border Section(string title, params View[] children)
    {
        var stack = new VerticalStackLayout { Spacing = 8 };
        stack.Children.Add(new Label { Text = title, FontSize = 17, FontAttributes = FontAttributes.Bold, TextColor = Palette.Text });
        foreach (var c in children) stack.Children.Add(c);
        return new Border
        {
            BackgroundColor = Palette.Panel,
            StrokeThickness = 0,
            Padding = 16,
            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 10 },
            Content = stack
        };
    }
}
