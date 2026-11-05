using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Text;
using System.Windows.Input;
using ChessMentor.App.Controls;
using ChessMentor.App.Services;
using ChessMentor.Core;
using ChessMentor.Engine;
using ChessMentor.Mentor;

namespace ChessMentor.App.ViewModels;

/// <summary>
/// Oyun akışı:
/// 1) Sıra sende → mentor pozisyonu arka planda analiz eder (tehdit, kritik an).
/// 2) Hamleni yaparsın → mentor hamleni değerlendirir, bu sırada bot paralel olarak düşünür.
/// 3) Hata yaptıysan (ayar açıksa) oyun durur: "Tekrar dene" ya da "Devam et".
/// 4) Bot oynar → 1'e dön.
/// </summary>
public sealed class MainViewModel : ObservableObject
{
    private const int MaxHistory = 60;

    private readonly EngineHost _engines;
    private readonly AppSettings _settings;
    private readonly List<MoveReview> _reviews = [];
    private readonly Random _random = new();

    private MentorService? _mentor;
    private Game _game = new();
    private PieceColor _userColor = PieceColor.White;
    private BotLevel _gameLevel = BotLevel.All[3];
    private bool _gameStarted;

    /// <summary>Her durum değişikliğinde artar; eski asenkron işlerin sonucu bununla yok sayılır.</summary>
    private int _turn;
    private Task<PositionAnalysis?>? _userTurnAnalysis;
    private Task<SearchResult?>? _pendingBotReply;
    private PositionAnalysis? _pausedBeforeAnalysis;
    private MoveReview? _pausedReview;
    private int _hintLevel;

    private int _selected = Square.None;
    private List<Move> _selectedMoves = [];
    private List<int> _highlights = [];
    private readonly List<BoardArrow> _arrows = [];
    private bool _flipped;

    public MainViewModel(EngineHost engines, AppSettings settings)
    {
        _engines = engines;
        _settings = settings;
        _selectedLevelIndex = Math.Clamp(settings.LevelIndex, 0, Levels.Count - 1);
        _colorIndex = Math.Clamp(settings.ColorIndex, 0, 2);

        StartGameCommand = new Command(() => Run(StartGameAsync));
        ShowSetupCommand = new Command(() => IsSetupVisible = true);
        CancelSetupCommand = new Command(() => { if (_gameStarted) IsSetupVisible = false; });
        UndoCommand = new Command(() => Run(UndoAsync));
        HintCommand = new Command(() => Run(HintAsync));
        FlipCommand = new Command(Flip);
        CopyPgnCommand = new Command(() => Run(CopyPgnAsync));
        ResignCommand = new Command(() => Run(ResignAsync));
        RetryCommand = new Command(Retry);
        ContinueCommand = new Command(() => Run(ContinueAsync));
        ShowMovesCommand = new Command(() => ShowMoves = true);
        ShowHistoryCommand = new Command(() => ShowMoves = false);

        _engines.Restarted += () => MainThread.BeginInvokeOnMainThread(() => Run(InitializeAsync));
    }

    #region Bağlanabilir özellikler

    public ObservableCollection<MentorMessage> History { get; } = [];
    public ObservableCollection<MoveRow> MoveRows { get; } = [];

    public IReadOnlyList<BotLevel> Levels => BotLevel.All;
    public IReadOnlyList<string> ColorOptions { get; } = ["Beyaz", "Siyah", "Rastgele"];

    private int _selectedLevelIndex;
    public int SelectedLevelIndex
    {
        get => _selectedLevelIndex;
        set
        {
            if (SetProperty(ref _selectedLevelIndex, value) && value >= 0)
            {
                _settings.LevelIndex = value;
                OnPropertyChanged(nameof(LevelDescription));
            }
        }
    }

    public string LevelDescription => SelectedLevelIndex >= 0 ? Levels[SelectedLevelIndex].Description : "";

    private int _colorIndex;
    public int ColorIndex
    {
        get => _colorIndex;
        set { if (SetProperty(ref _colorIndex, value) && value >= 0) _settings.ColorIndex = value; }
    }

    public bool ThinkFirst
    {
        get => _settings.ThinkFirst;
        set { _settings.ThinkFirst = value; OnPropertyChanged(); }
    }

