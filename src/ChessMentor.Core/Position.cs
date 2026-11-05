using System.Text;

namespace ChessMentor.Core;

/// <summary>
/// Bir satranç pozisyonu. Değişmez gibi kullanılır: <see cref="MakeMove"/> yeni bir pozisyon döndürür.
/// </summary>
public sealed class Position
{
    public const string StartFen = "rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1";

    private static readonly (int df, int dr)[] KnightOffsets =
        [(1, 2), (2, 1), (2, -1), (1, -2), (-1, -2), (-2, -1), (-2, 1), (-1, 2)];

    private static readonly (int df, int dr)[] KingOffsets =
        [(1, 0), (1, 1), (0, 1), (-1, 1), (-1, 0), (-1, -1), (0, -1), (1, -1)];

    private static readonly (int df, int dr)[] BishopDirs = [(1, 1), (-1, 1), (1, -1), (-1, -1)];
    private static readonly (int df, int dr)[] RookDirs = [(1, 0), (-1, 0), (0, 1), (0, -1)];

    private static readonly PieceType[] PromotionTypes =
        [PieceType.Queen, PieceType.Rook, PieceType.Bishop, PieceType.Knight];

    private readonly Piece[] _board = new Piece[64];

    // Pozisyon dışarıdan değiştirilemediği için pahalı hesaplar bir kez yapılıp saklanır.
    private IReadOnlyList<Move>? _legalMoves;
    private bool? _inCheck;
    private string? _repetitionKey;
    private readonly int[] _kingSquare = [-2, -2];

    public PieceColor SideToMove { get; private set; }
    public CastlingRights Castling { get; private set; }
    public int EnPassantSquare { get; private set; } = Square.None;
    public int HalfmoveClock { get; private set; }
    public int FullmoveNumber { get; private set; } = 1;

    public Piece this[int sq] => _board[sq];

    private Position() { }

    public static Position Start() => FromFen(StartFen);

    #region FEN

    public static Position FromFen(string fen)
    {
        var parts = fen.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 4) throw new FormatException("FEN en az 4 alan içermeli.");

        var p = new Position();
        var rows = parts[0].Split('/');
        if (rows.Length != 8) throw new FormatException("FEN tahta kısmı 8 satır içermeli.");

        for (int i = 0; i < 8; i++)
        {
            int rank = 7 - i;
            int file = 0;
            foreach (char c in rows[i])
            {
                if (char.IsDigit(c))
                {
                    file += c - '0';
                }
                else
                {
                    if (file > 7) throw new FormatException("FEN satırı çok uzun.");
                    p._board[Square.Make(file, rank)] = Piece.FromFenChar(c);
                    file++;
                }
            }
            if (file != 8) throw new FormatException("FEN satırı 8 kare içermeli.");
        }

        p.SideToMove = parts[1] == "b" ? PieceColor.Black : PieceColor.White;

        var cr = CastlingRights.None;
        if (parts[2] != "-")
        {
            foreach (char c in parts[2])
            {
                cr |= c switch
                {
                    'K' => CastlingRights.WhiteKingSide,
                    'Q' => CastlingRights.WhiteQueenSide,
                    'k' => CastlingRights.BlackKingSide,
                    'q' => CastlingRights.BlackQueenSide,
                    _ => CastlingRights.None
                };
            }
        }
        p.Castling = cr;
        p.EnPassantSquare = parts[3] == "-" ? Square.None : Square.Parse(parts[3]);
        p.HalfmoveClock = parts.Length > 4 && int.TryParse(parts[4], out var h) ? h : 0;
        p.FullmoveNumber = parts.Length > 5 && int.TryParse(parts[5], out var f) ? Math.Max(1, f) : 1;

        if (p.FindKing(PieceColor.White) < 0 || p.FindKing(PieceColor.Black) < 0)
            throw new FormatException("Her iki tarafın da şahı olmalı.");

