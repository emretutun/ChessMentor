namespace ChessMentor.Engine;

/// <summary>
/// Satranç motoru soyutlaması. Windows'ta <see cref="UciEngine"/> bir Stockfish süreci çalıştırır;
/// ileride Android vb. için başka uygulamalar eklenebilir, uygulamanın geri kalanı farkı bilmez.
/// </summary>
public interface IChessEngine : IAsyncDisposable
{
    string Name { get; }

    Task NewGameAsync(CancellationToken ct = default);

    Task SetOptionAsync(string name, string? value = null, CancellationToken ct = default);

    /// <summary>
    /// Pozisyonu arar. Pozisyon başlangıç FEN'i + UCI hamle listesi olarak verilir
    /// (motor böylece tekrar kuralını da bilir).
    /// </summary>
    Task<SearchResult> SearchAsync(string startFen, IEnumerable<string> moves, SearchLimits limits, CancellationToken ct = default);

    /// <summary>Botun gücünü ayarlar.</summary>
    Task ApplyLevelAsync(BotLevel level, CancellationToken ct = default);
}
