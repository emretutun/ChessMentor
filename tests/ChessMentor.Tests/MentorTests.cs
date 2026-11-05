using ChessMentor.Core;
using ChessMentor.Engine;
using ChessMentor.Mentor;

namespace ChessMentor.Tests;

public class NotationTests
{
    [Fact]
    public void TurkishSanUsesTurkishLetters()
    {
        var pos = Position.Start();
        var move = pos.FindLegalMove("g1f3")!.Value;
        Assert.Equal("Af3", Notation.ToSan(pos, move, NotationLanguage.Turkish));
        Assert.Equal("Nf3", Notation.ToSan(pos, move, NotationLanguage.English));
    }

    [Fact]
    public void SanMarksMate()
    {
        // Çoban matı: 1.e4 e5 2.Fc4 Ac6 3.Vh5 Af6 4.Vxf7#
        var game = new Game();
        foreach (var m in new[] { "e2e4", "e7e5", "f1c4", "b8c6", "d1h5", "g8f6" }) Assert.True(game.TryMakeMove(m));
        var mate = game.Current.FindLegalMove("h5f7")!.Value;
        Assert.Equal("Vxf7#", Notation.ToSan(game.Current, mate));
        game.TryMakeMove(mate);
        Assert.Equal(GameOutcome.WhiteWins, game.Outcome);
        Assert.Equal(GameEndReason.Checkmate, game.EndReason);
    }

    [Fact]
    public void EasyNotationIsReadable()
    {
        var pos = Position.Start();
        Assert.Equal("At g1 → f3", Notation.ToSan(pos, pos.FindLegalMove("g1f3")!.Value, NotationLanguage.Easy));

        var castle = Position.FromFen("4k3/8/8/8/8/8/8/4K2R w K - 0 1");
        Assert.Equal("Kısa rok", Notation.ToSan(castle, castle.FindLegalMove("e1g1")!.Value, NotationLanguage.Easy));

        var capture = Position.FromFen("4k3/8/8/3q4/4P3/8/8/4K3 w - - 0 1");
        Assert.Equal("Piyon e4 → d5 (alır)", Notation.ToSan(capture, capture.FindLegalMove("e4d5")!.Value, NotationLanguage.Easy));
    }

    [Fact]
    public void SanDisambiguates()
    {
        var pos = Position.FromFen("4k3/8/8/8/8/8/4K3/R6R w - - 0 1");
        var move = pos.FindLegalMove("a1d1")!.Value;
        Assert.Equal("Rad1", Notation.ToSan(pos, move, NotationLanguage.English));
    }

    [Theory]
    [InlineData("e5", "e5'teki", "e5'e")]
    [InlineData("a1", "a1'deki", "a1'e")]
    [InlineData("h6", "h6'daki", "h6'ya")]
    [InlineData("c2", "c2'deki", "c2'ye")]
    public void TurkishSuffixes(string square, string at, string to)
    {
        int sq = Square.Parse(square);
        Assert.Equal(at, Turkish.At(sq));
        Assert.Equal(to, Turkish.To(sq));
    }

    [Fact]
    public void PgnUsesEnglishNotation()
    {
        var game = new Game();
        game.TryMakeMove("g1f3");
        Assert.Contains("1. Nf3", game.ToPgn("Sen", "Stockfish"));
    }

    [Fact]
    public void FenRoundTrip()
    {
        const string fen = "r3k2r/p1ppqpb1/bn2pnp1/3PN3/1p2P3/2N2Q1p/PPPBBPPP/R3K2R w KQkq - 0 1";
        Assert.Equal(fen, Position.FromFen(fen).ToFen());
    }
}

public class EngineParsingTests
{
    [Fact]
    public void ParsesInfoLine()
    {
        var line = UciEngine.ParseInfo("info depth 18 seldepth 24 multipv 2 score cp -35 nodes 123 nps 1000 pv e7e5 g1f3 b8c6");
        Assert.NotNull(line);
        Assert.Equal(2, line.MultiPv);
        Assert.Equal(18, line.Depth);
        Assert.Equal(-35, line.Score.Centipawns);
        Assert.Equal(["e7e5", "g1f3", "b8c6"], line.Pv);
    }

    [Fact]
    public void ParsesMateAndSkipsBounds()
    {
        Assert.Equal(3, UciEngine.ParseInfo("info depth 10 score mate 3 pv d1h5")!.Score.MateIn);
        Assert.Null(UciEngine.ParseInfo("info depth 10 score cp 20 lowerbound pv e2e4"));
        Assert.Null(UciEngine.ParseInfo("info string NNUE evaluation enabled"));
    }
}