    public bool PauseOnMistakes
    {
        get => _settings.PauseOnMistakes;
        set { _settings.PauseOnMistakes = value; OnPropertyChanged(); }
    }

    private bool _isSetupVisible = true;
    public bool IsSetupVisible
    {
        get => _isSetupVisible;
        set { if (SetProperty(ref _isSetupVisible, value)) OnPropertyChanged(nameof(IsGameBarVisible)); }
    }
    public bool IsGameBarVisible => !IsSetupVisible;

    private string _gameSummary = "";
    public string GameSummary { get => _gameSummary; private set => SetProperty(ref _gameSummary, value); }

    private string _status = "Hazırlanıyor…";
    public string Status { get => _status; private set => SetProperty(ref _status, value); }

    private Color _statusColor = Palette.StatusWaiting;
    public Color StatusColor { get => _statusColor; private set => SetProperty(ref _statusColor, value); }

    private MentorMessage? _latest;
    public MentorMessage? Latest
    {
        get => _latest;
        private set { if (SetProperty(ref _latest, value)) OnPropertyChanged(nameof(HasLatest)); }
    }
    public bool HasLatest => Latest is not null;

    /// <summary>Tehdit / kritik an uyarısı. Koç kartının üstünde ayrı gösterilir, hamle yapınca kaybolur.</summary>
    private MentorMessage? _alert;
    public MentorMessage? Alert
    {
        get => _alert;
        private set { if (SetProperty(ref _alert, value)) OnPropertyChanged(nameof(HasAlert)); }
    }
    public bool HasAlert => Alert is not null;

    private string _opponentMove = "";
    public string OpponentMove
    {
        get => _opponentMove;
        private set { if (SetProperty(ref _opponentMove, value)) OnPropertyChanged(nameof(HasOpponentMove)); }
    }
    public bool HasOpponentMove => !string.IsNullOrEmpty(OpponentMove);

    private bool _showMoves = true;
    public bool ShowMoves
    {
        get => _showMoves;
        set
        {
            if (!SetProperty(ref _showMoves, value)) return;
            OnPropertyChanged(nameof(ShowHistory));
            OnPropertyChanged(nameof(MovesTabColor));
            OnPropertyChanged(nameof(HistoryTabColor));
        }
    }
    public bool ShowHistory => !ShowMoves;
    public Color MovesTabColor => ShowMoves ? Palette.PanelLight : Colors.Transparent;
    public Color HistoryTabColor => ShowMoves ? Colors.Transparent : Palette.PanelLight;

    private string _hintText = "💡 İpucu";
    public string HintText { get => _hintText; private set => SetProperty(ref _hintText, value); }

    private string _opponentLabel = "🤖 Rakip";
    public string OpponentLabel { get => _opponentLabel; private set => SetProperty(ref _opponentLabel, value); }

    private string _playerLabel = "🧑 Sen";
    public string PlayerLabel { get => _playerLabel; private set => SetProperty(ref _playerLabel, value); }

    private string _opponentCaptured = "";
    public string OpponentCaptured { get => _opponentCaptured; private set => SetProperty(ref _opponentCaptured, value); }

    private string _playerCaptured = "";
    public string PlayerCaptured { get => _playerCaptured; private set => SetProperty(ref _playerCaptured, value); }

    private string _evalSummary = "";
    public string EvalSummary { get => _evalSummary; private set => SetProperty(ref _evalSummary, value); }

    private bool _isPaused;
    public bool IsPaused { get => _isPaused; private set => SetProperty(ref _isPaused, value); }

    private bool _isBotThinking;
    public bool IsBotThinking { get => _isBotThinking; private set => SetProperty(ref _isBotThinking, value); }

    private bool _canUserMove;
    public bool CanUserMove { get => _canUserMove; private set => SetProperty(ref _canUserMove, value); }

    public ICommand StartGameCommand { get; }
    public ICommand ShowSetupCommand { get; }
    public ICommand CancelSetupCommand { get; }
    public ICommand UndoCommand { get; }
    public ICommand HintCommand { get; }
    public ICommand FlipCommand { get; }
    public ICommand CopyPgnCommand { get; }
    public ICommand ResignCommand { get; }
    public ICommand RetryCommand { get; }
    public ICommand ContinueCommand { get; }
    public ICommand ShowMovesCommand { get; }
    public ICommand ShowHistoryCommand { get; }

