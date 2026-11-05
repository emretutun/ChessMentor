using ChessMentor.Core;

namespace ChessMentor.App.Services;

/// <summary>Kullanıcı ayarları. Cihazda yerel olarak (MAUI Preferences) saklanır.</summary>
public sealed class AppSettings
{
    private static IPreferences P => Preferences.Default;

    public string? EnginePath
    {
        get => P.Get<string?>("enginePath", null);
        set { if (string.IsNullOrWhiteSpace(value)) P.Remove("enginePath"); else P.Set("enginePath", value); }
    }

    public int AnalysisTimeMs
    {
        get => P.Get("analysisTimeMs", 700);
        set => P.Set("analysisTimeMs", Math.Clamp(value, 200, 5000));
    }

    public NotationLanguage Notation
    {
        get => (NotationLanguage)P.Get("notation", (int)NotationLanguage.Easy);
        set => P.Set("notation", (int)value);
    }

    /// <summary>Kritik anlarda ve tehditlerde cevabı hemen söyleme, önce kullanıcıyı düşündür.</summary>
    public bool ThinkFirst
    {
        get => P.Get("thinkFirst", true);
        set => P.Set("thinkFirst", value);
    }

    /// <summary>Hata ve ciddi hatalarda bot cevap vermeden önce dur, "tekrar dene" fırsatı ver.</summary>
    public bool PauseOnMistakes
    {
        get => P.Get("pauseOnMistakes", true);
        set => P.Set("pauseOnMistakes", value);
    }

    /// <summary>Rakibin tehditlerini göster.</summary>
    public bool ShowThreats
    {
        get => P.Get("showThreats", true);
        set => P.Set("showThreats", value);
    }

    public int LevelIndex
    {
        get => P.Get("levelIndex", 3);
        set => P.Set("levelIndex", value);
    }

    /// <summary>0 = beyaz, 1 = siyah, 2 = rastgele.</summary>
    public int ColorIndex
    {
        get => P.Get("colorIndex", 0);
        set => P.Set("colorIndex", value);
    }

    public string StatsPath => Path.Combine(FileSystem.AppDataDirectory, "player-stats.json");
}
