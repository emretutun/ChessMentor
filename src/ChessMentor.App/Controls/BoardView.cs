using ChessMentor.Core;
using IImage = Microsoft.Maui.Graphics.IImage;
#if WINDOWS
using Microsoft.Maui.Graphics.Win2D;
#else
using Microsoft.Maui.Graphics.Platform;
#endif

namespace ChessMentor.App.Controls;

/// <summary>Gömülü taş görsellerini (wK.png, bQ.png …) bir kez yükleyip saklar.</summary>
internal static class PieceImages
{
    private static readonly Dictionary<string, IImage?> Cache = [];

    public static IImage? Get(Piece piece)
    {
        var key = $"{(piece.Color == PieceColor.White ? 'w' : 'b')}{char.ToUpperInvariant(piece.FenChar)}";
        if (Cache.TryGetValue(key, out var img)) return img;

        try
        {
            using var stream = typeof(PieceImages).Assembly.GetManifestResourceStream($"Pieces.{key}.png");
#if WINDOWS
            img = stream is null ? null : new W2DImageLoadingService().FromStream(stream);
#else
            img = stream is null ? null : PlatformImage.FromStream(stream);
#endif
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine(ex);
            img = null;
        }
        Cache[key] = img;
        return img;
    }
}

public readonly record struct BoardArrow(int From, int To, Color Color);

/// <summary>Tahtanın çizim durumu. Her değişiklikte yeni bir örnek verilir.</summary>
public sealed record BoardState(
    Position Position,
    bool Flipped,
    Move? LastMove,
    int SelectedSquare,
    IReadOnlyCollection<int> Targets,
    IReadOnlyCollection<int> Highlights,
    IReadOnlyList<BoardArrow> Arrows)
{
    public static BoardState Initial { get; } =
        new(Position.Start(), false, null, Square.None, [], [], []);
}

/// <summary>
/// Satranç tahtası. Kareler, taşlar (Unicode satranç glifleri), son hamle, seçim,
/// yasal hedefler, ipucu vurguları ve oklar çizilir. Dokunma/tıklama ile kare seçilir.
/// </summary>
public sealed class BoardView : GraphicsView, IDrawable
{
    private static readonly Color Light = Color.FromArgb("#F0D9B5");
    private static readonly Color Dark = Color.FromArgb("#B58863");
    private static readonly Color LastMoveColor = Color.FromArgb("#699BC700");
    private static readonly Color SelectedColor = Color.FromArgb("#8014551E");
    private static readonly Color HighlightColor = Color.FromArgb("#883692E7");
    private static readonly Color TargetColor = Color.FromArgb("#55000000");
    private static readonly Color CheckColor = Color.FromArgb("#AAE03C31");

    // Dolu ve boş (kontur) glifler: ♚♛♜♝♞♟ / ♔♕♖♗♘♙
    private static string Filled(PieceType t) => t switch
    {
        PieceType.King => "♚",
        PieceType.Queen => "♛",
        PieceType.Rook => "♜",
        PieceType.Bishop => "♝",
        PieceType.Knight => "♞",
        _ => "♟"
    };

    private static string Outline(PieceType t) => t switch
    {
        PieceType.King => "♔",
        PieceType.Queen => "♕",
        PieceType.Rook => "♖",
        PieceType.Bishop => "♗",
        PieceType.Knight => "♘",
        _ => "♙"
    };

    private BoardState _state = BoardState.Initial;
    private RectF _boardRect;
    private float _sq;

    public BoardView()
    {
        Drawable = this;
        var tap = new TapGestureRecognizer();
        tap.Tapped += OnTapped;
        GestureRecognizers.Add(tap);
    }

    /// <summary>Tıklanan kare (0..63).</summary>
    public event EventHandler<int>? SquareTapped;

    public BoardState State
    {
        get => _state;
        set
        {
            _state = value;
            Invalidate();
        }
    }

    private void OnTapped(object? sender, TappedEventArgs e)
    {
        var p = e.GetPosition(this);
        if (p is null || _sq <= 0) return;
        float x = (float)p.Value.X - _boardRect.Left;
        float y = (float)p.Value.Y - _boardRect.Top;
        if (x < 0 || y < 0 || x >= _boardRect.Width || y >= _boardRect.Height) return;

        int col = (int)(x / _sq), row = (int)(y / _sq);
        int file = _state.Flipped ? 7 - col : col;
        int rank = _state.Flipped ? row : 7 - row;
        SquareTapped?.Invoke(this, Square.Make(file, rank));
    }

    private RectF SquareRect(int sq)
    {
        int file = Square.File(sq), rank = Square.Rank(sq);
        int col = _state.Flipped ? 7 - file : file;
        int row = _state.Flipped ? rank : 7 - rank;
        return new RectF(_boardRect.Left + col * _sq, _boardRect.Top + row * _sq, _sq, _sq);
    }

