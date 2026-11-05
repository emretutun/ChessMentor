using System.Text;

namespace ChessMentor.Core;

public enum GameOutcome
{
    Ongoing,
    WhiteWins,
    BlackWins,
    Draw
}

public enum GameEndReason
{
    None,
    Checkmate,
    Stalemate,
    FiftyMoveRule,
    ThreefoldRepetition,
    InsufficientMaterial,
    Resignation
}

/// <summary>Bir oyunun hamle geçmişi, sonucu ve PGN dışa aktarımı.</summary>
public sealed class Game
{
    private readonly List<Position> _positions = [];
    private readonly List<Move> _moves = [];
    private GameOutcome _resignOutcome = GameOutcome.Ongoing;
    private readonly Dictionary<NotationLanguage, List<string>> _sanCache = [];
    private (int ply, GameOutcome outcome, GameEndReason reason)? _evaluation;

    public Game(string startFen = Position.StartFen)
    {
        StartFen = startFen;
        _positions.Add(Position.FromFen(startFen));
    }

    public string StartFen { get; }
    public Position Current => _positions[^1];
    public Position Start => _positions[0];
    public IReadOnlyList<Move> Moves => _moves;
    public IReadOnlyList<Position> Positions => _positions;
    public Move? LastMove => _moves.Count > 0 ? _moves[^1] : null;
    public int PlyCount => _moves.Count;

    public IEnumerable<string> UciMoves => _moves.Select(m => m.ToUci());

    public bool TryMakeMove(Move move)
    {
        if (IsOver) return false;
        var legal = Current.GenerateLegalMoves().FirstOrDefault(m => m.SameAs(move));
        if (legal == default) return false;
        _positions.Add(Current.MakeMove(legal));
        _moves.Add(legal);
        return true;
    }

    public bool TryMakeMove(string uci)
    {
        var m = Current.FindLegalMove(uci);
        return m is not null && TryMakeMove(m.Value);
    }

    public bool Undo()
    {
        if (_moves.Count == 0) return false;
        _moves.RemoveAt(_moves.Count - 1);
        _positions.RemoveAt(_positions.Count - 1);
        _resignOutcome = GameOutcome.Ongoing;
        _evaluation = null;
        foreach (var list in _sanCache.Values)
            if (list.Count > _moves.Count) list.RemoveRange(_moves.Count, list.Count - _moves.Count);
        return true;
    }

    public void Resign(PieceColor loser)
    {
        _resignOutcome = loser == PieceColor.White ? GameOutcome.BlackWins : GameOutcome.WhiteWins;
        _evaluation = null;
    }

    public bool IsOver => Outcome != GameOutcome.Ongoing;

    public GameOutcome Outcome => Evaluate().outcome;
    public GameEndReason EndReason => Evaluate().reason;

    private (GameOutcome outcome, GameEndReason reason) Evaluate()
    {
        if (_evaluation is { } e && e.ply == _moves.Count) return (e.outcome, e.reason);
        var result = EvaluateCore();
        _evaluation = (_moves.Count, result.outcome, result.reason);
        return result;
    }

    private (GameOutcome outcome, GameEndReason reason) EvaluateCore()
    {
        if (_resignOutcome != GameOutcome.Ongoing) return (_resignOutcome, GameEndReason.Resignation);

        var pos = Current;
        if (pos.GenerateLegalMoves().Count == 0)
        {
            if (pos.InCheck)
                return (pos.SideToMove == PieceColor.White ? GameOutcome.BlackWins : GameOutcome.WhiteWins, GameEndReason.Checkmate);
            return (GameOutcome.Draw, GameEndReason.Stalemate);
        }
        if (pos.IsInsufficientMaterial) return (GameOutcome.Draw, GameEndReason.InsufficientMaterial);
        if (pos.HalfmoveClock >= 100) return (GameOutcome.Draw, GameEndReason.FiftyMoveRule);

        var key = pos.RepetitionKey;
        if (_positions.Count(p => p.RepetitionKey == key) >= 3) return (GameOutcome.Draw, GameEndReason.ThreefoldRepetition);

        return (GameOutcome.Ongoing, GameEndReason.None);
    }

    /// <summary>Hamlelerin yazılışı. Her hamle bir kez hesaplanır ve saklanır.</summary>
    public IReadOnlyList<string> SanMoves(NotationLanguage lang = NotationLanguage.Turkish)
    {
        if (!_sanCache.TryGetValue(lang, out var list)) _sanCache[lang] = list = new List<string>(_moves.Count);
        for (int i = list.Count; i < _moves.Count; i++) list.Add(Notation.ToSan(_positions[i], _moves[i], lang));
        return list;
    }

    public string FormattedMoves(NotationLanguage lang = NotationLanguage.Turkish) =>
        Notation.FormatLine(Start, SanMoves(lang));

    public string ResultTag => Outcome switch
    {
        GameOutcome.WhiteWins => "1-0",
        GameOutcome.BlackWins => "0-1",
        GameOutcome.Draw => "1/2-1/2",
        _ => "*"
    };

    public string ToPgn(string white, string black, DateTime? date = null)
    {
        var d = date ?? DateTime.Now;
        var sb = new StringBuilder();
        sb.AppendLine("[Event \"ChessMentor\"]");
        sb.AppendLine("[Site \"ChessMentor\"]");
        sb.AppendLine($"[Date \"{d:yyyy.MM.dd}\"]");
        sb.AppendLine($"[White \"{white}\"]");
        sb.AppendLine($"[Black \"{black}\"]");
        sb.AppendLine($"[Result \"{ResultTag}\"]");
        if (StartFen != Position.StartFen)
        {
            sb.AppendLine("[SetUp \"1\"]");
            sb.AppendLine($"[FEN \"{StartFen}\"]");
        }
        sb.AppendLine();
        var moves = Notation.FormatLine(Start, SanMoves(NotationLanguage.English));
        sb.AppendLine(string.IsNullOrEmpty(moves) ? ResultTag : $"{moves} {ResultTag}");
        return sb.ToString();
    }
}
