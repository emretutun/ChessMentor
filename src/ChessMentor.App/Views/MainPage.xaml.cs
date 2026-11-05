using System.Diagnostics;
using ChessMentor.App.ViewModels;
using ChessMentor.Core;

namespace ChessMentor.App.Views;

public partial class MainPage : ContentPage
{
    private readonly MainViewModel _vm;
    private bool _initialized;

    public MainPage(MainViewModel vm)
    {
        InitializeComponent();
        BindingContext = _vm = vm;

        Board.SquareTapped += (_, sq) => vm.OnSquareTapped(sq);
        vm.BoardChanged += state => Board.State = state;
        vm.EvalChanged += (whiteWin, text, flipped) => EvalBarView.Update(whiteWin, text, flipped);

        vm.PickPromotion = async () =>
        {
            var choice = await DisplayActionSheetAsync("Piyon terfisi: hangi taş?", "İptal", null, "Vezir", "At", "Kale", "Fil");
            return choice switch
            {
                "Vezir" => PieceType.Queen,
                "At" => PieceType.Knight,
                "Kale" => PieceType.Rook,
                "Fil" => PieceType.Bishop,
                _ => (PieceType?)null
            };
        };
        vm.ShowAlert = (title, message) => DisplayAlertAsync(title, message, "Tamam");
        vm.Confirm = (title, message) => DisplayAlertAsync(title, message, "Evet", "Hayır");

        BoardArea.SizeChanged += (_, _) => ResizeBoard();
        vm.MovesChanged += () =>
        {
            if (vm.MoveRows.Count > 0) MovesList.ScrollTo(vm.MoveRows.Count - 1, position: ScrollToPosition.End, animate: false);
        };
    }

    private void ResizeBoard()
    {
        double size = Math.Min(BoardArea.Width - 40, BoardArea.Height - 130);
        size = Math.Max(240, Math.Floor(size / 8) * 8);
        Board.WidthRequest = size;
        Board.HeightRequest = size;
        EvalBarView.HeightRequest = size;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        if (_initialized) return;
        _initialized = true;
        try
        {
            await _vm.InitializeAsync();
        }
        catch (Exception ex)
        {
            Debug.WriteLine(ex);
            await DisplayAlertAsync("Başlatma hatası", ex.Message, "Tamam");
        }
    }
}