    #endregion

    #region Sayfa ile iletişim

    public event Action<BoardState>? BoardChanged;
    public event Action<double, string, bool>? EvalChanged;
    public event Action? MovesChanged;

    /// <summary>Terfi taşı seçimi (sayfa tarafından sağlanır).</summary>
    public Func<Task<PieceType?>>? PickPromotion { get; set; }
    public Func<string, string, Task>? ShowAlert { get; set; }
    public Func<string, string, Task<bool>>? Confirm { get; set; }

    #endregion

    private NotationLanguage Lang => _settings.Notation;

    private async void Run(Func<Task> action)
    {
        try
        {
            await action();
        }
        catch (Exception ex)
        {
            Debug.WriteLine(ex);
            AddMessage("⚠️ Bir sorun oluştu", ex.Message, Palette.Red);
        }
    }

    private void SetStatus(string text, Color color)
    {
        Status = text;
        StatusColor = color;
    }

    #region Başlatma ve yeni oyun

    public async Task InitializeAsync()
    {
        SetStatus("Motor başlatılıyor…", Palette.StatusWaiting);
        CanUserMove = false;
        bool ok = await _engines.EnsureStartedAsync(_settings.EnginePath);
        if (!ok)
        {
            _mentor = null;
            SetStatus("Stockfish bulunamadı", Palette.StatusPaused);
            AddMessage("⚙️ Stockfish bulunamadı",
                "Uygulama ücretsiz Stockfish motorunu kullanır. Kurmak için proje klasöründe şu komutu çalıştır:\n\n" +
                "powershell -ExecutionPolicy Bypass -File scripts\\get-stockfish.ps1\n\n" +
                "Ya da Ayarlar sekmesinden stockfish.exe dosyasını seç.\n\n" +
                $"Ayrıntı: {_engines.LastError}", Palette.Orange);
            UpdateBoard();
            return;
        }

        _mentor = new MentorService(_engines.Analyst!, new MentorSettings());
        _gameStarted = false;
        IsSetupVisible = true;
        UpdateBoard();
        SetStatus("Seviyeni seç ve oyunu başlat", Palette.StatusWaiting);
        Latest = new MentorMessage
        {
            Title = "👋 Merhaba! Ben satranç koçunum.",
            Body = "Rakibinin seviyesini ve rengini seç, sonra ▶ Oyunu başlat'a bas.\n\n" +
                   "Her hamlenden sonra sana ne kadar iyi olduğunu ve nedenini söyleyeceğim. " +
                   "Takılırsan 💡 İpucu'na bas: önce küçük bir ipucu veririm, bastıkça ayrıntı artar.",
            Accent = Palette.Green
        };
    }

    private async Task StartGameAsync()
    {
        if (_mentor is null || !_engines.IsReady)
        {
            await InitializeAsync();
            if (_mentor is null) return;
        }
        if (_gameStarted && _game.PlyCount > 1 && !_game.IsOver && Confirm is not null
            && !await Confirm("Yeni oyun", "Mevcut oyun bitmeden yeni oyuna başlansın mı?"))
            return;

        IsSetupVisible = false;
        await StartGameCoreAsync();
    }

