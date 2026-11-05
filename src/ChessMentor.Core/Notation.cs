using System.Text;

namespace ChessMentor.Core;

public enum NotationLanguage
{
    /// <summary>Türkçe harfler: Ş (şah), V (vezir), K (kale), F (fil), A (at).</summary>
    Turkish = 0,
    /// <summary>Uluslararası harfler: K, Q, R, B, N. PGN dosyaları her zaman bunu kullanır.</summary>
    English = 1,
    /// <summary>Yeni başlayanlar için açık yazım: "At g1 → f3", "Kısa rok", "Piyon e7 → e8 (vezir olur)".</summary>
    Easy = 2
}

/// <summary>Kısa cebirsel notasyon (SAN) üretimi ve Türkçe taş adları.</summary>
public static class Notation
{
    public static string PieceLetter(PieceType type, NotationLanguage lang) => (type, lang) switch
    {
        (PieceType.King, NotationLanguage.Turkish) => "Ş",
        (PieceType.Queen, NotationLanguage.Turkish) => "V",
        (PieceType.Rook, NotationLanguage.Turkish) => "K",
        (PieceType.Bishop, NotationLanguage.Turkish) => "F",
        (PieceType.Knight, NotationLanguage.Turkish) => "A",
        (PieceType.King, _) => "K",
        (PieceType.Queen, _) => "Q",
        (PieceType.Rook, _) => "R",
        (PieceType.Bishop, _) => "B",
        (PieceType.Knight, _) => "N",
        _ => ""
    };

    /// <summary>Hamleyi SAN olarak yazar (ör. "Af3", "exd5", "O-O", "e8=V+").</summary>
    public static string ToSan(Position pos, Move move, NotationLanguage lang = NotationLanguage.Turkish)
    {
        if (lang == NotationLanguage.Easy) return ToEasy(pos, move);
        var piece = pos[move.From];
        var sb = new StringBuilder(8);

        if (move.IsCastle)
        {
            sb.Append(move.To > move.From ? "O-O" : "O-O-O");
        }
        else
        {
            bool capture = !pos[move.To].IsNone || move.IsEnPassant;

            if (piece.Type == PieceType.Pawn)
            {
                if (capture) sb.Append((char)('a' + Square.File(move.From)));
            }
            else
            {
                sb.Append(PieceLetter(piece.Type, lang));
                sb.Append(Disambiguation(pos, move, piece));
            }

            if (capture) sb.Append('x');
            sb.Append(Square.Name(move.To));

            if (move.IsPromotion)
            {
                sb.Append('=');
                sb.Append(PieceLetter(move.Promotion, lang));
            }
        }

        var next = pos.MakeMove(move);
        if (next.InCheck)
            sb.Append(next.GenerateLegalMoves().Count == 0 ? '#' : '+');

        return sb.ToString();
    }

    /// <summary>
    /// Kolay okunur yazım: "At g1 → f3", "Fil c8 → g4 (alır)", "Kısa rok", "Vezir d1 → h5 (şah!)".
    /// </summary>
    public static string ToEasy(Position pos, Move move)
    {
        var piece = pos[move.From];
        var sb = new StringBuilder(24);

        if (move.IsCastle)
        {
            sb.Append(move.To > move.From ? "Kısa rok" : "Uzun rok");
        }
        else
        {
            sb.Append(Turkish.Capitalize(Turkish.PieceName(piece.Type)))
              .Append(' ').Append(Square.Name(move.From))
              .Append(" → ").Append(Square.Name(move.To));

            var notes = new List<string>(2);
            if (!pos[move.To].IsNone || move.IsEnPassant) notes.Add("alır");
            if (move.IsPromotion) notes.Add($"{Turkish.PieceName(move.Promotion)} olur");
            if (notes.Count > 0) sb.Append(" (").Append(string.Join(", ", notes)).Append(')');
        }

        var next = pos.MakeMove(move);
        if (next.InCheck)
            sb.Append(next.GenerateLegalMoves().Count == 0 ? " · MAT!" : " · şah!");
        return sb.ToString();
    }

    private static string Disambiguation(Position pos, Move move, Piece piece)
    {
        var rivals = pos.GenerateLegalMoves()
            .Where(m => m.To == move.To && m.From != move.From && pos[m.From] == piece)
            .ToList();
        if (rivals.Count == 0) return "";

        bool sameFile = rivals.Any(m => Square.File(m.From) == Square.File(move.From));
        bool sameRank = rivals.Any(m => Square.Rank(m.From) == Square.Rank(move.From));

        if (!sameFile) return ((char)('a' + Square.File(move.From))).ToString();
        if (!sameRank) return ((char)('1' + Square.Rank(move.From))).ToString();
        return Square.Name(move.From);
    }

