using ChessMentor.Core;
using ChessMentor.Engine;

namespace ChessMentor.Mentor;

public sealed class MentorSettings
{
    /// <summary>Her pozisyon için analiz süresi (ms). Daha uzun = daha doğru ama daha yavaş.</summary>
    public int AnalysisTimeMs { get; set; } = 700;

    /// <summary>Rakip tehdidi araması için süre (ms).</summary>
    public int ThreatTimeMs { get; set; } = 250;

    /// <summary>Kaç alternatif hamle analiz edilsin (kritik an tespiti için en az 2).</summary>
    public int MultiPv { get; set; } = 3;

    public NotationLanguage Notation { get; set; } = NotationLanguage.Turkish;
}

/// <summary>
/// Mentorun beyni: pozisyonları tam güçteki analiz motoruyla inceler, hamleleri sınıflandırır,
/// açıklar ve rakibin tehditlerini bulur.
/// </summary>
public sealed class MentorService
{
    private readonly IChessEngine _analyst;
    private readonly IMoveExplainer _explainer;

    public MentorService(IChessEngine analyst, MentorSettings settings, IMoveExplainer? explainer = null)
    {
        _analyst = analyst;
        Settings = settings;
        _explainer = explainer ?? new RuleBasedExplainer();
    }

    public MentorSettings Settings { get; }

    public Task<PositionAnalysis> AnalyzeAsync(Game game, bool multi = true, CancellationToken ct = default) =>
        AnalyzeAsync(game.StartFen, game.UciMoves.ToList(), game.Current, multi, ct);

    /// <summary>Başlangıç FEN'i + hamle listesiyle tanımlanan pozisyonu analiz eder.</summary>
    public async Task<PositionAnalysis> AnalyzeAsync(string startFen, IReadOnlyList<string> moves, Position position, bool multi = true, CancellationToken ct = default)
    {
        var limits = SearchLimits.Time(Settings.AnalysisTimeMs, multi ? Math.Max(1, Settings.MultiPv) : 1);
        var result = await _analyst.SearchAsync(startFen, moves, limits, ct).ConfigureAwait(false);
        return new PositionAnalysis(position, result);
    }

    /// <summary>
    /// Oynanan hamleyi değerlendirir.
    /// </summary>
    /// <param name="before">Hamleden önceki pozisyon.</param>
    /// <param name="beforeAnalysis">Hamleden önceki pozisyonun analizi (MultiPV).</param>
    /// <param name="move">Oynanan hamle.</param>
    /// <param name="after">Hamleden sonraki pozisyon.</param>
    /// <param name="afterAnalysis">Hamleden sonraki pozisyonun analizi (oyun bittiyse null).</param>
    public MoveReview ReviewMove(Position before, PositionAnalysis beforeAnalysis, Move move, Position after, PositionAnalysis? afterAnalysis)
    {
        var uci = move.ToUci();
        var bestLine = beforeAnalysis.Best;
        bool isBest = string.Equals(bestLine?.FirstMove, uci, StringComparison.OrdinalIgnoreCase);

        double winBefore = beforeAnalysis.BestWin;
        double winAfter;

        var legalReplies = after.GenerateLegalMoves().Count;
        if (legalReplies == 0)
            winAfter = after.InCheck ? 100 : 50; // mat ya da pat
        else if (beforeAnalysis.LineFor(uci) is EngineLine played)
            winAfter = WinChance.FromScore(played.Score); // aynı aramadan: daha tutarlı
        else if (afterAnalysis is not null)
            winAfter = 100 - afterAnalysis.BestWin;
        else
            winAfter = winBefore;

        if (isBest) winAfter = Math.Max(winAfter, winBefore);

        double loss = Math.Max(0, winBefore - winAfter);
        var quality = MoveQualityInfo.Classify(loss, isBest, beforeAnalysis.IsCritical);

        var explanation = _explainer.Explain(new ExplainContext(before, move, after, beforeAnalysis, afterAnalysis, quality, Settings.Notation));

        string? bestSan = null, bestLineText = null;
        if (bestLine is not null)
        {
            var sans = Notation.UciLineToSan(before, bestLine.Pv, Settings.Notation, maxMoves: 6);
            bestSan = sans.FirstOrDefault();
            bestLineText = Notation.FormatLine(before, sans);
        }

        return new MoveReview
        {
            Ply = (before.FullmoveNumber - 1) * 2 + (before.SideToMove == PieceColor.White ? 0 : 1),
            MoveNumber = before.FullmoveNumber,
            Color = before.SideToMove,
            Move = move,
            San = Notation.ToSan(before, move, Settings.Notation),
            Quality = quality,
            WinBefore = winBefore,
            WinAfter = winAfter,
            BestMoveUci = bestLine?.FirstMove,
            BestSan = bestSan,
            BestLine = bestLineText,
            Headline = explanation.Headline,
            Details = explanation.Details,
            Better = explanation.Better,
            Motif = explanation.Motif
        };
    }