    private async Task StartGameCoreAsync()
    {
        var turn = ++_turn;
        _gameStarted = true;
        _game = new Game();
        _reviews.Clear();
        _arrows.Clear();
        _highlights = [];
        ClearSelection();
        IsPaused = false;
        _pendingBotReply = null;
        History.Clear();
        Latest = null;
        Alert = null;
        OpponentMove = "";
        CanUserMove = false;

        _userColor = ColorIndex switch
        {
            1 => PieceColor.Black,
            2 => _random.Next(2) == 0 ? PieceColor.White : PieceColor.Black,
            _ => PieceColor.White
        };
        _flipped = _userColor == PieceColor.Black;
        _gameLevel = Levels[Math.Max(0, SelectedLevelIndex)];

        ApplyMentorSettings();
        SetStatus("Motor hazırlanıyor…", Palette.StatusWaiting);
        await _engines.Opponent!.NewGameAsync();
        await _engines.Analyst!.NewGameAsync();
        await _engines.Opponent.ApplyLevelAsync(_gameLevel);
        if (turn != _turn) return;

        var colorName = _userColor == PieceColor.White ? "Beyaz" : "Siyah";
        OpponentLabel = $"🤖 Bilgisayar · {_gameLevel.Name}";
        PlayerLabel = $"🧑 Sen · {colorName}";
        GameSummary = $"Rakip: {_gameLevel}  ·  Sen: {colorName}";
        UpdateBoard();
        UpdateMoveList();
        PublishEval(50, "0.00", null);

        AddMessage("♟️ Oyun başladı", $"{colorName} taşlarla oynuyorsun. Bol şans!", Palette.Green);

        if (_userColor == PieceColor.White) BeginUserTurn();
        else await PlayBotMoveAsync(turn, null);
    }

    private void ApplyMentorSettings()
    {
        if (_mentor is null) return;
        _mentor.Settings.AnalysisTimeMs = _settings.AnalysisTimeMs;
        _mentor.Settings.Notation = _settings.Notation;
    }

    #endregion

    #region Kullanıcının sırası

    private void BeginUserTurn(PositionAnalysis? cached = null)
    {
        _hintLevel = 0;
        HintText = "💡 İpucu";
        SetStatus("● Sıra sende", Palette.StatusYourTurn);
        CanUserMove = true;
        ApplyMentorSettings();
        var turn = _turn;
        _userTurnAnalysis = cached is not null ? Task.FromResult<PositionAnalysis?>(cached) : AnalyzeUserTurnAsync(turn);
    }

    private async Task<PositionAnalysis?> AnalyzeUserTurnAsync(int turn)
    {
        if (_mentor is null) return null;
        try
        {
            var analysis = await _mentor.AnalyzeAsync(_game);
            if (turn != _turn) return analysis;

            PublishEval(analysis);

            if (_settings.ShowThreats && _game.PlyCount > 0)
            {
                var threat = await _mentor.FindThreatAsync(analysis);
                if (turn == _turn && threat is not null)
                {
                    if (ThinkFirst)
                    {
                        SetAlert("🤔 Dikkat!", threat.IsMateThreat
                            ? "Rakip mat tehdit ediyor! Ne olduğunu görebiliyor musun?"
                            : "Rakibin son hamlesinin bir tehdidi var. Ne olduğunu görebiliyor musun?\nBulamazsan 💡 İpucu'na bas.", Palette.Orange);
                    }
                    else if (_game.Current.NullMove()?.FindLegalMove(threat.MoveUci) is Move tm)
                    {
                        SetAlert("⚠️ Rakibin tehdidi", threat.Description, Palette.Orange);
                        _arrows.Add(new BoardArrow(tm.From, tm.To, Palette.ArrowThreat));
                        UpdateBoard();
                    }
                }
            }

            if (turn == _turn && ThinkFirst && analysis.IsCritical)
                SetAlert("⚡ Kritik an!", "Burada sadece bir iyi hamle var. Acele etme: şah çeken, taş alan ve tehdit eden hamleleri tek tek kontrol et.", Palette.Purple);

            return analysis;
        }
        catch (Exception ex)
        {
            Debug.WriteLine(ex);
            return null;
        }
    }

    public void OnSquareTapped(int sq) => Run(() => OnSquareTappedAsync(sq));

    private async Task OnSquareTappedAsync(int sq)
    {
        if (!CanUserMove || _game.IsOver || _game.Current.SideToMove != _userColor) return;
        var pos = _game.Current;

        if (_selected >= 0)
        {
            var candidates = _selectedMoves.Where(m => m.To == sq).ToList();
            if (candidates.Count > 0)
            {
                var move = candidates[0];
                if (candidates.Count > 1)
                {
                    var type = PickPromotion is null ? PieceType.Queen : await PickPromotion();
                    if (type is null)
                    {
                        ClearSelection();
                        UpdateBoard();
                        return;
                    }
                    move = candidates.First(m => m.Promotion == type);
                }
                ClearSelection();
                await UserMoveAsync(move);
                return;
            }
        }

        var piece = pos[sq];
        if (!piece.IsNone && piece.Color == _userColor && sq != _selected)
        {
            _selected = sq;
            _selectedMoves = pos.GenerateLegalMoves().Where(m => m.From == sq).ToList();
        }
        else
        {
            ClearSelection();
        }
        UpdateBoard();
    }

