using ChessMentor.Core;
using ChessMentor.Engine;

namespace ChessMentor.Mentor;

/// <summary>Açıklama üretmek için gereken her şey.</summary>
public sealed record ExplainContext(
    Position Before,
    Move Move,
    Position After,
    PositionAnalysis BeforeAnalysis,
    PositionAnalysis? AfterAnalysis,
    MoveQuality Quality,
    NotationLanguage Language);

/// <param name="Details">Hamle hakkında yorum (hata ise: ne ters gitti).</param>
/// <param name="Better">Daha iyi hamle ve nedeni (sadece hatalarda). "Hatada dur" açıkken geç gösterilir.</param>
public sealed record Explanation(string Headline, IReadOnlyList<string> Details, IReadOnlyList<string> Better, MistakeMotif Motif);

/// <summary>
/// Hamle açıklayıcısı. Varsayılan <see cref="RuleBasedExplainer"/> tamamen yereldir ve ücretsizdir.
/// İleride (ör. Ollama ile) yerel bir dil modeli bu arayüzün arkasına eklenebilir.
/// </summary>
public interface IMoveExplainer
{
    Explanation Explain(ExplainContext ctx);
}

/// <summary>
/// Kural tabanlı Türkçe açıklamalar. Motorun bulduğu varyantları basit taktik tespitleriyle
/// (korumasız taş, çatal, açmaz, mat, materyal kazancı) sade bir dille anlatır.
/// </summary>
public sealed class RuleBasedExplainer : IMoveExplainer
{
    public Explanation Explain(ExplainContext ctx)
    {
        var moveText = Notation.ToSan(ctx.Before, ctx.Move, ctx.Language);
        var symbol = ctx.Language == NotationLanguage.Easy ? "" : ctx.Quality.Symbol();
        var headline = $"{ctx.Quality.Icon()} {ctx.Quality.Label()}: {moveText}{symbol}";
        var details = new List<string>();
        var better = new List<string>();
        var motif = MistakeMotif.None;

        if (!ctx.Quality.IsError())
        {
            DescribeGoodMove(ctx, details);
        }
        else
        {
            motif = DescribeProblem(ctx, details);
            var missed = DescribeBetterMove(ctx, better);
            // Kaçırılan fırsat daha öğreticidir; ama mata izin vermek ve taş bırakmak önceliklidir.
            if (missed != MistakeMotif.None && motif is not (MistakeMotif.AllowedMate or MistakeMotif.HungPiece))
                motif = missed;
            if (motif == MistakeMotif.None) motif = MistakeMotif.Positional;
        }

        return new Explanation(headline, details, better, motif);
    }

    private static int CapturedSquare(Position pos, Move m) =>
        m.IsEnPassant ? (pos.SideToMove == PieceColor.White ? m.To - 8 : m.To + 8) : m.To;

    private static void DescribeGoodMove(ExplainContext ctx, List<string> details)
    {
        var before = ctx.Before;
        var after = ctx.After;
        var move = ctx.Move;

        if (after.IsCheckmate)
        {
            details.Add("Mat! Oyunu kazandın. 🎉");
            return;
        }

        if (ctx.Quality == MoveQuality.Great)
            details.Add("Kritik anda tek iyi hamleyi buldun! 👏");

        if (move.IsCapture)
        {
            int capSq = CapturedSquare(before, move);
            details.Add($"{Turkish.At(capSq)} {Turkish.PieceAccusative(before[capSq].Type)} aldın.");
        }

        var fork = Tactics.ForkTargets(after, move.To);
        if (fork.Count >= 2 && !Tactics.IsHanging(after, move.To))
            details.Add($"Çatal! Aynı anda {Tactics.DescribeSquares(after, fork)} tehdit ediliyor.");
        else if (after.InCheck)
            details.Add("Şah çektin, rakip cevap vermek zorunda.");

        if (move.IsCastle)
            details.Add("Rok yaptın: şahın güvende, kalen oyuna giriyor.");

        var mateLine = ctx.BeforeAnalysis.Best;
        if (mateLine?.Score.MateIn is int m && m > 1 && ctx.Quality <= MoveQuality.Best)
            details.Add($"Mat ağı kuruluyor: {m} hamlede mat var.");

        if (details.Count == 0)
        {
            details.Add(ctx.Quality switch
            {
                MoveQuality.Best => "Bilgisayarın da ilk tercihi bu hamle.",
                MoveQuality.Excellent => "Çok iyi bir hamle, en iyiye çok yakın.",
                _ => "Sağlam bir hamle."
            });
        }

        var best = ctx.BeforeAnalysis.Best;
        if (ctx.Quality is MoveQuality.Excellent or MoveQuality.Good && best?.FirstMove is string bestUci)
        {
            var bestText = Notation.UciLineToSan(before, [bestUci], ctx.Language).FirstOrDefault();
            if (bestText is not null) details.Add($"Biraz daha iyisi: {bestText}");
        }
    }