    /// <summary>
    /// "Rakip ne tehdit ediyor?" Sıra rakipte olsaydı (boş hamle) ne oynardı ve bu ne kazandırırdı?
    /// </summary>
    public async Task<ThreatInfo?> FindThreatAsync(PositionAnalysis current, CancellationToken ct = default)
    {
        var pos = current.Position;
        var nullPos = pos.NullMove();
        if (nullPos is null) return null;

        var result = await _analyst.SearchAsync(nullPos.ToFen(), [], SearchLimits.Time(Settings.ThreatTimeMs), ct).ConfigureAwait(false);
        var line = result.Best;
        if (line?.FirstMove is null || nullPos.FindLegalMove(line.FirstMove) is not Move threat) return null;

        var san = Notation.ToSan(nullPos, threat, Settings.Notation);

        if (line.Score.MateIn is int mate && mate > 0 && mate <= 3)
        {
            var text = mate == 1
                ? $"Rakip mat tehdit ediyor: {san}"
                : $"Rakip {mate} hamlelik bir mat tehdidi kuruyor. İlk hamlesi: {san}";
            return new ThreatInfo(line.FirstMove, san, text, true);
        }

        double threatWin = WinChance.FromScore(line.Score);
        double currentOpponentWin = 100 - current.BestWin;
        if (threatWin - currentOpponentWin < 15) return null;

        string description;
        var mover = nullPos[threat.From];
        if (threat.IsCapture && !nullPos[threat.To].IsNone)
            description = $"Rakip, {Turkish.At(threat.From)} {Turkish.PieceWith(mover.Type)} {Turkish.At(threat.To)} {Turkish.YourPieceAccusative(nullPos[threat.To].Type)} almakla tehdit ediyor. Önlem al!";
        else if (Tactics.IsFork(nullPos.MakeMove(threat), threat.To))
            description = $"Rakip çatal tehdidi yapıyor: {san}";
        else
            description = $"Rakibin tehdidi: {san}. Buna karşı önlem al.";

        return new ThreatInfo(line.FirstMove, san, description, false);
    }

    /// <summary>Rakibin (botun) oynadığı hamleyi kısaca yorumlar.</summary>
    public string DescribeOpponentMove(Position before, Move move)
    {
        var san = Notation.ToSan(before, move, Settings.Notation);
        var after = before.MakeMove(move);
        var piece = before[move.From];

        if (after.IsCheckmate) return $"🤖 Rakip: {san}. Mat oldun.";
        if (move.IsCapture)
        {
            int capSq = move.IsEnPassant ? (before.SideToMove == PieceColor.White ? move.To - 8 : move.To + 8) : move.To;
            return $"🤖 Rakip {Turkish.PieceWith(piece.Type)} {Turkish.At(capSq)} {Turkish.YourPieceAccusative(before[capSq].Type)} aldı.";
        }
        if (after.InCheck) return $"🤖 Rakip şah çekti: {san}";
        var fork = Tactics.ForkTargets(after, move.To);
        if (fork.Count >= 2) return $"🤖 Rakip: {san}. Dikkat, {Tactics.DescribeSquares(after, fork)} aynı anda tehdit altında!";
        return $"🤖 Rakip: {san}";
    }
}
