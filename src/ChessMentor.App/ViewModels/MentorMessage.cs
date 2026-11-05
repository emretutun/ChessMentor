namespace ChessMentor.App.ViewModels;

/// <summary>Mentor panelinde gösterilen tek bir mesaj kartı.</summary>
public sealed class MentorMessage
{
    public required string Title { get; init; }
    public string Body { get; init; } = "";
    public Color Accent { get; init; } = Palette.Muted;
    public bool HasBody => !string.IsNullOrWhiteSpace(Body);
}

/// <summary>Hamle listesinde bir satır: "12. | beyazın hamlesi | siyahın hamlesi".</summary>
public sealed class MoveRow
{
    public required string Number { get; init; }
    public required string White { get; init; }
    public string Black { get; init; } = "";
    public Color WhiteColor { get; init; } = Palette.Text;
    public Color BlackColor { get; init; } = Palette.Text;
}

public static class Palette
{
    public static readonly Color Background = Color.FromArgb("#161512");
    public static readonly Color Panel = Color.FromArgb("#262421");
    public static readonly Color PanelLight = Color.FromArgb("#302E2B");
    public static readonly Color Text = Color.FromArgb("#E8E6E3");
    public static readonly Color Muted = Color.FromArgb("#9E9B98");
    public static readonly Color Green = Color.FromArgb("#81B64C");
    public static readonly Color Blue = Color.FromArgb("#3692E7");
    public static readonly Color Orange = Color.FromArgb("#E6A23C");
    public static readonly Color Red = Color.FromArgb("#D9443C");
    public static readonly Color Purple = Color.FromArgb("#A77BCA");

    public static readonly Color StatusYourTurn = Color.FromArgb("#4E7A27");
    public static readonly Color StatusWaiting = Color.FromArgb("#3C3A36");
    public static readonly Color StatusPaused = Color.FromArgb("#9A6A1E");
    public static readonly Color StatusOver = Color.FromArgb("#2B5F91");

    public static readonly Color ArrowBest = Color.FromArgb("#CC5C9E31");
    public static readonly Color ArrowHint = Color.FromArgb("#CC3692E7");
    public static readonly Color ArrowThreat = Color.FromArgb("#CCD9443C");
}
