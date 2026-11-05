namespace ChessMentor.Engine;

/// <summary>
/// Motor puanı. Her zaman <b>sırası gelen taraf</b> açısındandır (UCI standardı).
/// Ya santipiyon (<see cref="Centipawns"/>) ya da mat mesafesi (<see cref="MateIn"/>) doludur.
/// </summary>
public readonly record struct Score(int? Centipawns, int? MateIn)
{
    public static Score Cp(int cp) => new(cp, null);
    public static Score Mate(int moves) => new(null, moves);

    public bool IsMate => MateIn.HasValue;

    /// <summary>Karşılaştırma için tek bir sayıya indirger. Mat ±100000'e yakındır.</summary>
    public int ToComparable()
    {
        if (MateIn is int m)
            return m > 0 ? 100_000 - m * 100 : -100_000 - m * 100;
        return Centipawns ?? 0;
    }

    /// <summary>Karşı tarafın gözünden aynı puan.</summary>
    public Score Negate() => new(-Centipawns, -MateIn);

    /// <summary>"+1.25", "-0.40", "M3", "-M2" biçiminde gösterim.</summary>
    public string Format()
    {
        if (MateIn is int m) return m > 0 ? $"M{m}" : $"-M{-m}";
        double pawns = (Centipawns ?? 0) / 100.0;
        return pawns.ToString("+0.00;-0.00;0.00", System.Globalization.CultureInfo.InvariantCulture);
    }

    public override string ToString() => Format();
}

/// <summary>Motorun bulduğu bir varyant (MultiPV satırı).</summary>
public sealed record EngineLine(int MultiPv, int Depth, Score Score, IReadOnlyList<string> Pv)
{
    public string? FirstMove => Pv.Count > 0 ? Pv[0] : null;
}

public sealed record SearchResult(string BestMove, string? Ponder, IReadOnlyList<EngineLine> Lines)
{
    public EngineLine? Best => Lines.Count > 0 ? Lines[0] : null;
}

/// <summary>Bir aramanın sınırları. En az biri belirtilmeli.</summary>
public sealed record SearchLimits
{
    public int? Depth { get; init; }
    public int? MoveTimeMs { get; init; }
    public long? Nodes { get; init; }
    public int MultiPv { get; init; } = 1;

    public static SearchLimits Time(int ms, int multiPv = 1) => new() { MoveTimeMs = ms, MultiPv = multiPv };
    public static SearchLimits ToDepth(int depth, int multiPv = 1) => new() { Depth = depth, MultiPv = multiPv };
}

/// <summary>Bot gücü ayarı. Stockfish'in UCI_Elo / Skill Level seçenekleriyle eşleşir.</summary>
public sealed record BotLevel(string Name, string Description, int ApproxElo, int? UciElo, int? SkillLevel, int? Depth, int MoveTimeMs)
{
    public override string ToString() => $"{Name} (≈{ApproxElo})";

    /// <summary>
    /// Hazır seviyeler. Stockfish'in UCI_Elo'su ~1320'nin altına inmediği için zayıf seviyelerde
    /// Skill Level ve arama derinliği sınırı kullanılır. Elo değerleri yaklaşıktır.
    /// </summary>
    public static IReadOnlyList<BotLevel> All { get; } =
    [
        new("Başlangıç", "Kuralları yeni öğrenenler için. Sık sık taş bırakır.", 600, null, 0, 1, 50),
        new("Acemi", "Basit tehditleri görür, derin hesap yapmaz.", 900, null, 2, 2, 100),
        new("Kulüp altı", "Tek hamlelik taktikleri yakalar.", 1100, null, 4, 4, 200),
        new("Orta", "Düzenli oynayan bir amatör gibi.", 1400, 1400, null, null, 500),
        new("İyi", "Kulüp oyuncusu seviyesi.", 1600, 1600, null, null, 600),
        new("Güçlü", "Taktiklerde dikkatli olmanı gerektirir.", 1800, 1800, null, null, 700),
        new("Uzman", "Ciddi bir rakip.", 2000, 2000, null, null, 800),
        new("Usta", "Usta seviyesi oyun.", 2300, 2300, null, null, 1000),
        new("Büyükusta", "Çok güçlü.", 2700, 2700, null, null, 1200),
        new("Tam güç", "Stockfish'in sınırsız hali. İyi şanslar!", 3500, null, null, null, 1500),
    ];
}