    private async Task UserMoveAsync(Move move)
    {
        if (_mentor is null) return;
        CanUserMove = false;
        var analysisTask = _userTurnAnalysis;
        var turn = ++_turn;

        var before = _game.Current;
        var movesBefore = _game.UciMoves.ToList();
        if (!_game.TryMakeMove(move)) { BeginUserTurn(); return; }

        _arrows.Clear();
        _highlights = [];
        Alert = null;
        OpponentMove = "";
        UpdateBoard();
        UpdateMoveList();
        SetStatus("🔎 Koç hamleni inceliyor…", Palette.StatusWaiting);

        var beforeAnalysis = analysisTask is not null ? await analysisTask : null;
        beforeAnalysis ??= await _mentor.AnalyzeAsync(_game.StartFen, movesBefore, before);
        if (turn != _turn) return;

        // Bot, koç hamleni incelerken paralel olarak düşünmeye başlar.
        PositionAnalysis? afterAnalysis = null;
        Task<SearchResult?>? botTask = null;
        if (!_game.IsOver)
        {
            botTask = SearchBotMoveAsync();
            afterAnalysis = await _mentor.AnalyzeAsync(_game, multi: false);
            if (turn != _turn) return;
            PublishEval(afterAnalysis);
        }

        var review = _mentor.ReviewMove(before, beforeAnalysis, move, _game.Current, afterAnalysis);
        _reviews.Add(review);
        UpdateMoveList();

        if (PauseOnMistakes && review.Quality >= MoveQuality.Mistake && !_game.IsOver)
        {
            AddReviewMessage(review, revealBetter: false);
            _pausedReview = review;
            _pausedBeforeAnalysis = beforeAnalysis;
            _pendingBotReply = botTask;
            IsPaused = true;
            SetStatus("⏸ Hata yaptın: tekrar dene ya da devam et", Palette.StatusPaused);
            return;
        }

        AddReviewMessage(review, revealBetter: true);
        ShowBestMoveArrow(review, before);

        if (_game.IsOver)
        {
            await FinishGameAsync();
            return;
        }
        await PlayBotMoveAsync(turn, botTask);
    }

    private void AddReviewMessage(MoveReview review, bool revealBetter)
    {
        var lines = new List<string>(review.Details);
        if (revealBetter) lines.AddRange(review.Better);
        else if (review.Better.Count > 0)
            lines.Add("\n🔁 Tekrar dene: hamleni geri alıp daha iyisini ara.\n▶️ Devam et: daha iyi hamleyi göstereyim.");

        AddMessage(review.Headline, string.Join("\n", lines), Color.FromArgb(review.Quality.ColorHex()));
    }

    private void ShowBestMoveArrow(MoveReview review, Position before)
    {
        if (!review.Quality.IsError() || review.BestMoveUci is null) return;
        if (before.FindLegalMove(review.BestMoveUci) is Move best)
        {
            _arrows.Add(new BoardArrow(best.From, best.To, Palette.ArrowBest));
            UpdateBoard();
        }
    }

    #endregion

    #region Hata sonrası: tekrar dene / devam et

    private void Retry()
    {
        if (!IsPaused) return;
        IsPaused = false;
        _turn++;
        _pendingBotReply = null;
        Alert = null;
        _game.Undo();
        if (_reviews.Count > 0) _reviews.RemoveAt(_reviews.Count - 1);
        _arrows.Clear();
        _highlights = [];
        UpdateBoard();
        UpdateMoveList();
        AddMessage("🔁 Tekrar dene", "Hamleni geri aldım. Bu sefer rakibin nasıl cevap vereceğini de düşünerek oyna.", Palette.Blue);
        BeginUserTurn(_pausedBeforeAnalysis);
    }

