using ChessMentor.Engine;

namespace ChessMentor.Mentor;

/// <summary>
/// Motor puanını kazanma şansına (0-100) çevirir. Lichess'in kullandığı lojistik formül:
/// win% = 50 + 50 * (2 / (1 + e^(-0.00368208 * cp)) - 1)
/// Bu sayede "+8'den +6'ya düşmek" ile "+0.5'ten -1.5'e düşmek" doğru şekilde farklı değerlendirilir.
/// </summary>
public static class WinChance
{
    private const double K = 0.00368208;

    public static double FromCentipawns(int cp)
    {
        cp = Math.Clamp(cp, -1000, 1000);
        return 50 + 50 * (2 / (1 + Math.Exp(-K * cp)) - 1);
    }

    /// <summary>Puan sırası gelen taraf açısından olduğu için sonuç da o tarafın kazanma şansıdır.</summary>
    public static double FromScore(Score score)
    {
        if (score.MateIn is int m) return m > 0 ? 100 : 0;
        return FromCentipawns(score.Centipawns ?? 0);
    }

    /// <summary>
    /// Hamle doğruluğu (0-100). Lichess formülü: 103.1668 * e^(-0.04354 * kayıp) - 3.1669
    /// </summary>
    public static double Accuracy(double winLoss)
    {
        var acc = 103.1668 * Math.Exp(-0.04354 * Math.Max(0, winLoss)) - 3.1669;
        return Math.Clamp(acc, 0, 100);
    }
}