public class MentorLogicTests
{
    [Fact]
    public void WinChanceIsSymmetric()
    {
        Assert.Equal(50, WinChance.FromCentipawns(0), 3);
        Assert.Equal(100 - WinChance.FromCentipawns(300), WinChance.FromCentipawns(-300), 3);
        Assert.Equal(100, WinChance.FromScore(Score.Mate(2)));
    }

    [Theory]
    [InlineData(0.5, false, MoveQuality.Excellent)]
    [InlineData(3, false, MoveQuality.Good)]
    [InlineData(7, false, MoveQuality.Inaccuracy)]
    [InlineData(15, false, MoveQuality.Mistake)]
    [InlineData(40, false, MoveQuality.Blunder)]
    [InlineData(0, true, MoveQuality.Best)]
    public void ClassifiesByWinLoss(double loss, bool best, MoveQuality expected) =>
        Assert.Equal(expected, MoveQualityInfo.Classify(loss, best, wasCritical: false));

    [Fact]
    public void DetectsKnightFork()
    {
        // Beyaz at c7'de: a8'deki kale ve e8'deki şah çatalda
        var pos = Position.FromFen("r3k3/2N5/8/8/8/8/8/4K3 b - - 0 1");
        var targets = Tactics.ForkTargets(pos, Square.Parse("c7"));
        Assert.Contains(Square.Parse("a8"), targets);
        Assert.Contains(Square.Parse("e8"), targets);
    }

    [Fact]
    public void DetectsHangingPiece()
    {
        // Siyah vezir d5'te, beyaz piyon e4 saldırıyor
        var pos = Position.FromFen("4k3/8/8/3q4/4P3/8/8/4K3 b - - 0 1");
        Assert.Contains(Square.Parse("d5"), Tactics.HangingPieces(pos, PieceColor.Black));
    }

    [Fact]
    public void DetectsPin()
    {
        // Beyaz fil b5, siyah at c6, siyah şah e8: açmaz
        var pos = Position.FromFen("4k3/8/2n5/1B6/8/8/8/4K3 w - - 0 1");
        var pin = Assert.Single(Tactics.FindPins(pos, PieceColor.White));
        Assert.Equal(Square.Parse("c6"), pin.Pinned);
    }

    [Fact]
    public void ExplainsMissedFreeQueen()
    {
        // Beyaz e4xd5 ile bedava vezir alabilirken a3 oynuyor.
        var before = Position.FromFen("4k3/8/8/3q4/4P3/8/P7/4K3 w - - 0 1");
        var bestLines = new List<EngineLine>
        {
            new(1, 20, Score.Cp(900), ["e4d5", "e8d7"]),
            new(2, 20, Score.Cp(-50), ["a2a3", "d5e4"])
        };
        var beforeAnalysis = new PositionAnalysis(before, new SearchResult("e4d5", null, bestLines));
        var move = before.FindLegalMove("a2a3")!.Value;
        var after = before.MakeMove(move);
        var afterAnalysis = new PositionAnalysis(after, new SearchResult("d5e4", null, [new(1, 20, Score.Cp(800), ["d5e4", "e1f2"])]));

        var mentor = new MentorService(new NullEngine(), new MentorSettings());
        var review = mentor.ReviewMove(before, beforeAnalysis, move, after, afterAnalysis);

        Assert.Equal(MoveQuality.Blunder, review.Quality);
        Assert.Equal(MistakeMotif.MissedCapture, review.Motif);
        Assert.Contains(review.Better, s => s.Contains("exd5"));
        Assert.Contains(review.Better, s => s.Contains("d5'teki veziri"));
    }

    private sealed class NullEngine : IChessEngine
    {
        public string Name => "null";
        public Task NewGameAsync(CancellationToken ct = default) => Task.CompletedTask;
        public Task SetOptionAsync(string name, string? value = null, CancellationToken ct = default) => Task.CompletedTask;
        public Task<SearchResult> SearchAsync(string startFen, IEnumerable<string> moves, SearchLimits limits, CancellationToken ct = default) =>
            Task.FromResult(new SearchResult("(none)", null, []));
        public Task ApplyLevelAsync(BotLevel level, CancellationToken ct = default) => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