    private async Task ContinueAsync()
    {
        if (!IsPaused || _pausedReview is null) return;
        IsPaused = false;
        var review = _pausedReview;
        if (review.Better.Count > 0)
            AddMessage("💡 Doğrusu", string.Join("\n", review.Better), Palette.Green);

        var before = _game.Positions[^2];
        ShowBestMoveArrow(review, before);
        await PlayBotMoveAsync(_turn, _pendingBotReply);
    }

    #endregion

    #region Botun sırası

    private Task<SearchResult?> SearchBotMoveAsync()
    {
        var engine = _engines.Opponent!;
        var fen = _game.StartFen;
        var moves = _game.UciMoves.ToList();
        var limits = new SearchLimits { Depth = _gameLevel.Depth, MoveTimeMs = _gameLevel.MoveTimeMs };
        return Task.Run<SearchResult?>(async () =>
        {
            try { return await engine.SearchAsync(fen, moves, limits); }
            catch (Exception ex) { Debug.WriteLine(ex); return null; }
        });
    }

    private async Task PlayBotMoveAsync(int turn, Task<SearchResult?>? searchTask)
    {
        IsBotThinking = true;
        SetStatus("🤖 Rakip düşünüyor…", Palette.StatusWaiting);
        var sw = Stopwatch.StartNew();
        try
        {
            searchTask ??= SearchBotMoveAsync();
            var result = await searchTask;

            // İnsan gibi hissettirmek için kısa bir "düşünme" süresi
            int target = _random.Next(300, 900);
            if (sw.ElapsedMilliseconds < target) await Task.Delay(target - (int)sw.ElapsedMilliseconds);
            if (turn != _turn) return;

            if (result is null || _game.Current.FindLegalMove(result.BestMove) is not Move move)
            {
                AddMessage("⚠️ Bilgisayar hamle bulamadı", "Motorla iletişimde bir sorun oldu. Yeni oyun başlatmayı dene.", Palette.Red);
                SetStatus("Hata", Palette.StatusPaused);
                return;
            }

            var before = _game.Current;
            _game.TryMakeMove(move);
            _turn++;
            ClearSelection();
            UpdateBoard();
            UpdateMoveList();
            OpponentMove = _mentor!.DescribeOpponentMove(before, move);
            AddLog(OpponentMove, "", Palette.Muted);
        }
        finally
        {
            IsBotThinking = false;
        }

        if (_game.IsOver)
        {
            await FinishGameAsync();
            return;
        }
        BeginUserTurn();
    }

    #endregion

    #region Butonlar

    private async Task HintAsync()
    {
        if (!CanUserMove || _userTurnAnalysis is null) return;
        var analysis = await _userTurnAnalysis;
        if (analysis is null || !CanUserMove) return;

        if (_hintLevel >= Hint.MaxLevel) return;
        _hintLevel++;
        var hint = HintService.GetHint(analysis, _hintLevel, Lang);

        _highlights = hint.HighlightSquares.ToList();
        if (hint.Arrow is Move arrow) _arrows.Add(new BoardArrow(arrow.From, arrow.To, Palette.ArrowHint));
        UpdateBoard();

        AddMessage($"💡 İpucu {_hintLevel}/{Hint.MaxLevel}", hint.Text, Palette.Blue);
        HintText = _hintLevel >= Hint.MaxLevel ? "💡 Cevap gösterildi" : $"💡 Biraz daha ({_hintLevel + 1}/{Hint.MaxLevel})";
    }

    private Task UndoAsync()
    {
        if (IsBotThinking || !_gameStarted) return Task.CompletedTask;
        if (IsPaused)
        {
            Retry();
            return Task.CompletedTask;
        }

        // Kullanıcının sırasına dönecek şekilde geri al
        int count = _game.Current.SideToMove == _userColor ? 2 : 1;
        int minPlies = _userColor == PieceColor.Black ? 1 : 0; // siyahsan bilgisayarın ilk hamlesi kalır
        if (_game.PlyCount - count < minPlies) return Task.CompletedTask;

        _turn++;
        for (int i = 0; i < count; i++) _game.Undo();
        _reviews.RemoveAll(r => r.Ply >= _game.PlyCount);
        _arrows.Clear();
        _highlights = [];
        ClearSelection();
        UpdateBoard();
        UpdateMoveList();
        AddLog("↩️ Hamle geri alındı", "", Palette.Muted);
        BeginUserTurn();
        return Task.CompletedTask;
    }

