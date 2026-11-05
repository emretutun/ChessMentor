using ChessMentor.Core;

namespace ChessMentor.Tests;

/// <summary>
/// Perft: belirli bir derinlikte tüm yasal hamle dizilerini sayar. Bilinen değerlerle karşılaştırmak,
/// hamle üretiminin (rok, geçerken alma, terfi, açmaz) doğru olduğunu kanıtlamanın standart yoludur.
/// </summary>
public class PerftTests
{
    private static long Perft(Position pos, int depth)
    {
        if (depth == 0) return 1;
        var moves = pos.GenerateLegalMoves();
        if (depth == 1) return moves.Count;
        long total = 0;
        foreach (var m in moves) total += Perft(pos.MakeMove(m), depth - 1);
        return total;
    }

    [Theory]
    [InlineData(1, 20)]
    [InlineData(2, 400)]
    [InlineData(3, 8902)]
    [InlineData(4, 197281)]
    public void StartPosition(int depth, long expected) =>
        Assert.Equal(expected, Perft(Position.Start(), depth));

    [Theory]
    [InlineData(1, 48)]
    [InlineData(2, 2039)]
    [InlineData(3, 97862)]
    public void Kiwipete(int depth, long expected) =>
        Assert.Equal(expected, Perft(Position.FromFen("r3k2r/p1ppqpb1/bn2pnp1/3PN3/1p2P3/2N2Q1p/PPPBBPPP/R3K2R w KQkq - 0 1"), depth));

    [Theory]
    [InlineData(1, 14)]
    [InlineData(2, 191)]
    [InlineData(3, 2812)]
    [InlineData(4, 43238)]
    public void Position3_EnPassantAndPins(int depth, long expected) =>
        Assert.Equal(expected, Perft(Position.FromFen("8/2p5/3p4/KP5r/1R3p1k/8/4P1P1/8 w - - 0 1"), depth));

    [Theory]
    [InlineData(1, 6)]
    [InlineData(2, 264)]
    [InlineData(3, 9467)]
    public void Position4_Promotions(int depth, long expected) =>
        Assert.Equal(expected, Perft(Position.FromFen("r3k2r/Pppp1ppp/1b3nbN/nP6/BBP1P3/q4N2/Pp1P2PP/R2Q1RK1 w kq - 0 1"), depth));
}