    /// <summary>Hamleden sonra ne ters gitti? Rakibin en iyi cevabına bakar.</summary>
    private static MistakeMotif DescribeProblem(ExplainContext ctx, List<string> details)
    {
        var after = ctx.After;
        var me = ctx.Before.SideToMove;
        var refutation = ctx.AfterAnalysis?.Best;
        if (refutation is null || refutation.Pv.Count == 0) return MistakeMotif.None;

        var line = Notation.UciLineToSan(after, refutation.Pv, ctx.Language, maxMoves: 5);
        var lineText = Notation.FormatLine(after, line);

        // Rakip mat edebiliyor mu?
        if (refutation.Score.MateIn is int mate && mate > 0)
        {
            details.Add(mate == 1
                ? $"Bu hamleden sonra rakip tek hamlede mat ediyor: {line.FirstOrDefault()}"
                : $"Bu hamleden sonra rakip {mate} hamlede mat edebilir: {lineText}");
            return MistakeMotif.AllowedMate;
        }

        var reply = after.FindLegalMove(refutation.Pv[0]);
        if (reply is null) return MistakeMotif.None;
        var replyText = line.FirstOrDefault() ?? refutation.Pv[0];
        int swing = Tactics.MaterialSwing(after, refutation.Pv); // rakip açısından

        // Rakip bir taşımızı alıyor mu?
        if (reply.Value.IsCapture && swing >= 1)
        {
            int capSq = CapturedSquare(after, reply.Value);
            var captured = after[capSq];
            var attacker = after[reply.Value.From];
            bool capturedHanging = Tactics.IsHanging(after, capSq);
            // Bu hamleyle mi tehlikeye girdi, yoksa zaten tehlikede miydi?
            bool newlyHanging = capturedHanging && (capSq == ctx.Move.To || !Tactics.IsHanging(ctx.Before, capSq));

            details.Add($"Rakip {Turkish.At(reply.Value.From)} {Turkish.PieceWith(attacker.Type)} {Turkish.At(capSq)} {Turkish.YourPieceAccusative(captured.Type)} alabilir.");
            if (newlyHanging && capSq == ctx.Move.To)
                details.Add($"Oynadığın {Turkish.PieceName(captured.Type)} {Turkish.On(capSq)} korumasız kaldı.");
            else if (newlyHanging)
                details.Add($"Bu hamleyle {Turkish.PieceName(captured.Type)} korumasız kaldı.");
            else if (capturedHanging)
                details.Add($"{Turkish.Capitalize(Turkish.PieceName(captured.Type))} zaten tehlikedeydi ve önlem almadın.");
            return newlyHanging ? MistakeMotif.HungPiece : MistakeMotif.AllowedTactic;
        }

        // Rakip çatal yapabiliyor mu?
        var afterReply = after.MakeMove(reply.Value);
        var forkTargets = Tactics.ForkTargets(afterReply, reply.Value.To);
        if (forkTargets.Count >= 2 && swing >= 1)
        {
            details.Add($"Rakip {replyText} ile çatal yapabilir: {Tactics.DescribeSquares(afterReply, forkTargets)} aynı anda tehdit altında kalır.");
            return MistakeMotif.AllowedFork;
        }

        // Yeni oluşan korumasız taşlar
        var hangingBefore = Tactics.HangingPieces(ctx.Before, me).ToHashSet();
        var hangingNow = Tactics.HangingPieces(after, me).Where(s => !hangingBefore.Contains(s) || s == ctx.Move.To).ToList();
        if (hangingNow.Count > 0 && swing >= 1)
        {
            details.Add($"{Turkish.Capitalize(Tactics.DescribeSquares(after, hangingNow.Take(2)))} tehlikede kaldı.");
            details.Add($"Rakibin en iyi cevabı: {lineText}");
            return MistakeMotif.HungPiece;
        }

        if (swing >= 2)
        {
            details.Add($"Rakip şu devamla yaklaşık {swing} puanlık taş kazanır: {lineText}");
            return MistakeMotif.AllowedTactic;
        }

        double winAfter = 100 - WinChance.FromScore(refutation.Score);
        details.Add($"Pozisyonun zayıfladı. Kazanma şansın: %{ctx.BeforeAnalysis.BestWin:0} → %{winAfter:0}");
        if (ctx.Quality >= MoveQuality.Mistake) details.Add($"Rakibin en iyi cevabı: {lineText}");
        return MistakeMotif.None;
    }

