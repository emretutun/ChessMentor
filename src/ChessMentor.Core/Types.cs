namespace ChessMentor.Core;

public enum PieceColor : byte
{
    White = 0,
    Black = 1
}

public enum PieceType : byte
{
    None = 0,
    Pawn = 1,
    Knight = 2,
    Bishop = 3,
    Rook = 4,
    Queen = 5,
    King = 6
}

[Flags]
public enum CastlingRights : byte
{
    None = 0,
    WhiteKingSide = 1,
    WhiteQueenSide = 2,
    BlackKingSide = 4,
    BlackQueenSide = 8,
    All = WhiteKingSide | WhiteQueenSide | BlackKingSide | BlackQueenSide
}

[Flags]
public enum MoveFlags : byte
{
    None = 0,
    Capture = 1,
    EnPassant = 2,
    Castle = 4,
    DoublePush = 8,
    Promotion = 16
}

public static class PieceColorExtensions
{
    public static PieceColor Opposite(this PieceColor color) =>
        color == PieceColor.White ? PieceColor.Black : PieceColor.White;
}

/// <summary>Bir taş (tür + renk). <c>default</c> değeri boş kareyi temsil eder.</summary>
public readonly record struct Piece(PieceType Type, PieceColor Color)
{
    public static readonly Piece None = default;

    public bool IsNone => Type == PieceType.None;

    /// <summary>Kaba materyal değeri (piyon = 1).</summary>
    public int Value => Type switch
    {
        PieceType.Pawn => 1,
        PieceType.Knight => 3,
        PieceType.Bishop => 3,
        PieceType.Rook => 5,
        PieceType.Queen => 9,
        _ => 0
    };

    public char FenChar
    {
        get
        {
            char c = Type switch
            {
                PieceType.Pawn => 'p',
                PieceType.Knight => 'n',
                PieceType.Bishop => 'b',
                PieceType.Rook => 'r',
                PieceType.Queen => 'q',
                PieceType.King => 'k',
                _ => '.'
            };
            return Color == PieceColor.White ? char.ToUpperInvariant(c) : c;
        }
    }

    public static Piece FromFenChar(char c)
    {
        var color = char.IsUpper(c) ? PieceColor.White : PieceColor.Black;
        var type = char.ToLowerInvariant(c) switch
        {
            'p' => PieceType.Pawn,
            'n' => PieceType.Knight,
            'b' => PieceType.Bishop,
            'r' => PieceType.Rook,
            'q' => PieceType.Queen,
            'k' => PieceType.King,
            _ => throw new FormatException($"Geçersiz FEN taş karakteri: '{c}'")
        };
        return new Piece(type, color);
    }

    public override string ToString() => IsNone ? "." : FenChar.ToString();
}

/// <summary>Kare yardımcıları. Kareler 0..63 arasıdır: a1 = 0, h1 = 7, a8 = 56, h8 = 63.</summary>
public static class Square
{
    public const int None = -1;

    public static int File(int sq) => sq & 7;
    public static int Rank(int sq) => sq >> 3;
    public static int Make(int file, int rank) => rank * 8 + file;
    public static bool IsValid(int file, int rank) => file is >= 0 and < 8 && rank is >= 0 and < 8;

    public static string Name(int sq) => sq < 0 ? "-" : $"{(char)('a' + File(sq))}{(char)('1' + Rank(sq))}";

    public static int Parse(string name)
    {
        if (name is not { Length: 2 }) throw new FormatException($"Geçersiz kare: '{name}'");
        int file = name[0] - 'a';
        int rank = name[1] - '1';
        if (!IsValid(file, rank)) throw new FormatException($"Geçersiz kare: '{name}'");
        return Make(file, rank);
    }
}

/// <summary>Tek bir hamle. UCI biçimine (ör. e2e4, e7e8q) çevrilebilir.</summary>
public readonly record struct Move(int From, int To, PieceType Promotion = PieceType.None, MoveFlags Flags = MoveFlags.None)
{
    public bool IsCapture => (Flags & MoveFlags.Capture) != 0;
    public bool IsCastle => (Flags & MoveFlags.Castle) != 0;
    public bool IsEnPassant => (Flags & MoveFlags.EnPassant) != 0;
    public bool IsPromotion => Promotion != PieceType.None;

    public string ToUci()
    {
        var s = Square.Name(From) + Square.Name(To);
        if (IsPromotion)
        {
            s += Promotion switch
            {
                PieceType.Queen => "q",
                PieceType.Rook => "r",
                PieceType.Bishop => "b",
                PieceType.Knight => "n",
                _ => ""
            };
        }
        return s;
    }

    /// <summary>Taşın tür/kare olarak eşleşmesi (bayraklar hariç).</summary>
    public bool SameAs(Move other) => From == other.From && To == other.To && Promotion == other.Promotion;

    public override string ToString() => ToUci();
}