        return p;
    }

    public string ToFen() => $"{PlacementFen()} {(SideToMove == PieceColor.White ? 'w' : 'b')} {CastlingFen()} {Square.Name(EnPassantSquare)} {HalfmoveClock} {FullmoveNumber}";

    /// <summary>Tekrar (üçlü tekrar) tespiti için anahtar: saat alanları hariç FEN.</summary>
    public string RepetitionKey => _repetitionKey ??= $"{PlacementFen()} {(SideToMove == PieceColor.White ? 'w' : 'b')} {CastlingFen()} {Square.Name(EnPassantSquare)}";

    private string PlacementFen()
    {
        var sb = new StringBuilder(72);
        for (int rank = 7; rank >= 0; rank--)
        {
            int empty = 0;
            for (int file = 0; file < 8; file++)
            {
                var piece = _board[Square.Make(file, rank)];
                if (piece.IsNone)
                {
                    empty++;
                    continue;
                }
                if (empty > 0) { sb.Append(empty); empty = 0; }
                sb.Append(piece.FenChar);
            }
            if (empty > 0) sb.Append(empty);
            if (rank > 0) sb.Append('/');
        }
        return sb.ToString();
    }

    private string CastlingFen()
    {
        if (Castling == CastlingRights.None) return "-";
        var sb = new StringBuilder(4);
        if (Castling.HasFlag(CastlingRights.WhiteKingSide)) sb.Append('K');
        if (Castling.HasFlag(CastlingRights.WhiteQueenSide)) sb.Append('Q');
        if (Castling.HasFlag(CastlingRights.BlackKingSide)) sb.Append('k');
        if (Castling.HasFlag(CastlingRights.BlackQueenSide)) sb.Append('q');
        return sb.ToString();
    }

    #endregion

    public Position Clone()
    {
        var p = new Position
        {
            SideToMove = SideToMove,
            Castling = Castling,
            EnPassantSquare = EnPassantSquare,
            HalfmoveClock = HalfmoveClock,
            FullmoveNumber = FullmoveNumber
        };
        Array.Copy(_board, p._board, 64);
        return p;
    }

    /// <summary>
    /// Sırayı karşı tarafa veren "boş hamle" pozisyonu. Rakibin tehdidini bulmak için kullanılır.
    /// Şah çekilmiş pozisyonda anlamsızdır, bu yüzden o durumda null döner.
    /// </summary>
    public Position? NullMove()
    {
        if (InCheck) return null;
        var p = Clone();
        p.SideToMove = SideToMove.Opposite();
        p.EnPassantSquare = Square.None;
        return p;
    }

    #region Saldırılar

    public int FindKing(PieceColor color)
    {
        ref int cached = ref _kingSquare[(int)color];
        if (cached == -2) cached = ScanKing(color);
        return cached;
    }

    private int ScanKing(PieceColor color)
    {
        for (int sq = 0; sq < 64; sq++)
        {
            var piece = _board[sq];
            if (piece.Type == PieceType.King && piece.Color == color) return sq;
        }
        return Square.None;
    }

    public bool InCheck
    {
        get
        {
            if (_inCheck is bool cached) return cached;
            int king = FindKing(SideToMove);
            _inCheck = king >= 0 && IsSquareAttacked(king, SideToMove.Opposite());
            return _inCheck.Value;
        }
    }

    public bool IsSquareAttacked(int sq, PieceColor by) => GetAttackers(sq, by, stopAtFirst: true).Count > 0;

    /// <summary>Verilen kareye saldıran <paramref name="by"/> rengindeki taşların kareleri.</summary>
    public List<int> GetAttackers(int sq, PieceColor by) => GetAttackers(sq, by, stopAtFirst: false);

    private List<int> GetAttackers(int sq, PieceColor by, bool stopAtFirst)
    {
        var result = new List<int>(4);
        int f = Square.File(sq), r = Square.Rank(sq);

        // Piyonlar: beyaz piyon bir alt satırdan çapraz saldırır.
        int pawnRank = by == PieceColor.White ? r - 1 : r + 1;
        foreach (int df in (ReadOnlySpan<int>)[-1, 1])
        {
            if (!Square.IsValid(f + df, pawnRank)) continue;
            int from = Square.Make(f + df, pawnRank);
            if (_board[from] == new Piece(PieceType.Pawn, by)) { result.Add(from); if (stopAtFirst) return result; }
        }

        foreach (var (df, dr) in KnightOffsets)
        {
            if (!Square.IsValid(f + df, r + dr)) continue;
            int from = Square.Make(f + df, r + dr);
            if (_board[from] == new Piece(PieceType.Knight, by)) { result.Add(from); if (stopAtFirst) return result; }
        }

        foreach (var (df, dr) in KingOffsets)
        {
            if (!Square.IsValid(f + df, r + dr)) continue;
            int from = Square.Make(f + df, r + dr);
            if (_board[from] == new Piece(PieceType.King, by)) { result.Add(from); if (stopAtFirst) return result; }
        }

        if (ScanSliders(f, r, BishopDirs, by, PieceType.Bishop, result, stopAtFirst) && stopAtFirst) return result;
        ScanSliders(f, r, RookDirs, by, PieceType.Rook, result, stopAtFirst);
        return result;
    }

    private bool ScanSliders(int f, int r, (int df, int dr)[] dirs, PieceColor by, PieceType slider, List<int> result, bool stopAtFirst)
    {
        foreach (var (df, dr) in dirs)
        {
            int cf = f + df, cr = r + dr;
            while (Square.IsValid(cf, cr))
            {
                var piece = _board[Square.Make(cf, cr)];
                if (!piece.IsNone)
                {
                    if (piece.Color == by && (piece.Type == slider || piece.Type == PieceType.Queen))
                    {
                        result.Add(Square.Make(cf, cr));
                        if (stopAtFirst) return true;
                    }
                    break;
                }
                cf += df;
                cr += dr;
            }
        }
        return result.Count > 0;
    }

    /// <summary>Verilen karedeki taşın saldırdığı kareler (taşın kendi rengine bakılmaksızın).</summary>
    public List<int> AttackedSquaresFrom(int sq)
    {
        var result = new List<int>(16);
        var piece = _board[sq];
        if (piece.IsNone) return result;
        int f = Square.File(sq), r = Square.Rank(sq);

        switch (piece.Type)
        {
            case PieceType.Pawn:
                int dir = piece.Color == PieceColor.White ? 1 : -1;
                foreach (int df in (ReadOnlySpan<int>)[-1, 1])
                    if (Square.IsValid(f + df, r + dir)) result.Add(Square.Make(f + df, r + dir));
                break;
            case PieceType.Knight:
                AddSteps(KnightOffsets);
                break;
            case PieceType.King:
                AddSteps(KingOffsets);
                break;
            case PieceType.Bishop:
                AddRays(BishopDirs);
                break;
            case PieceType.Rook:
                AddRays(RookDirs);
                break;
            case PieceType.Queen:
                AddRays(BishopDirs);
                AddRays(RookDirs);
                break;
        }
        return result;

        void AddSteps((int df, int dr)[] offsets)
        {
            foreach (var (df, dr) in offsets)
                if (Square.IsValid(f + df, r + dr)) result.Add(Square.Make(f + df, r + dr));
        }

        void AddRays((int df, int dr)[] dirs)
        {
            foreach (var (df, dr) in dirs)
            {
                int cf = f + df, cr = r + dr;
                while (Square.IsValid(cf, cr))
                {
                    int to = Square.Make(cf, cr);
                    result.Add(to);
                    if (!_board[to].IsNone) break;
                    cf += df;
                    cr += dr;
                }
            }
        }
    }

    #endregion

    #region Hamle üretimi

    /// <summary>Yasal hamleler. Sonuç önbelleğe alınır; aynı pozisyonda tekrar çağırmak ücretsizdir.</summary>
    public IReadOnlyList<Move> GenerateLegalMoves() => _legalMoves ??= ComputeLegalMoves();

    private List<Move> ComputeLegalMoves()
    {
        var pseudo = GeneratePseudoLegalMoves();
        var legal = new List<Move>(pseudo.Count);
        var mover = SideToMove;
        foreach (var m in pseudo)
        {
            var next = MakeMove(m);
            int king = next.FindKing(mover);
            if (!next.IsSquareAttacked(king, mover.Opposite())) legal.Add(m);
        }
        return legal;
    }

    public List<Move> GeneratePseudoLegalMoves()
    {
        var moves = new List<Move>(48);
        var us = SideToMove;
        var them = us.Opposite();

        for (int sq = 0; sq < 64; sq++)
        {
            var piece = _board[sq];
            if (piece.IsNone || piece.Color != us) continue;
            int f = Square.File(sq), r = Square.Rank(sq);

            switch (piece.Type)
            {
                case PieceType.Pawn:
                    GeneratePawnMoves(sq, f, r, us, them, moves);
                    break;
                case PieceType.Knight:
                    GenerateSteps(sq, f, r, KnightOffsets, us, moves);
                    break;
                case PieceType.King:
                    GenerateSteps(sq, f, r, KingOffsets, us, moves);
                    GenerateCastling(sq, us, them, moves);
                    break;
                case PieceType.Bishop:
                    GenerateRays(sq, f, r, BishopDirs, us, moves);
                    break;
                case PieceType.Rook:
                    GenerateRays(sq, f, r, RookDirs, us, moves);
                    break;
                case PieceType.Queen:
                    GenerateRays(sq, f, r, BishopDirs, us, moves);
                    GenerateRays(sq, f, r, RookDirs, us, moves);
                    break;
            }
        }
        return moves;
    }

    private void GeneratePawnMoves(int sq, int f, int r, PieceColor us, PieceColor them, List<Move> moves)
    {
        int dir = us == PieceColor.White ? 1 : -1;
        int startRank = us == PieceColor.White ? 1 : 6;
        int lastRank = us == PieceColor.White ? 7 : 0;

        // İleri
        if (Square.IsValid(f, r + dir))
        {
            int one = Square.Make(f, r + dir);
            if (_board[one].IsNone)
            {
                AddPawnMove(sq, one, r + dir == lastRank, MoveFlags.None, moves);
                if (r == startRank)
                {
                    int two = Square.Make(f, r + 2 * dir);
                    if (_board[two].IsNone) moves.Add(new Move(sq, two, PieceType.None, MoveFlags.DoublePush));
                }
            }
        }

        // Alma
        foreach (int df in (ReadOnlySpan<int>)[-1, 1])
        {
            if (!Square.IsValid(f + df, r + dir)) continue;
            int to = Square.Make(f + df, r + dir);
            var target = _board[to];
            if (!target.IsNone && target.Color == them)
                AddPawnMove(sq, to, r + dir == lastRank, MoveFlags.Capture, moves);
            else if (to == EnPassantSquare)
                moves.Add(new Move(sq, to, PieceType.None, MoveFlags.Capture | MoveFlags.EnPassant));
        }
    }

    private static void AddPawnMove(int from, int to, bool promotes, MoveFlags flags, List<Move> moves)
    {
        if (!promotes)
        {
            moves.Add(new Move(from, to, PieceType.None, flags));
            return;
        }
        foreach (var promo in PromotionTypes)
            moves.Add(new Move(from, to, promo, flags | MoveFlags.Promotion));
    }

    private void GenerateSteps(int sq, int f, int r, (int df, int dr)[] offsets, PieceColor us, List<Move> moves)
    {
        foreach (var (df, dr) in offsets)
        {
            if (!Square.IsValid(f + df, r + dr)) continue;
            int to = Square.Make(f + df, r + dr);
            var target = _board[to];
            if (target.IsNone) moves.Add(new Move(sq, to));
            else if (target.Color != us) moves.Add(new Move(sq, to, PieceType.None, MoveFlags.Capture));
        }
    }

    private void GenerateRays(int sq, int f, int r, (int df, int dr)[] dirs, PieceColor us, List<Move> moves)
    {
        foreach (var (df, dr) in dirs)
        {
            int cf = f + df, cr = r + dr;
            while (Square.IsValid(cf, cr))
            {
                int to = Square.Make(cf, cr);
                var target = _board[to];
                if (target.IsNone)
                {
                    moves.Add(new Move(sq, to));
                }
                else
                {
                    if (target.Color != us) moves.Add(new Move(sq, to, PieceType.None, MoveFlags.Capture));
                    break;
                }
                cf += df;
                cr += dr;
            }
        }
    }

    private void GenerateCastling(int kingSq, PieceColor us, PieceColor them, List<Move> moves)
    {
        bool white = us == PieceColor.White;
        int home = white ? 4 : 60;
        if (kingSq != home) return;

        var ks = white ? CastlingRights.WhiteKingSide : CastlingRights.BlackKingSide;
        var qs = white ? CastlingRights.WhiteQueenSide : CastlingRights.BlackQueenSide;
        var rook = new Piece(PieceType.Rook, us);

        if ((Castling & ks) != 0
            && _board[home + 3] == rook
            && _board[home + 1].IsNone && _board[home + 2].IsNone
            && !IsSquareAttacked(home, them) && !IsSquareAttacked(home + 1, them) && !IsSquareAttacked(home + 2, them))
        {
            moves.Add(new Move(home, home + 2, PieceType.None, MoveFlags.Castle));
        }

        if ((Castling & qs) != 0
            && _board[home - 4] == rook
            && _board[home - 1].IsNone && _board[home - 2].IsNone && _board[home - 3].IsNone
            && !IsSquareAttacked(home, them) && !IsSquareAttacked(home - 1, them) && !IsSquareAttacked(home - 2, them))
        {
            moves.Add(new Move(home, home - 2, PieceType.None, MoveFlags.Castle));
        }
    }

    #endregion

    #region Hamle yapma

    /// <summary>Hamleyi uygular ve yeni pozisyonu döndürür. Hamlenin yasal olduğu varsayılır.</summary>
    public Position MakeMove(Move m)
    {
        var p = Clone();
        var piece = p._board[m.From];
        var us = piece.Color;
        bool isCapture = !p._board[m.To].IsNone || m.IsEnPassant;

        p._board[m.From] = Piece.None;

        if (m.IsEnPassant)
        {
            int capturedSq = us == PieceColor.White ? m.To - 8 : m.To + 8;
            p._board[capturedSq] = Piece.None;
        }

        if (m.IsCastle)
        {
            bool kingSide = m.To > m.From;
            int rookFrom = kingSide ? m.From + 3 : m.From - 4;
            int rookTo = kingSide ? m.From + 1 : m.From - 1;
            p._board[rookTo] = p._board[rookFrom];
            p._board[rookFrom] = Piece.None;
        }

        p._board[m.To] = m.IsPromotion ? new Piece(m.Promotion, us) : piece;

        // Rok hakları
        if (piece.Type == PieceType.King)
        {
            p.Castling &= us == PieceColor.White
                ? ~(CastlingRights.WhiteKingSide | CastlingRights.WhiteQueenSide)
                : ~(CastlingRights.BlackKingSide | CastlingRights.BlackQueenSide);
        }
        p.Castling &= ~CastlingLossFor(m.From);
        p.Castling &= ~CastlingLossFor(m.To);

        p.EnPassantSquare = piece.Type == PieceType.Pawn && Math.Abs(m.To - m.From) == 16
            ? (m.From + m.To) / 2
            : Square.None;

        p.HalfmoveClock = piece.Type == PieceType.Pawn || isCapture ? 0 : HalfmoveClock + 1;
        if (us == PieceColor.Black) p.FullmoveNumber = FullmoveNumber + 1;
        p.SideToMove = us.Opposite();
        return p;
    }

    private static CastlingRights CastlingLossFor(int sq) => sq switch
    {
        0 => CastlingRights.WhiteQueenSide,
        7 => CastlingRights.WhiteKingSide,
        56 => CastlingRights.BlackQueenSide,
        63 => CastlingRights.BlackKingSide,
        _ => CastlingRights.None
    };

    /// <summary>UCI dizgesinden (ör. "e2e4") yasal hamleyi bulur.</summary>
    public Move? FindLegalMove(string uci)
    {
        if (uci is null || uci.Length < 4) return null;
        foreach (var m in GenerateLegalMoves())
            if (string.Equals(m.ToUci(), uci, StringComparison.OrdinalIgnoreCase)) return m;
        return null;
    }

    public Move? FindLegalMove(int from, int to, PieceType promotion = PieceType.None)
    {
        foreach (var m in GenerateLegalMoves())
            if (m.From == from && m.To == to && m.Promotion == promotion) return m;
        return null;
    }

    #endregion

    #region Oyun durumu

    public bool IsCheckmate => InCheck && GenerateLegalMoves().Count == 0;
    public bool IsStalemate => !InCheck && GenerateLegalMoves().Count == 0;

    /// <summary>Hiçbir tarafın mat edemeyeceği durumlar (K-K, K+F-K, K+A-K, aynı renk filler).</summary>
    public bool IsInsufficientMaterial
    {
        get
        {
            var minors = new List<(int sq, Piece piece)>();
            for (int sq = 0; sq < 64; sq++)
            {
                var piece = _board[sq];
                switch (piece.Type)
                {
                    case PieceType.None:
                    case PieceType.King:
                        continue;
                    case PieceType.Pawn:
                    case PieceType.Rook:
                    case PieceType.Queen:
                        return false;
                    default:
                        minors.Add((sq, piece));
                        break;
                }
            }
            if (minors.Count <= 1) return true;
            // Sadece filler ve hepsi aynı renk karede ise yetersiz
            if (minors.All(m => m.piece.Type == PieceType.Bishop))
            {
                int color = (Square.File(minors[0].sq) + Square.Rank(minors[0].sq)) & 1;
                return minors.All(m => ((Square.File(m.sq) + Square.Rank(m.sq)) & 1) == color);
            }
            return false;
        }
    }

    /// <summary>Bir tarafın toplam materyali (piyon = 1).</summary>
    public int Material(PieceColor color)
    {
        int total = 0;
        foreach (var piece in _board)
            if (!piece.IsNone && piece.Color == color) total += piece.Value;
        return total;
    }

    /// <summary><paramref name="color"/> açısından materyal farkı.</summary>
    public int MaterialBalance(PieceColor color) => Material(color) - Material(color.Opposite());

    #endregion

    public override string ToString() => ToFen();
}
