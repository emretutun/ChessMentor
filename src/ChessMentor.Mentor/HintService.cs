using ChessMentor.Core;

namespace ChessMentor.Mentor;

/// <summary>
/// Kademeli ipucu sistemi. Amaç cevabı hemen vermek değil, oyuncuyu doğru yöne itmek:
/// 1) Pozisyonun karakteri, 2) Hangi taş, 3) Fikir/motif, 4) Hamlenin kendisi.
/// </summary>
public static class HintService
{
    public static Hint GetHint(PositionAnalysis analysis, int level, NotationLanguage lang)
    {
        level = Math.Clamp(level, 1, Hint.MaxLevel);
        var pos = analysis.Position;
        var best = analysis.Best;
        if (best?.FirstMove is null || pos.FindLegalMove(best.FirstMove) is not Move move)
            return new Hint(level, "Bu pozisyon için henüz analiz yok.", [], null);

        var piece = pos[move.From];
        var after = pos.MakeMove(move);
        int swing = Tactics.MaterialSwing(pos, best.Pv);

        switch (level)
        {
            case 1:
                return new Hint(1, Character(analysis, swing), [], null);

            case 2:
                return new Hint(2, $"{Turkish.Capitalize(Turkish.PieceOn(pos, move.From))} ile oynamayı düşün.", [move.From], null);

            case 3:
                return new Hint(3, $"{Turkish.Capitalize(Turkish.PieceName(piece.Type))} {Turkish.To(move.To)} gidebilir. {Idea(pos, after, move, best.Score.MateIn, swing)}",
                    [move.From, move.To], null);

            default:
                var line = Notation.UciLineToSan(pos, best.Pv, lang, maxMoves: 5);
                return new Hint(4, $"En iyi hamle: {line.FirstOrDefault()}. Olası devam: {Notation.FormatLine(pos, line)}",
                    [move.From, move.To], move);
        }
    }

    private static string Character(PositionAnalysis analysis, int swing)
    {
        var pos = analysis.Position;
        var best = analysis.Best!;

        if (best.Score.MateIn is int m && m > 0)
            return m == 1 ? "Burada tek hamlede mat var! Şah çeken hamlelere bak." : $"Burada zorlayıcı bir mat var ({m} hamlede). Şah çeken hamlelerle başla.";
        if (swing >= 2)
            return "Taktik bir fırsat var! Zorlayıcı hamlelere bak: şah, alma ve tehdit.";
        if (Tactics.HangingPieces(pos, pos.SideToMove).Count > 0)
            return "Dikkat: tehlikede olan bir taşın var. Önce savunmayı düşün.";
        if (analysis.IsCritical)
            return "Kritik an! Burada sadece bir iyi hamle var, acele etme.";
        if (best.Score.MateIn is int mm && mm < 0)
            return "Zor durumdasın. En uzun direnişi sağlayan hamleyi ara.";
        return "Sakin bir pozisyon. En kötü duran taşını iyileştirmeyi ya da merkezi güçlendirmeyi düşün.";
    }

    private static string Idea(Position pos, Position after, Move move, int? mateIn, int swing)
    {
        if (mateIn is int m && m > 0) return "Bu hamle mat ağını kuruyor.";
        if (after.InCheck) return "Bu hamle şah çekiyor ve rakibi zorluyor.";
        if (Tactics.IsFork(after, move.To)) return "Bu hamle iki taşı birden tehdit ediyor (çatal).";
        if (move.IsCapture && swing >= 1) return "Bu hamle materyal kazandırıyor.";
        if (swing >= 2) return "Bu hamle bir taktiğin başlangıcı.";
        if (Tactics.IsHanging(pos, move.From) && !Tactics.IsHanging(after, move.To)) return "Bu hamle tehlikedeki taşını kurtarıyor.";
        if (move.IsCastle) return "Şahını güvene al.";
        return "Bu hamle konumunu iyileştiriyor.";
    }
}