    private void Flip()
    {
        _flipped = !_flipped;
        UpdateBoard();
        EvalChanged?.Invoke(_lastWhiteWin, _lastEvalText, _flipped);
    }

    private async Task CopyPgnAsync()
    {
        var white = _userColor == PieceColor.White ? "Sen" : $"Stockfish ({_gameLevel.Name})";
        var black = _userColor == PieceColor.Black ? "Sen" : $"Stockfish ({_gameLevel.Name})";
        await Clipboard.Default.SetTextAsync(_game.ToPgn(white, black));
        AddMessage("📋 Oyun kaydı (PGN) kopyalandı", "Lichess veya Chess.com'un analiz sayfasına yapıştırarak oyunu inceleyebilirsin.", Palette.Muted);
    }

    private async Task ResignAsync()
    {
        if (!_gameStarted || _game.IsOver || _game.PlyCount == 0) return;
        if (Confirm is not null && !await Confirm("Terk et", "Oyunu terk etmek istediğine emin misin?")) return;
        IsPaused = false;
        _game.Resign(_userColor);
        await FinishGameAsync();
    }

    #endregion

    #region Oyun sonu

    private async Task FinishGameAsync()
    {
        _turn++;
        CanUserMove = false;
        IsPaused = false;

        var report = GameReport.Build(_reviews, _game, _userColor);
        SetStatus($"🏁 {report.ResultText}", Palette.StatusOver);
        AddMessage($"🏁 {report.ResultText}", report.ToText().Split('\n', 2).ElementAtOrDefault(1)?.Trim() ?? "", Palette.Green);

        bool? userWon = _game.Outcome switch
        {
            GameOutcome.WhiteWins => _userColor == PieceColor.White,
            GameOutcome.BlackWins => _userColor == PieceColor.Black,
            _ => null
        };

        try
        {
            var stats = PlayerStats.Load(_settings.StatsPath);
            stats.RecordGame(_reviews, report, userWon);
            stats.Save(_settings.StatsPath);
        }
        catch (Exception ex)
        {
            Debug.WriteLine(ex);
        }

        IsSetupVisible = true;
        if (ShowAlert is not null) await ShowAlert("Oyun bitti", report.ToText());
    }

    #endregion

    #region Yardımcılar

    private double _lastWhiteWin = 50;
    private string _lastEvalText = "0.00";

    private void PublishEval(PositionAnalysis analysis)
    {
        var white = analysis.WhiteScore;
        PublishEval(WinChance.FromScore(white), white.Format(), white.MateIn);
    }

    private void PublishEval(double whiteWin, string text, int? whiteMate)
    {
        _lastWhiteWin = whiteWin;
        _lastEvalText = text;
        EvalChanged?.Invoke(whiteWin, text, _flipped);

        // Kullanıcı açısından sade bir özet
        double myWin = _userColor == PieceColor.White ? whiteWin : 100 - whiteWin;
        int? myMate = whiteMate is int m ? (_userColor == PieceColor.White ? m : -m) : null;
        string verdict = myMate switch
        {
            > 0 => $"{myMate} hamlede mat edebilirsin!",
            < 0 => $"Rakip {-myMate} hamlede mat edebilir!",
            _ => myWin switch
            {
                >= 90 => "Kazanıyorsun",
                >= 70 => "Açık ara öndesin",
                >= 57 => "Biraz öndesin",
                > 43 => "Durum eşit",
                > 30 => "Biraz geridesin",
                > 10 => "Zor durumdasın",
                _ => "Kaybediyorsun"
            }
        };
        EvalSummary = $"Kazanma şansın %{myWin:0}  ·  {verdict}";
    }

    private void ClearSelection()
    {
        _selected = Square.None;
        _selectedMoves = [];
    }

    private void UpdateBoard()
    {
        BoardChanged?.Invoke(new BoardState(
            _game.Current,
            _flipped,
            _game.LastMove,
            _selected,
            _selectedMoves.Select(m => m.To).Distinct().ToList(),
            _highlights,
            _arrows.ToList()));
        UpdateCaptured();
    }