    /// <summary>Daha iyi hamle neydi ve neden?</summary>
    private static MistakeMotif DescribeBetterMove(ExplainContext ctx, List<string> better)
    {
        var before = ctx.Before;
        var best = ctx.BeforeAnalysis.Best;
        if (best?.FirstMove is null) return MistakeMotif.None;

        var bestMove = before.FindLegalMove(best.FirstMove);
        if (bestMove is null) return MistakeMotif.None;

        var line = Notation.UciLineToSan(before, best.Pv, ctx.Language, maxMoves: 5);
        var bestText = line.FirstOrDefault() ?? best.FirstMove;
        var lineText = Notation.FormatLine(before, line);
        better.Add($"💡 Daha iyisi: {bestText}");

        if (best.Score.MateIn is int mate && mate > 0)
        {
            better.Add(mate == 1 ? "Bu hamle doğrudan mat ediyordu!" : $"Buradan {mate} hamlede mat vardı: {lineText}");
            return MistakeMotif.MissedMate;
        }

        var afterBest = before.MakeMove(bestMove.Value);
        int swing = Tactics.MaterialSwing(before, best.Pv);

        if (bestMove.Value.IsCapture && swing >= 1)
        {
            int capSq = CapturedSquare(before, bestMove.Value);
            var captured = before[capSq];
            if (!captured.IsNone)
            {
                better.Add($"Böylece {Turkish.At(capSq)} {Turkish.PieceAccusative(captured.Type)} kazanıyordun.");
                return MistakeMotif.MissedCapture;
            }
        }

        var fork = Tactics.ForkTargets(afterBest, bestMove.Value.To);
        if (fork.Count >= 2 && swing >= 1)
        {
            better.Add($"Bu bir çataldı: {Tactics.DescribeSquares(afterBest, fork)} aynı anda tehdit altında kalıyordu.");
            return MistakeMotif.MissedFork;
        }

        var pinsBefore = Tactics.FindPins(before, before.SideToMove).Select(p => p.Pinned).ToHashSet();
        var newPin = Tactics.FindPins(afterBest, before.SideToMove)
            .FirstOrDefault(p => p.Pinner == bestMove.Value.To && !pinsBefore.Contains(p.Pinned));
        if (newPin != default && swing >= 1)
        {
            better.Add($"Böylece {Turkish.PieceOn(afterBest, newPin.Pinned)} açmaza giriyordu (arkasında {Turkish.PieceName(afterBest[newPin.Behind].Type)} var, kıpırdayamaz).");
            return MistakeMotif.MissedPin;
        }

        if (swing >= 2)
        {
            better.Add($"Bu devam yaklaşık {swing} puanlık taş kazandırıyordu: {lineText}");
            return MistakeMotif.MissedTactic;
        }

        if (line.Count > 1 && ctx.Quality >= MoveQuality.Mistake) better.Add($"Olası devam: {lineText}");
        return MistakeMotif.None;
    }
}
