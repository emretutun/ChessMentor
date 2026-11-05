namespace ChessMentor.Mentor;

/// <summary>Bir hamlenin kalitesi, kazanma şansı kaybına göre.</summary>
public enum MoveQuality
{
    /// <summary>Kritik anda tek iyi hamleyi bulmak.</summary>
    Great,
    Best,
    Excellent,
    Good,
    Inaccuracy,
    Mistake,
    Blunder
}

/// <summary>Kendi yaptığın hataların türü. İstatistik ve kişisel ipuçları için kullanılır.</summary>
public enum MistakeMotif
{
    None,
    HungPiece,
    AllowedMate,
    AllowedFork,
    AllowedTactic,
    MissedMate,
    MissedCapture,
    MissedFork,
    MissedPin,
    MissedTactic,
    Positional
}

public static class MoveQualityInfo
{
    /// <summary>
    /// Sınıflandırma: hamlenin kazanma şansından (yüzde puanı) ne kadar kaybettirdiği.
    /// </summary>
    public static MoveQuality Classify(double winLoss, bool isBest, bool wasCritical)
    {
        if (isBest) return wasCritical ? MoveQuality.Great : MoveQuality.Best;
        return winLoss switch
        {
            < 2 => MoveQuality.Excellent,
            < 5 => MoveQuality.Good,
            < 10 => MoveQuality.Inaccuracy,
            < 20 => MoveQuality.Mistake,
            _ => MoveQuality.Blunder
        };
    }

    public static string Label(this MoveQuality q) => q switch
    {
        MoveQuality.Great => "Harika",
        MoveQuality.Best => "En iyi",
        MoveQuality.Excellent => "Mükemmel",
        MoveQuality.Good => "İyi",
        MoveQuality.Inaccuracy => "Yanlışlık",
        MoveQuality.Mistake => "Hata",
        MoveQuality.Blunder => "Ciddi hata",
        _ => ""
    };

    public static string Icon(this MoveQuality q) => q switch
    {
        MoveQuality.Great => "🌟",
        MoveQuality.Best => "✅",
        MoveQuality.Excellent => "👍",
        MoveQuality.Good => "🙂",
        MoveQuality.Inaccuracy => "⚠️",
        MoveQuality.Mistake => "❌",
        MoveQuality.Blunder => "💥",
        _ => ""
    };

    /// <summary>Notasyon sembolü: !!, ?!, ?, ??</summary>
    public static string Symbol(this MoveQuality q) => q switch
    {
        MoveQuality.Great => "!!",
        MoveQuality.Inaccuracy => "?!",
        MoveQuality.Mistake => "?",
        MoveQuality.Blunder => "??",
        _ => ""
    };

    /// <summary>Arayüz rengi (hex).</summary>
    public static string ColorHex(this MoveQuality q) => q switch
    {
        MoveQuality.Great => "#1BADA6",
        MoveQuality.Best => "#5C9E31",
        MoveQuality.Excellent => "#7CB342",
        MoveQuality.Good => "#9CB86B",
        MoveQuality.Inaccuracy => "#E6A23C",
        MoveQuality.Mistake => "#E07B39",
        MoveQuality.Blunder => "#D9443C",
        _ => "#888888"
    };

    public static bool IsError(this MoveQuality q) => q >= MoveQuality.Inaccuracy;

    public static string Label(this MistakeMotif m) => m switch
    {
        MistakeMotif.HungPiece => "Taş bırakmak",
        MistakeMotif.AllowedMate => "Mata izin vermek",
        MistakeMotif.AllowedFork => "Çatala izin vermek",
        MistakeMotif.AllowedTactic => "Rakibin taktiğini görmemek",
        MistakeMotif.MissedMate => "Mat fırsatını kaçırmak",
        MistakeMotif.MissedCapture => "Bedava taşı almamak",
        MistakeMotif.MissedFork => "Çatal fırsatını kaçırmak",
        MistakeMotif.MissedPin => "Açmaz fırsatını kaçırmak",
        MistakeMotif.MissedTactic => "Taktik fırsatını kaçırmak",
        MistakeMotif.Positional => "Konumsal hata",
        _ => "—"
    };

    /// <summary>Hata türüne göre çalışma tavsiyesi.</summary>
    public static string Tip(this MistakeMotif m) => m switch
    {
        MistakeMotif.HungPiece => "Her hamleden önce kendine sor: \"Bu hamleden sonra rakip neyi alabilir?\" Korumasız taş bırakma.",
        MistakeMotif.AllowedMate => "Şah güvenliğine dikkat et. Rakibin şah çekebileceği tüm hamleleri kontrol et.",
        MistakeMotif.AllowedFork => "Taşlarını rakip atın ve vezirin çift tehdit yapabileceği karelerden uzak tut.",
        MistakeMotif.AllowedTactic => "Rakibin zorlayıcı hamlelerini (şah, alma, tehdit) hamleni yapmadan önce hesapla.",
        MistakeMotif.MissedMate => "Her hamlede ilk önce şah çeken hamlelere bak; mat ağları çoğu zaman oradan çıkar.",
        MistakeMotif.MissedCapture => "Rakibin korumasız taşlarını tara. Bedava taşı almak en kolay kazançtır.",
        MistakeMotif.MissedFork => "Atının ve vezirinin gidebileceği karelerden iki taşı birden tehdit edip edemediğine bak.",
        MistakeMotif.MissedPin => "Aynı çizgide duran rakip taşlarına dikkat et; fil ve kale ile açmaz fırsatları çıkar.",
        MistakeMotif.MissedTactic => "Zorlayıcı hamleleri (şah, alma, tehdit) sırayla dene ve 2-3 hamle ileri hesapla.",
        MistakeMotif.Positional => "Taşlarını geliştir, merkezi kontrol et, şahını rok ile güvene al ve en kötü taşını iyileştir.",
        _ => ""
    };
}
