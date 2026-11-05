using System.Text;
using ChessMentor.Core;

namespace ChessMentor.Mentor;

/// <summary>Oyun sonu raporu: doğruluk, hamle dağılımı, en büyük hatalar ve tavsiye.</summary>
public sealed record GameReport(
    double Accuracy,
    IReadOnlyDictionary<MoveQuality, int> Counts,
    IReadOnlyList<MoveReview> WorstMoves,
    MistakeMotif MostCommonMotif,
    string ResultText)
{
    public static GameReport Build(IReadOnlyList<MoveReview> reviews, Game game, PieceColor userColor)
    {
        var counts = Enum.GetValues<MoveQuality>().ToDictionary(q => q, q => reviews.Count(r => r.Quality == q));
        double accuracy = reviews.Count == 0 ? 0 : reviews.Average(r => r.Accuracy);

        var worst = reviews
            .Where(r => r.Quality.IsError())
            .OrderByDescending(r => r.WinLoss)
            .Take(3)
            .ToList();

        var motif = reviews
            .Where(r => r.Quality.IsError() && r.Motif != MistakeMotif.None)
            .GroupBy(r => r.Motif)
            .OrderByDescending(g => g.Count())
            .Select(g => g.Key)
            .FirstOrDefault();

        return new GameReport(accuracy, counts, worst, motif, DescribeResult(game, userColor));
    }

    public static string DescribeResult(Game game, PieceColor userColor)
    {
        var outcome = game.Outcome;
        bool userWon = (outcome == GameOutcome.WhiteWins && userColor == PieceColor.White)
                    || (outcome == GameOutcome.BlackWins && userColor == PieceColor.Black);

        string reason = game.EndReason switch
        {
            GameEndReason.Checkmate => "mat",
            GameEndReason.Stalemate => "pat",
            GameEndReason.FiftyMoveRule => "50 hamle kuralı",
            GameEndReason.ThreefoldRepetition => "üçlü tekrar",
            GameEndReason.InsufficientMaterial => "yetersiz materyal",
            GameEndReason.Resignation => "terk",
            _ => ""
        };

        return outcome switch
        {
            GameOutcome.Draw => $"🤝 Berabere ({reason})",
            _ when userWon => $"🏆 Kazandın! ({reason})",
            GameOutcome.Ongoing => "Oyun devam ediyor",
            _ => $"😔 Kaybettin ({reason})"
        };
    }

    public string ToText()
    {
        var sb = new StringBuilder();
        sb.AppendLine(ResultText);
        sb.AppendLine($"Doğruluk: %{Accuracy:0}");
        sb.AppendLine();

        foreach (var q in Enum.GetValues<MoveQuality>())
            if (Counts.TryGetValue(q, out var c) && c > 0)
                sb.AppendLine($"{q.Icon()} {q.Label()}: {c}");

        if (WorstMoves.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("En büyük hataların:");
            foreach (var r in WorstMoves)
                sb.AppendLine($"• {r.MoveLabel} → daha iyisi {r.BestSan} ({r.Quality.Label()})");
        }

        if (MostCommonMotif != MistakeMotif.None)
        {
            sb.AppendLine();
            sb.AppendLine($"Bu oyunda en sık yaptığın hata: {MostCommonMotif.Label()}");
            sb.AppendLine($"Tavsiye: {MostCommonMotif.Tip()}");
        }
        return sb.ToString().TrimEnd();
    }
}