    /// <summary>UCI hamle dizisini (motorun varyantı) SAN listesine çevirir. Geçersiz hamlede durur.</summary>
    public static List<string> UciLineToSan(Position start, IEnumerable<string> uciMoves, NotationLanguage lang = NotationLanguage.Turkish, int maxMoves = int.MaxValue)
    {
        var result = new List<string>();
        var pos = start;
        foreach (var uci in uciMoves)
        {
            if (result.Count >= maxMoves) break;
            var m = pos.FindLegalMove(uci);
            if (m is null) break;
            result.Add(ToSan(pos, m.Value, lang));
            pos = pos.MakeMove(m.Value);
        }
        return result;
    }

    /// <summary>SAN listesini hamle numaralarıyla biçimlendirir: "12. Af3 Ae4 13. Vd2".</summary>
    public static string FormatLine(Position start, IReadOnlyList<string> sanMoves)
    {
        // Kolay yazımda hamle numarası yerine sade bir sıra: "Fil c8 → g4, sonra Piyon e4 → e5, ..."
        if (sanMoves.Count > 0 && sanMoves[0].Contains('→') || sanMoves.Count > 0 && sanMoves[0].Contains(" rok"))
            return string.Join(", sonra ", sanMoves.Take(3));

        var sb = new StringBuilder();
        int number = start.FullmoveNumber;
        bool white = start.SideToMove == PieceColor.White;

        for (int i = 0; i < sanMoves.Count; i++)
        {
            if (white)
            {
                sb.Append(number).Append(". ");
            }
            else if (i == 0)
            {
                sb.Append(number).Append("... ");
            }

            sb.Append(sanMoves[i]).Append(' ');
            if (!white) number++;
            white = !white;
        }
        return sb.ToString().TrimEnd();
    }
}

/// <summary>Türkçe dil yardımcıları: taş adları ve kare ekleri (e5'teki, a1'e ...).</summary>
public static class Turkish
{
    public static string PieceName(PieceType type) => type switch
    {
        PieceType.King => "şah",
        PieceType.Queen => "vezir",
        PieceType.Rook => "kale",
        PieceType.Bishop => "fil",
        PieceType.Knight => "at",
        PieceType.Pawn => "piyon",
        _ => "taş"
    };

    /// <summary>Belirtme hâli: "atı", "veziri", "kaleyi", "piyonu".</summary>
    public static string PieceAccusative(PieceType type) => type switch
    {
        PieceType.King => "şahı",
        PieceType.Queen => "veziri",
        PieceType.Rook => "kaleyi",
        PieceType.Bishop => "fili",
        PieceType.Knight => "atı",
        PieceType.Pawn => "piyonu",
        _ => "taşı"
    };

    /// <summary>İlk harfi büyütür; kare adıyla başlıyorsa ("c8'deki") dokunmaz.</summary>
    /// <summary>Araç hâli (iyelikli): "filiyle", "atıyla", "veziriyle".</summary>
    public static string PieceWith(PieceType type) => type switch
    {
        PieceType.King => "şahıyla",
        PieceType.Queen => "veziriyle",
        PieceType.Rook => "kalesiyle",
        PieceType.Bishop => "filiyle",
        PieceType.Knight => "atıyla",
        PieceType.Pawn => "piyonuyla",
        _ => "taşıyla"
    };

    /// <summary>"Senin" taşın, belirtme hâli: "vezirini", "atını", "kaleni".</summary>
    public static string YourPieceAccusative(PieceType type) => type switch
    {
        PieceType.King => "şahını",
        PieceType.Queen => "vezirini",
        PieceType.Rook => "kaleni",
        PieceType.Bishop => "filini",
        PieceType.Knight => "atını",
        PieceType.Pawn => "piyonunu",
        _ => "taşını"
    };

    public static string Capitalize(string s)
    {
        if (string.IsNullOrEmpty(s)) return s;
        if (s.Length >= 2 && s[0] is >= 'a' and <= 'h' && char.IsDigit(s[1])) return s;
        return char.ToUpper(s[0], new System.Globalization.CultureInfo("tr-TR")) + s[1..];
    }

    // Kare adları rakamla biter; ek, rakamın okunuşuna göre seçilir (bir, iki, üç, dört, beş, altı, yedi, sekiz).
    private static readonly string[] Locative = ["'deki", "'deki", "'teki", "'teki", "'teki", "'daki", "'deki", "'deki"];
    private static readonly string[] Dative = ["'e", "'ye", "'e", "'e", "'e", "'ya", "'ye", "'e"];
    private static readonly string[] LocativeSimple = ["'de", "'de", "'te", "'te", "'te", "'da", "'de", "'de"];

    /// <summary>"e5'teki", "a1'deki".</summary>
    public static string At(int sq) => Square.Name(sq) + Locative[Square.Rank(sq)];

    /// <summary>"e5'e", "h6'ya".</summary>
    public static string To(int sq) => Square.Name(sq) + Dative[Square.Rank(sq)];

    /// <summary>"e5'te", "g6'da".</summary>
    public static string On(int sq) => Square.Name(sq) + LocativeSimple[Square.Rank(sq)];

    /// <summary>"e5'teki at".</summary>
    public static string PieceOn(Position pos, int sq) => $"{At(sq)} {PieceName(pos[sq].Type)}";
}
