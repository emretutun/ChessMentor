using ChessMentor.Core;
using ChessMentor.Engine;
using ChessMentor.Mentor;
using Xunit.Abstractions;

namespace ChessMentor.Tests;

/// <summary>
/// Gerçek Stockfish ile uçtan uca testler. engine/ klasöründe (ya da PATH'te) Stockfish yoksa
/// testler hiçbir şey yapmadan geçer.
/// </summary>
public class EngineIntegrationTests(ITestOutputHelper output)
{
    private static readonly string? EnginePath = EngineLocator.Find();

    [Fact]
    public async Task EngineReturnsMultiPvAnalysis()
    {
        if (EnginePath is null) return;
        await using var engine = await UciEngine.StartAsync(EnginePath);
        output.WriteLine($"Motor: {engine.Name}");

        var result = await engine.SearchAsync(Position.StartFen, [], SearchLimits.Time(400, multiPv: 3));
        foreach (var l in result.Lines) output.WriteLine($"{l.MultiPv}: {l.Score} d{l.Depth} {string.Join(' ', l.Pv.Take(5))}");

        Assert.Equal(3, result.Lines.Count);
        Assert.NotNull(Position.Start().FindLegalMove(result.BestMove));
    }

    [Fact]
    public async Task BotLevelsProduceLegalMoves()
    {
        if (EnginePath is null) return;
        await using var engine = await UciEngine.StartAsync(EnginePath);
        foreach (var level in new[] { BotLevel.All[0], BotLevel.All[3], BotLevel.All[^1] })
        {
            await engine.ApplyLevelAsync(level);
            var r = await engine.SearchAsync(Position.StartFen, ["e2e4", "e7e5"], new SearchLimits { Depth = level.Depth, MoveTimeMs = level.MoveTimeMs });
            output.WriteLine($"{level.Name}: {r.BestMove}");
            var game = new Game();
            game.TryMakeMove("e2e4");
            game.TryMakeMove("e7e5");
            Assert.NotNull(game.Current.FindLegalMove(r.BestMove));
        }
    }

    [Fact]
    public async Task MentorReviewsRealBlunderInTurkish()
    {
        if (EnginePath is null) return;
        await using var engine = await UciEngine.StartAsync(EnginePath);
        var mentor = new MentorService(engine, new MentorSettings { AnalysisTimeMs = 500 });

        // 1.e4 d5 ve beyaz 2.Vg4?? oynuyor: c8'deki fil veziri bedavaya alır.
        var game = new Game();
        game.TryMakeMove("e2e4");
        game.TryMakeMove("d7d5");

        var before = game.Current;
        var beforeAnalysis = await mentor.AnalyzeAsync(game);
        var blunder = before.FindLegalMove("d1g4")!.Value;
        Assert.True(game.TryMakeMove(blunder));
        var afterAnalysis = await mentor.AnalyzeAsync(game, multi: false);
        var review = mentor.ReviewMove(before, beforeAnalysis, blunder, game.Current, afterAnalysis);
        Log(review);

        Assert.Equal(MoveQuality.Blunder, review.Quality);
        Assert.Equal(MistakeMotif.HungPiece, review.Motif);
        Assert.Contains(review.Details, d => d.Contains("c8'deki filiyle g4'teki vezirini"));

        // Kolay okunur yazımla da aynı inceleme
        mentor.Settings.Notation = NotationLanguage.Easy;
        var easy = mentor.ReviewMove(before, beforeAnalysis, blunder, game.Current, afterAnalysis);
        Log(easy);
        Assert.Contains("Vezir d1 → g4", easy.Headline);
        mentor.Settings.Notation = NotationLanguage.Turkish;

        // Siyah için ipuçları: 1. seviye genel, 4. seviye hamlenin kendisi
        var blackTurn = await mentor.AnalyzeAsync(game);
        for (int level = 1; level <= Hint.MaxLevel; level++)
            output.WriteLine($"İpucu {level}: {HintService.GetHint(blackTurn, level, NotationLanguage.Turkish).Text}");
        Assert.Contains("Fxg4", HintService.GetHint(blackTurn, 4, NotationLanguage.Turkish).Text);
    }

    [Fact]
    public async Task FindsOpponentThreat()
    {
        if (EnginePath is null) return;
        await using var engine = await UciEngine.StartAsync(EnginePath);
        var mentor = new MentorService(engine, new MentorSettings { AnalysisTimeMs = 400 });

        // Çoban matı tehdidi: 1.e4 e5 2.Fc4 Ac6 3.Vh5 → siyah sırada, beyaz Vxf7# tehdit ediyor
        var game = new Game();
        foreach (var m in new[] { "e2e4", "e7e5", "f1c4", "b8c6", "d1h5" }) game.TryMakeMove(m);
        var analysis = await mentor.AnalyzeAsync(game);
        var threat = await mentor.FindThreatAsync(analysis);
        output.WriteLine(threat?.Description ?? "tehdit yok");

        Assert.NotNull(threat);
        Assert.True(threat.IsMateThreat);
        Assert.Equal("h5f7", threat.MoveUci);
    }

    private void Log(MoveReview r)
    {
        output.WriteLine(r.Headline);
        foreach (var d in r.Details) output.WriteLine("  " + d);
        foreach (var d in r.Better) output.WriteLine("  " + d);
        output.WriteLine($"  kazanma: {r.WinBefore:0}% → {r.WinAfter:0}%  motif: {r.Motif}");
    }
}
