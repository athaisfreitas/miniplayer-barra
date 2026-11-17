using MiniplayerBarra.Ui;
using Xunit;

namespace MiniplayerBarra.Tests;

public class FocusMemoryTests
{
    private static readonly IntPtr Notepad = new(100);
    private static readonly IntPtr Old = new(200);
    private static readonly IntPtr Tray = new(300);

    [Fact]
    public void Press_with_real_foreground_saves_it()
    {
        var m = new FocusMemory();
        m.OnPress(Notepad, false);
        Assert.Equal(Notepad, m.Saved);
    }

    [Fact]
    public void Press_when_taskbar_took_front_uses_window_seen_on_hover()
    {
        var m = new FocusMemory();
        m.OnHover(Notepad, false);
        m.OnPress(Tray, true);
        Assert.Equal(Notepad, m.Saved);
    }

    [Fact]
    public void Minimized_last_window_is_never_brought_back()
    {
        // Clique antigo guardou "Old"; o ciclo terminou; a usuária minimizou tudo (frente = barra).
        var m = new FocusMemory();
        m.OnPress(Old, false);
        m.BeginCycle();
        m.EndCycle();
        m.OnHover(Tray, true);
        m.OnPress(Tray, true);
        Assert.Equal(IntPtr.Zero, m.Saved);
    }

    [Fact]
    public void Stale_value_does_not_survive_press_on_empty_front_without_hover()
    {
        var m = new FocusMemory();
        m.OnPress(Old, false); // menu aberto e "Sair" escolhido: nenhum ciclo
        m.OnLeave();
        m.OnPress(IntPtr.Zero, true);
        Assert.Equal(IntPtr.Zero, m.Saved);
    }

    [Fact]
    public void Leaving_the_player_drops_the_hover_candidate()
    {
        var m = new FocusMemory();
        m.OnHover(Notepad, false);
        m.OnLeave();
        m.OnPress(Tray, true);
        Assert.Equal(IntPtr.Zero, m.Saved);
    }

    [Fact]
    public void Hover_on_empty_front_clears_candidate()
    {
        var m = new FocusMemory();
        m.OnHover(Notepad, false);
        m.OnHover(IntPtr.Zero, true);
        m.OnPress(Tray, true);
        Assert.Equal(IntPtr.Zero, m.Saved);
    }

    [Fact]
    public void Quick_second_click_during_cycle_keeps_window()
    {
        var m = new FocusMemory();
        m.OnPress(Notepad, false);
        m.BeginCycle();
        m.OnHover(IntPtr.Zero, true); // movimento logo depois do clique, com a frente vazia
        m.OnPress(IntPtr.Zero, true);
        Assert.Equal(Notepad, m.Saved);
    }

    [Fact]
    public void End_of_cycle_and_forget_clear_saved()
    {
        var m = new FocusMemory();
        m.OnPress(Notepad, false);
        m.BeginCycle();
        m.EndCycle();
        Assert.Equal(IntPtr.Zero, m.Saved);
        m.OnPress(Notepad, false);
        m.Forget();
        Assert.Equal(IntPtr.Zero, m.Saved);
    }

    [Theory]
    [InlineData(100, true, false, 0, true, true)]     // frente vazia: devolve
    [InlineData(100, true, false, 300, true, true)]   // barra na frente: devolve
    [InlineData(100, true, true, 300, true, false)]   // minimizada: não devolve
    [InlineData(100, false, false, 0, true, false)]   // janela não existe mais
    [InlineData(0, true, false, 0, true, false)]      // nada guardado
    [InlineData(100, true, false, 400, false, false)] // outra janela veio para a frente: respeita
    [InlineData(100, true, false, 100, false, false)] // já está na frente
    public void ShouldRestore_rules(int saved, bool alive, bool minimized, int fg, bool fgEmptyOrOurs, bool expected) =>
        Assert.Equal(expected, FocusMemory.ShouldRestore(new IntPtr(saved), alive, minimized, new IntPtr(fg), fgEmptyOrOurs));
}
