using System.Diagnostics;
using System.Globalization;
using System.Threading.Channels;

namespace ChessMentor.Engine;

/// <summary>
/// UCI protokolü üzerinden harici bir motor sürecini (ör. stockfish.exe) yönetir.
/// Komutlar stdin'e yazılır, cevaplar stdout'tan satır satır okunur.
/// Aynı anda yalnızca bir komut dizisi çalışır (iç kilit).
/// </summary>
public sealed class UciEngine : IChessEngine
{
    private static readonly TimeSpan HandshakeTimeout = TimeSpan.FromSeconds(10);

    private readonly Process _process;
    private readonly Channel<string> _output = Channel.CreateUnbounded<string>(new UnboundedChannelOptions { SingleReader = true });
    private readonly SemaphoreSlim _lock = new(1, 1);
    private int _multiPv = 1;
    private bool _disposed;

    public string Name { get; private set; } = "UCI motoru";
    public string ExecutablePath { get; }

    private UciEngine(string path)
    {
        ExecutablePath = path;
        _process = new Process
        {
            StartInfo = new ProcessStartInfo(path)
            {
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = Path.GetDirectoryName(path) ?? Environment.CurrentDirectory
            },
            EnableRaisingEvents = true
        };
        _process.OutputDataReceived += (_, e) =>
        {
            if (e.Data is null) _output.Writer.TryComplete();
            else _output.Writer.TryWrite(e.Data);
        };
        _process.ErrorDataReceived += (_, _) => { };
    }

    /// <summary>Motoru başlatır ve UCI el sıkışmasını yapar.</summary>
    public static async Task<UciEngine> StartAsync(string path, int threads = 1, int hashMb = 64, CancellationToken ct = default)
    {
        if (!File.Exists(path)) throw new FileNotFoundException("Motor dosyası bulunamadı.", path);

        var engine = new UciEngine(path);
        try
        {
            engine._process.Start();
            // Motor arayüzle yarışmasın: işlemciyi önce arayüz kullanır, kalan güç motora gider.
            try { engine._process.PriorityClass = ProcessPriorityClass.BelowNormal; } catch { /* bazı ortamlarda izin yok */ }
            engine._process.BeginOutputReadLine();
            engine._process.BeginErrorReadLine();

            engine.Send("uci");
            await engine.ReadUntilAsync(line =>
            {
                if (line.StartsWith("id name ", StringComparison.Ordinal)) engine.Name = line[8..].Trim();
                return line == "uciok";
            }, HandshakeTimeout, ct).ConfigureAwait(false);

            engine.Send($"setoption name Threads value {threads}");
            engine.Send($"setoption name Hash value {hashMb}");
            await engine.IsReadyAsync(ct).ConfigureAwait(false);
            return engine;
        }
        catch
        {
            await engine.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    public async Task NewGameAsync(CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            Send("ucinewgame");
            await IsReadyAsync(ct).ConfigureAwait(false);
        }
        finally { _lock.Release(); }
    }

    public async Task SetOptionAsync(string name, string? value = null, CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            Send(value is null ? $"setoption name {name}" : $"setoption name {name} value {value}");
            if (name.Equals("MultiPV", StringComparison.OrdinalIgnoreCase) && int.TryParse(value, out var mpv)) _multiPv = mpv;
            await IsReadyAsync(ct).ConfigureAwait(false);
        }
        finally { _lock.Release(); }
    }

    public async Task ApplyLevelAsync(BotLevel level, CancellationToken ct = default)
    {
        if (level.UciElo is int elo)
        {
            await SetOptionAsync("Skill Level", "20", ct).ConfigureAwait(false);
            await SetOptionAsync("UCI_LimitStrength", "true", ct).ConfigureAwait(false);
            await SetOptionAsync("UCI_Elo", elo.ToString(CultureInfo.InvariantCulture), ct).ConfigureAwait(false);
        }
        else
        {
            await SetOptionAsync("UCI_LimitStrength", "false", ct).ConfigureAwait(false);
            await SetOptionAsync("Skill Level", (level.SkillLevel ?? 20).ToString(CultureInfo.InvariantCulture), ct).ConfigureAwait(false);
        }
    }

