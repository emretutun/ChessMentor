using ChessMentor.Core;

namespace ChessMentor.Mentor;

/// <summary>
/// Basit taktik motif tespitleri. Bunlar motorun bulduğu hamleyi <i>açıklamak</i> için kullanılır;
/// hangi hamlenin iyi olduğuna her zaman motor karar verir.
/// </summary>
public static class Tactics
{
    /// <summary>Saldırı hesabında şahın değeri (şah "alınamaz" kabul edilir).</summary>
    private static int AttackValue(Piece p) => p.Type == PieceType.King ? 100 : p.Value;

    /// <summary>
    /// Kare üzerindeki taş tehlikede mi? Rakip saldırıyor ve (taş korumasız) ya da
    /// (saldıran en ucuz taş, bu taştan daha değersiz).
    /// </summary>
    public static bool IsHanging(Position pos, int sq)
    {
        var piece = pos[sq];
        if (piece.IsNone || piece.Type == PieceType.King) return false;

        var attackers = pos.GetAttackers(sq, piece.Color.Opposite());
        if (attackers.Count == 0) return false;

        var defenders = pos.GetAttackers(sq, piece.Color);
        if (defenders.Count == 0) return true;

        int cheapest = attackers.Min(a => AttackValue(pos[a]));
        return cheapest < piece.Value;
    }

    /// <summary><paramref name="owner"/> rengindeki tehlikedeki taşlar, değerliden ucuza.</summary>
    public static List<int> HangingPieces(Position pos, PieceColor owner)
    {
        var list = new List<int>();
        for (int sq = 0; sq < 64; sq++)
        {
            var p = pos[sq];
            if (!p.IsNone && p.Color == owner && IsHanging(pos, sq)) list.Add(sq);
        }
        return list.OrderByDescending(s => pos[s].Value).ToList();
    }

    /// <summary>
    /// Karedeki taşın aynı anda tehdit ettiği "anlamlı" rakip taşlar: şah, kendisinden değerli taşlar
    /// ve korumasız taşlar. İki veya daha fazlası varsa bu bir çataldır.
    /// </summary>
    public static List<int> ForkTargets(Position pos, int sq)
    {
        var attacker = pos[sq];
        var targets = new List<int>();
        if (attacker.IsNone) return targets;

        foreach (var to in pos.AttackedSquaresFrom(sq))
        {
            var target = pos[to];
            if (target.IsNone || target.Color == attacker.Color) continue;
            bool meaningful = target.Type == PieceType.King
                || target.Value > attacker.Value
                || pos.GetAttackers(to, target.Color).Count == 0;
            if (meaningful) targets.Add(to);
        }
        return targets;
    }

    public static bool IsFork(Position pos, int sq) => ForkTargets(pos, sq).Count >= 2;

    public readonly record struct Pin(int Pinner, int Pinned, int Behind);

    /// <summary><paramref name="pinnerColor"/> renginin uzun menzilli taşlarının yaptığı açmazlar.</summary>
    public static List<Pin> FindPins(Position pos, PieceColor pinnerColor)
    {
        var pins = new List<Pin>();
        (int df, int dr)[] diag = [(1, 1), (-1, 1), (1, -1), (-1, -1)];
        (int df, int dr)[] straight = [(1, 0), (-1, 0), (0, 1), (0, -1)];

        for (int sq = 0; sq < 64; sq++)
        {
            var p = pos[sq];
            if (p.IsNone || p.Color != pinnerColor) continue;
            if (p.Type is PieceType.Bishop or PieceType.Queen) Scan(sq, diag);
            if (p.Type is PieceType.Rook or PieceType.Queen) Scan(sq, straight);
        }
        return pins;

        void Scan(int from, (int df, int dr)[] dirs)
        {
            foreach (var (df, dr) in dirs)
            {
                int f = Square.File(from) + df, r = Square.Rank(from) + dr;
                int first = Square.None;
                while (Square.IsValid(f, r))
                {
                    int s = Square.Make(f, r);
                    var piece = pos[s];
                    if (!piece.IsNone)
                    {
                        if (piece.Color == pinnerColor) break;
                        if (first == Square.None)
                        {
                            first = s;
                        }
                        else
                        {
                            if (AttackValue(piece) > pos[first].Value) pins.Add(new Pin(from, first, s));
                            break;
                        }
                    }
                    f += df;
                    r += dr;
                }
            }
        }
    }

    /// <summary>
    /// Motor varyantı boyunca (en fazla <paramref name="maxPlies"/> yarım hamle) sırası gelen tarafın
    /// materyal kazancı. "Bu devam yaklaşık 3 puan kazandırıyor" demek için.
    /// </summary>
    public static int MaterialSwing(Position start, IReadOnlyList<string> pv, int maxPlies = 6)
    {
        var side = start.SideToMove;
        int initial = start.MaterialBalance(side);
        var pos = start;
        int plies = Math.Min(maxPlies, pv.Count);
        for (int i = 0; i < plies; i++)
        {
            var m = pos.FindLegalMove(pv[i]);
            if (m is null) break;
            pos = pos.MakeMove(m.Value);
        }
        return pos.MaterialBalance(side) - initial;
    }

    public static bool GivesCheck(Position pos, Move m) => pos.MakeMove(m).InCheck;

    /// <summary>"e5'teki at ve d7'deki vezir" gibi bir liste.</summary>
    public static string DescribeSquares(Position pos, IEnumerable<int> squares)
    {
        var parts = squares.Select(s => Turkish.PieceOn(pos, s)).ToList();
        return parts.Count switch
        {
            0 => "",
            1 => parts[0],
            _ => string.Join(", ", parts.Take(parts.Count - 1)) + " ve " + parts[^1]
        };
    }
}
