namespace ChessMentor.App.Controls;

/// <summary>Değerlendirme çubuğu: beyazın kazanma şansını dikey bir çubukla gösterir.</summary>
public sealed class EvalBar : GraphicsView, IDrawable
{
    private double _whiteWin = 50;
    private bool _flipped;
    private string _text = "0.00";

    public EvalBar()
    {
        Drawable = this;
    }

    public void Update(double whiteWinPercent, string text, bool flipped)
    {
        _whiteWin = Math.Clamp(whiteWinPercent, 0, 100);
        _text = text;
        _flipped = flipped;
        Invalidate();
    }

    public void Draw(ICanvas canvas, RectF r)
    {
        canvas.FillColor = Color.FromArgb("#403D39");
        canvas.FillRoundedRectangle(r, 4);

        float whiteHeight = (float)(r.Height * _whiteWin / 100);
        canvas.FillColor = Color.FromArgb("#F5F5F5");
        if (_flipped)
            canvas.FillRoundedRectangle(new RectF(r.X, r.Y, r.Width, whiteHeight), 4);
        else
            canvas.FillRoundedRectangle(new RectF(r.X, r.Bottom - whiteHeight, r.Width, whiteHeight), 4);

        // Puan yazısı: önde olan tarafın bölgesine yazılır
        bool whiteAhead = _whiteWin >= 50;
        bool textAtBottom = whiteAhead != _flipped;
        canvas.FontSize = 10;
        canvas.Font = Microsoft.Maui.Graphics.Font.DefaultBold;
        canvas.FontColor = whiteAhead ? Color.FromArgb("#403D39") : Color.FromArgb("#F5F5F5");
        var label = _text.TrimStart('+', '-');
        var textRect = textAtBottom
            ? new RectF(r.X, r.Bottom - 20, r.Width, 18)
            : new RectF(r.X, r.Y + 2, r.Width, 18);
        canvas.DrawString(label, textRect, HorizontalAlignment.Center, VerticalAlignment.Center);
    }
}