    public void Draw(ICanvas canvas, RectF dirtyRect)
    {
        float size = MathF.Floor(Math.Min(dirtyRect.Width, dirtyRect.Height) / 8) * 8;
        if (size <= 0) return;
        _sq = size / 8;
        _boardRect = new RectF(dirtyRect.Left + (dirtyRect.Width - size) / 2, dirtyRect.Top + (dirtyRect.Height - size) / 2, size, size);

        var s = _state;
        var pos = s.Position;

        // Kareler
        for (int sq = 0; sq < 64; sq++)
        {
            bool light = ((Square.File(sq) + Square.Rank(sq)) & 1) == 1;
            canvas.FillColor = light ? Light : Dark;
            canvas.FillRectangle(SquareRect(sq));
        }

        // Son hamle, seçim ve ipucu vurguları
        if (s.LastMove is Move lm)
        {
            canvas.FillColor = LastMoveColor;
            canvas.FillRectangle(SquareRect(lm.From));
            canvas.FillRectangle(SquareRect(lm.To));
        }
        foreach (var h in s.Highlights)
        {
            canvas.FillColor = HighlightColor;
            canvas.FillRectangle(SquareRect(h));
        }
        if (s.SelectedSquare >= 0)
        {
            canvas.FillColor = SelectedColor;
            canvas.FillRectangle(SquareRect(s.SelectedSquare));
        }

        // Şah çekilmişse şahın altına kırmızı daire
        if (pos.InCheck)
        {
            var kr = SquareRect(pos.FindKing(pos.SideToMove));
            canvas.FillColor = CheckColor;
            canvas.FillEllipse(kr.Center.X - _sq * 0.45f, kr.Center.Y - _sq * 0.45f, _sq * 0.9f, _sq * 0.9f);
        }

        DrawCoordinates(canvas);

        // Taşlar (görsel bulunamazsa yazı tipindeki satranç sembolü kullanılır)
        for (int sq = 0; sq < 64; sq++)
        {
            var piece = pos[sq];
            if (piece.IsNone) continue;
            var r = SquareRect(sq);

            if (PieceImages.Get(piece) is { } image)
            {
                canvas.DrawImage(image, r.X, r.Y, r.Width, r.Height);
                continue;
            }

            canvas.Font = new Microsoft.Maui.Graphics.Font("Segoe UI Symbol");
            canvas.FontSize = _sq * 0.8f;
            canvas.FontColor = piece.Color == PieceColor.White ? Colors.White : Color.FromArgb("#1A1A1A");
            canvas.DrawString(Filled(piece.Type), r, HorizontalAlignment.Center, VerticalAlignment.Center);
            if (piece.Color == PieceColor.White)
            {
                canvas.FontColor = Color.FromArgb("#1E1E1E");
                canvas.DrawString(Outline(piece.Type), r, HorizontalAlignment.Center, VerticalAlignment.Center);
            }
        }

        // Yasal hedef kareleri
        foreach (var t in s.Targets)
        {
            var r = SquareRect(t);
            if (pos[t].IsNone)
            {
                canvas.FillColor = TargetColor;
                canvas.FillCircle(r.Center.X, r.Center.Y, _sq * 0.15f);
            }
            else
            {
                canvas.StrokeColor = TargetColor;
                canvas.StrokeSize = _sq * 0.08f;
                canvas.DrawCircle(r.Center.X, r.Center.Y, _sq * 0.44f);
            }
        }

        foreach (var a in s.Arrows) DrawArrow(canvas, a);
    }

    private void DrawCoordinates(ICanvas canvas)
    {
        canvas.Font = Microsoft.Maui.Graphics.Font.DefaultBold;
        canvas.FontSize = Math.Max(9, _sq * 0.16f);
        for (int i = 0; i < 8; i++)
        {
            // Dosya harfleri (alt sıra)
            int file = _state.Flipped ? 7 - i : i;
            int bottomRank = _state.Flipped ? 7 : 0;
            int fsq = Square.Make(file, bottomRank);
            var fr = SquareRect(fsq);
            canvas.FontColor = ((file + bottomRank) & 1) == 1 ? Dark : Light;
            canvas.DrawString(((char)('a' + file)).ToString(), fr.Right - _sq * 0.2f, fr.Bottom - _sq * 0.24f, _sq * 0.18f, _sq * 0.22f, HorizontalAlignment.Center, VerticalAlignment.Center);

            // Sıra numaraları (sol sütun)
            int rank = _state.Flipped ? i : 7 - i;
            int leftFile = _state.Flipped ? 7 : 0;
            var rr = SquareRect(Square.Make(leftFile, rank));
            canvas.FontColor = ((leftFile + rank) & 1) == 1 ? Dark : Light;
            canvas.DrawString(((char)('1' + rank)).ToString(), rr.Left + _sq * 0.03f, rr.Top + _sq * 0.03f, _sq * 0.18f, _sq * 0.22f, HorizontalAlignment.Center, VerticalAlignment.Center);
        }
    }

    private void DrawArrow(ICanvas canvas, BoardArrow a)
    {
        var from = SquareRect(a.From).Center;
        var to = SquareRect(a.To).Center;
        float dx = to.X - from.X, dy = to.Y - from.Y;
        float len = MathF.Sqrt(dx * dx + dy * dy);
        if (len < 1) return;
        float ux = dx / len, uy = dy / len;

        float head = _sq * 0.42f;
        float width = _sq * 0.16f;
        var shaftEnd = new PointF(to.X - ux * head * 0.9f, to.Y - uy * head * 0.9f);

        canvas.StrokeColor = a.Color;
        canvas.StrokeSize = width;
        canvas.StrokeLineCap = LineCap.Round;
        canvas.DrawLine(from, shaftEnd);

        var path = new PathF();
        path.MoveTo(to.X, to.Y);
        path.LineTo(to.X - ux * head - uy * head * 0.6f, to.Y - uy * head + ux * head * 0.6f);
        path.LineTo(to.X - ux * head + uy * head * 0.6f, to.Y - uy * head - ux * head * 0.6f);
        path.Close();
        canvas.FillColor = a.Color;
        canvas.FillPath(path);
    }
}
