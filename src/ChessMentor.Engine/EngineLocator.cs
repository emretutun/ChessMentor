namespace ChessMentor.Engine;

/// <summary>Stockfish çalıştırılabilir dosyasını bilinen yerlerde arar.</summary>
public static class EngineLocator
{
    private const string EngineFolder = "engine";

    /// <summary>
    /// Arama sırası:
    /// 1) Kullanıcının seçtiği yol,
    /// 2) Uygulama klasöründeki "engine" klasörü,
    /// 3) Üst klasörlerdeki "engine" klasörü (geliştirme sırasında depo kökündeki engine/),
    /// 4) PATH üzerindeki stockfish.
    /// </summary>
    public static string? Find(string? preferredPath = null)
    {
        if (!string.IsNullOrWhiteSpace(preferredPath) && File.Exists(preferredPath)) return preferredPath;

        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        for (int i = 0; i < 10 && dir is not null; i++, dir = dir.Parent)
        {
            var found = FindInFolder(Path.Combine(dir.FullName, EngineFolder));
            if (found is not null) return found;
        }

        var pathVar = Environment.GetEnvironmentVariable("PATH") ?? "";
        foreach (var p in pathVar.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            foreach (var name in (string[])["stockfish.exe", "stockfish"])
            {
                try
                {
                    var candidate = Path.Combine(p.Trim(), name);
                    if (File.Exists(candidate)) return candidate;
                }
                catch (ArgumentException) { /* geçersiz PATH girdisi */ }
            }
        }
        return null;
    }

    private static string? FindInFolder(string folder)
    {
        if (!Directory.Exists(folder)) return null;
        var pattern = OperatingSystem.IsWindows() ? "stockfish*.exe" : "stockfish*";
        return Directory.EnumerateFiles(folder, pattern, SearchOption.AllDirectories)
            .OrderBy(f => f.Length)
            .FirstOrDefault();
    }
}