    public async Task<SearchResult> SearchAsync(string startFen, IEnumerable<string> moves, SearchLimits limits, CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        await _lock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (limits.MultiPv != _multiPv)
            {
                Send($"setoption name MultiPV value {limits.MultiPv}");
                _multiPv = limits.MultiPv;
                await IsReadyAsync(ct).ConfigureAwait(false);
            }

            var moveList = string.Join(' ', moves);
            Send(moveList.Length == 0
                ? $"position fen {startFen}"
                : $"position fen {startFen} moves {moveList}");

            var go = "go";
            if (limits.Depth is int d) go += $" depth {d}";
            if (limits.MoveTimeMs is int t) go += $" movetime {t}";
            if (limits.Nodes is long n) go += $" nodes {n}";
            if (go == "go") go += " depth 12";
            Send(go);

            var lines = new Dictionary<int, EngineLine>();
            string bestMove = "(none)";
            string? ponder = null;

            // İptal edilirse motora "stop" gönderilir; motor yine de "bestmove" yazar, onu bekleriz.
            using var reg = ct.Register(() => { try { Send("stop"); } catch { /* süreç kapanmış olabilir */ } });

            var timeout = TimeSpan.FromMilliseconds((limits.MoveTimeMs ?? 30_000) + 30_000);
            await ReadUntilAsync(line =>
            {
                if (line.StartsWith("info ", StringComparison.Ordinal))
                {
                    var parsed = ParseInfo(line);
                    if (parsed is not null) lines[parsed.MultiPv] = parsed;
                    return false;
                }
                if (line.StartsWith("bestmove", StringComparison.Ordinal))
                {
                    var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length > 1) bestMove = parts[1];
                    if (parts.Length > 3 && parts[2] == "ponder") ponder = parts[3];
                    return true;
                }
                return false;
            }, timeout, CancellationToken.None).ConfigureAwait(false);

            var ordered = lines.Values.OrderBy(l => l.MultiPv).ToList();
            return new SearchResult(bestMove, ponder, ordered);
        }
        finally { _lock.Release(); }
    }

    /// <summary>"info depth 18 multipv 1 score cp 32 ... pv e2e4 e7e5" satırını çözümler.</summary>
    public static EngineLine? ParseInfo(string line)
    {
        var t = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        int depth = 0, multiPv = 1;
        Score? score = null;
        List<string>? pv = null;

        for (int i = 1; i < t.Length; i++)
        {
            switch (t[i])
            {
                case "depth" when i + 1 < t.Length:
                    int.TryParse(t[++i], out depth);
                    break;
                case "multipv" when i + 1 < t.Length:
                    int.TryParse(t[++i], out multiPv);
                    break;
                case "score" when i + 2 < t.Length:
                    var kind = t[++i];
                    if (!int.TryParse(t[++i], out var value)) return null;
                    score = kind == "mate" ? Score.Mate(value) : Score.Cp(value);
                    // Sınır puanları (aramanın ortasındaki kesin olmayan değerler) atlanır.
                    if (i + 1 < t.Length && (t[i + 1] == "lowerbound" || t[i + 1] == "upperbound")) return null;
                    break;
                case "pv":
                    pv = t[(i + 1)..].ToList();
                    i = t.Length;
                    break;
                case "string":
                    return null;
            }
        }

        if (score is null || pv is null || pv.Count == 0) return null;
        return new EngineLine(multiPv, depth, score.Value, pv);
    }

    private Task IsReadyAsync(CancellationToken ct)
    {
        Send("isready");
        return ReadUntilAsync(l => l == "readyok", HandshakeTimeout, ct);
    }

    private void Send(string command)
    {
        if (_process.HasExited) throw new InvalidOperationException("Motor süreci beklenmedik şekilde kapandı.");
        _process.StandardInput.WriteLine(command);
        _process.StandardInput.Flush();
    }

    private async Task ReadUntilAsync(Func<string, bool> stop, TimeSpan timeout, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeout);
        try
        {
            while (await _output.Reader.WaitToReadAsync(cts.Token).ConfigureAwait(false))
            {
                while (_output.Reader.TryRead(out var line))
                    if (stop(line)) return;
            }
            throw new InvalidOperationException("Motor çıktısı beklenmedik şekilde sona erdi.");
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new TimeoutException("Motor zamanında cevap vermedi.");
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        try
        {
            if (!_process.HasExited)
            {
                _process.StandardInput.WriteLine("quit");
                _process.StandardInput.Flush();
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                try { await _process.WaitForExitAsync(cts.Token).ConfigureAwait(false); }
                catch (OperationCanceledException) { _process.Kill(entireProcessTree: true); }
            }
        }
        catch { /* kapanırken oluşan hatalar önemsiz */ }
        finally
        {
            _process.Dispose();
            _lock.Dispose();
        }
    }
}
