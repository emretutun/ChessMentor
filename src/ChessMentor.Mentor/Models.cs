using ChessMentor.Core;
using ChessMentor.Engine;

namespace ChessMentor.Mentor;

/// <summary>Bir pozisyonun motor analizi.</summary>
public sealed record PositionAnalysis(Position Position, SearchResult Result)
{
    public IReadOnlyList<EngineLine> Lines => Result.Lines;
    public EngineLine? Best => Result.Best;

    /// <summary>Sırası gelen tarafın kazanma şansı (0-100).</summary>
    public double BestWin => Best is null ? 50 : WinChance.FromScore(Best.Score);

    /// <summary>Beyaz açısından puan (değerlendirme çubuğu için).</summary>
    public Score WhiteScore
    {
        get
        {
            var s = Best?.Score ?? Score.Cp(0);
            return Position.SideToMove == PieceColor.White ? s : s.Negate();
        }
    }

    /// <summary>
    /// Kritik an: en iyi hamle ile ikinci en iyi hamle arasında büyük fark var
    /// (yani "tek iyi hamle" durumu) ve pozisyon zaten tamamen kazanılmış değil.
    /// </summary>
    public bool IsCritical
    {
        get
        {
            if (Lines.Count < 2) return false;
            var first = WinChance.FromScore(Lines[0].Score);
            var second = WinChance.FromScore(Lines[1].Score);
            return first - second >= 15 && second < 85;
        }
    }

    public EngineLine? LineFor(string uci) =>
        Lines.FirstOrDefault(l => string.Equals(l.FirstMove, uci, StringComparison.OrdinalIgnoreCase));
}

/// <summary>Kullanıcının bir hamlesinin değerlendirmesi.</summary>
public sealed record MoveReview
{
    public required int Ply { get; init; }
    public required int MoveNumber { get; init; }
    public required PieceColor Color { get; init; }
    public required Move Move { get; init; }
    public required string San { get; init; }
    public required MoveQuality Quality { get; init; }
    public required double WinBefore { get; init; }
    public required double WinAfter { get; init; }
    public double WinLoss => Math.Max(0, WinBefore - WinAfter);
    public double Accuracy => WinChance.Accuracy(WinLoss);
    public string? BestMoveUci { get; init; }
    public string? BestSan { get; init; }
    public string? BestLine { get; init; }
    public required string Headline { get; init; }
    public required IReadOnlyList<string> Details { get; init; }
    public IReadOnlyList<string> Better { get; init; } = [];
    public MistakeMotif Motif { get; init; }

    public string MoveLabel => Color == PieceColor.White ? $"{MoveNumber}. {San}" : $"{MoveNumber}... {San}";
}

/// <summary>Rakibin yaptığı tehdit (sen hamle yapmasan ne olurdu?).</summary>
public sealed record ThreatInfo(string MoveUci, string San, string Description, bool IsMateThreat);

/// <summary>Kademeli ipucu.</summary>
public sealed record Hint(int Level, string Text, IReadOnlyList<int> HighlightSquares, Move? Arrow)
{
    public const int MaxLevel = 4;
}
