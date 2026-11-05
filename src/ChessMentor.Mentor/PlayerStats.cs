using System.Text.Json;
using System.Text.Json.Serialization;

namespace ChessMentor.Mentor;

/// <summary>
/// Oyuncunun uzun vadeli istatistikleri. Yerel bir JSON dosyasında saklanır (hiçbir yere gönderilmez).
/// Hangi hata türünü sık yaptığını takip ederek kişisel tavsiye üretir.
/// </summary>
public sealed class PlayerStats
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public int GamesPlayed { get; set; }
    public int Wins { get; set; }
    public int Losses { get; set; }
    public int Draws { get; set; }
    public int MovesReviewed { get; set; }

    /// <summary>Son oyunların doğruluk değerleri (en fazla 50).</summary>
    public List<double> RecentAccuracy { get; set; } = [];

    public Dictionary<MistakeMotif, int> MotifCounts { get; set; } = [];
    public Dictionary<MoveQuality, int> QualityCounts { get; set; } = [];

    public double AverageAccuracy => RecentAccuracy.Count == 0 ? 0 : RecentAccuracy.Average();

    public IEnumerable<KeyValuePair<MistakeMotif, int>> TopMotifs(int count = 3) =>
        MotifCounts.Where(kv => kv.Key != MistakeMotif.None && kv.Value > 0)
                   .OrderByDescending(kv => kv.Value)
                   .Take(count);

    public void RecordGame(IReadOnlyList<MoveReview> reviews, GameReport report, bool? userWon)
    {
        GamesPlayed++;
        if (userWon == true) Wins++;
        else if (userWon == false) Losses++;
        else Draws++;

        if (reviews.Count > 0)
        {
            RecentAccuracy.Add(Math.Round(report.Accuracy, 1));
            if (RecentAccuracy.Count > 50) RecentAccuracy.RemoveAt(0);
        }

        foreach (var r in reviews)
        {
            MovesReviewed++;
            QualityCounts[r.Quality] = QualityCounts.GetValueOrDefault(r.Quality) + 1;
            if (r.Quality.IsError() && r.Motif != MistakeMotif.None)
                MotifCounts[r.Motif] = MotifCounts.GetValueOrDefault(r.Motif) + 1;
        }
    }

    public static PlayerStats Load(string path)
    {
        try
        {
            if (File.Exists(path))
                return JsonSerializer.Deserialize<PlayerStats>(File.ReadAllText(path), JsonOptions) ?? new PlayerStats();
        }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
            // Bozuk dosya: sıfırdan başla
        }
        return new PlayerStats();
    }

    public void Save(string path)
    {
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        File.WriteAllText(path, JsonSerializer.Serialize(this, JsonOptions));
    }
}
