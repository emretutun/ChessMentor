using ChessMentor.Engine;

namespace ChessMentor.App.Services;

/// <summary>
/// İki ayrı Stockfish süreci yönetir:
/// <list type="bullet">
/// <item><b>Opponent</b>: sana karşı oynayan, seviyesi düşürülmüş bot.</item>
/// <item><b>Analyst</b>: tam güçte çalışan, mentorun kullandığı analiz motoru.</item>
/// </list>
/// Böylece rakip 1200 seviyesinde oynarken mentor 3000+ seviyesinde düşünür.
/// </summary>
public sealed class EngineHost : IAsyncDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);

    public UciEngine? Opponent { get; private set; }
    public UciEngine? Analyst { get; private set; }
    public string? EnginePath { get; private set; }
    public string? LastError { get; private set; }
    public bool IsReady => Opponent is not null && Analyst is not null;

    /// <summary>Motor yeniden başlatıldığında tetiklenir.</summary>
    public event Action? Restarted;

    public async Task<bool> EnsureStartedAsync(string? preferredPath)
    {
        await _gate.WaitAsync();
        try
        {
            return IsReady || await StartCoreAsync(preferredPath);
        }
        finally { _gate.Release(); }
    }

    public async Task<bool> RestartAsync(string? preferredPath)
    {
        bool ok;
        await _gate.WaitAsync();
        try
        {
            await StopCoreAsync();
            ok = await StartCoreAsync(preferredPath);
        }
        finally { _gate.Release(); }
        Restarted?.Invoke();
        return ok;
    }

    private async Task<bool> StartCoreAsync(string? preferredPath)
    {
        var path = EngineLocator.Find(preferredPath);
        if (path is null)
        {
            LastError = "Stockfish bulunamadı.";
            return false;
        }

        try
        {
            // Çekirdeklerin yarısından fazlasını kullanma; arayüze ve rakip motora yer kalsın.
            int threads = Math.Clamp(Environment.ProcessorCount / 2 - 1, 1, 4);
            Analyst = await UciEngine.StartAsync(path, threads, hashMb: 128);
            Opponent = await UciEngine.StartAsync(path, threads: 1, hashMb: 32);
            EnginePath = path;
            LastError = null;
            return true;
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            await StopCoreAsync();
            return false;
        }
    }

    private async Task StopCoreAsync()
    {
        if (Opponent is not null) await Opponent.DisposeAsync();
        if (Analyst is not null) await Analyst.DisposeAsync();
        Opponent = null;
        Analyst = null;
    }

    public async ValueTask DisposeAsync()
    {
        await _gate.WaitAsync();
        try { await StopCoreAsync(); }
        finally { _gate.Release(); }
    }
}