    private void UpdateMoveList()
    {
        var moves = _game.SanMoves(Lang);
        var colors = _reviews.ToDictionary(r => r.Ply, r => r.Quality.IsError() ? Color.FromArgb(r.Quality.ColorHex()) : Palette.Text);

        // Sadece değişen kısmı güncelle (liste her hamlede baştan kurulmaz)
        int rows = (moves.Count + 1) / 2;
        while (MoveRows.Count > rows) MoveRows.RemoveAt(MoveRows.Count - 1);
        for (int i = 0; i < rows; i++)
        {
            int w = i * 2, b = w + 1;
            var row = new MoveRow
            {
                Number = $"{i + 1}.",
                White = moves[w],
                Black = b < moves.Count ? moves[b] : "",
                WhiteColor = colors.GetValueOrDefault(w, Palette.Text),
                BlackColor = colors.GetValueOrDefault(b, Palette.Text)
            };
            if (i < MoveRows.Count)
            {
                var old = MoveRows[i];
                if (old.White != row.White || old.Black != row.Black || old.WhiteColor != row.WhiteColor || old.BlackColor != row.BlackColor)
                    MoveRows[i] = row;
            }
            else
            {
                MoveRows.Add(row);
            }
        }
        MovesChanged?.Invoke();
    }

    /// <summary>Alınan taşlar ve materyal farkı: "♟♟♞ +3".</summary>
    private void UpdateCaptured()
    {
        var pos = _game.Current;
        var start = new Dictionary<PieceType, int> { [PieceType.Queen] = 1, [PieceType.Rook] = 2, [PieceType.Bishop] = 2, [PieceType.Knight] = 2, [PieceType.Pawn] = 8 };

        string CapturedBy(PieceColor capturer)
        {
            var victim = capturer.Opposite();
            var sb = new StringBuilder();
            foreach (var (type, count) in start)
            {
                int onBoard = 0;
                for (int sq = 0; sq < 64; sq++)
                    if (pos[sq].Type == type && pos[sq].Color == victim) onBoard++;
                for (int i = 0; i < Math.Max(0, count - onBoard); i++) sb.Append(Glyph(type, victim));
            }
            int diff = pos.MaterialBalance(capturer);
            if (diff > 0) sb.Append("  +").Append(diff);
            return sb.ToString();
        }

        PlayerCaptured = CapturedBy(_userColor);
        OpponentCaptured = CapturedBy(_userColor.Opposite());
    }

    private static string Glyph(PieceType t, PieceColor c) => (t, c) switch
    {
        (PieceType.Queen, PieceColor.White) => "♕",
        (PieceType.Rook, PieceColor.White) => "♖",
        (PieceType.Bishop, PieceColor.White) => "♗",
        (PieceType.Knight, PieceColor.White) => "♘",
        (PieceType.Pawn, PieceColor.White) => "♙",
        (PieceType.Queen, _) => "♛",
        (PieceType.Rook, _) => "♜",
        (PieceType.Bishop, _) => "♝",
        (PieceType.Knight, _) => "♞",
        _ => "♟"
    };

    /// <summary>Sadece geçmişe yazılan önemsiz bilgi.</summary>
    private void AddLog(string title, string body, Color accent)
    {
        History.Insert(0, new MentorMessage { Title = title, Body = body, Accent = accent });
        while (History.Count > MaxHistory) History.RemoveAt(History.Count - 1);
    }

    private void SetAlert(string title, string body, Color accent)
    {
        Alert = new MentorMessage { Title = title, Body = body, Accent = accent };
        AddLog(title, body, accent);
    }

    /// <summary>Yeni mesaj koç kartında büyük gösterilir; önceki mesaj geçmişe kayar.</summary>
    private void AddMessage(string title, string body, Color accent)
    {
        if (Latest is not null)
        {
            History.Insert(0, Latest);
            while (History.Count > MaxHistory) History.RemoveAt(History.Count - 1);
        }
        Latest = new MentorMessage { Title = title, Body = body, Accent = accent };
    }

    #endregion
}
