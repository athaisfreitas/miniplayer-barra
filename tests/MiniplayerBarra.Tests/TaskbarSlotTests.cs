using System.Drawing;
using MiniplayerBarra.Ui;
using Xunit;

namespace MiniplayerBarra.Tests;

public class TaskbarSlotTests
{
    private static readonly Rectangle Bar = new(0, 1040, 1920, 40);

    [Fact]
    public void Centered_no_widgets_sits_at_left_edge()
    {
        var r = TaskbarSlot.Place(new TaskbarInfo(Bar, true, null, 700, 1200, 1700), 400, 140, 40, 8);
        Assert.Equal(Bar.Left + 8, r.Left);
        Assert.True(r.Right <= 700 - 8);
    }

    [Fact]
    public void Centered_with_widgets_sits_right_of_widgets()
    {
        var widgets = new Rectangle(0, 1040, 150, 40);
        var r = TaskbarSlot.Place(new TaskbarInfo(Bar, true, widgets, 700, 1200, 1700), 300, 140, 40, 8);
        Assert.Equal(widgets.Right + 8, r.Left);
    }

    [Fact]
    public void Left_aligned_sits_left_of_RightLimit()
    {
        var r = TaskbarSlot.Place(new TaskbarInfo(Bar, false, null, 0, 600, 1700), 300, 140, 40, 8);
        Assert.Equal(1700 - 8, r.Right);
        Assert.True(r.Left >= 600 + 8);
        Assert.Equal(300, r.Width);
    }

    [Fact]
    public void Left_aligned_shrinks_to_available()
    {
        var r = TaskbarSlot.Place(new TaskbarInfo(Bar, false, null, 0, 1500, 1700), 300, 140, 40, 8);
        Assert.Equal(1700 - 8, r.Right);
        Assert.Equal(1700 - 8 - (1500 + 8), r.Width);
    }

    [Fact]
    public void Too_narrow_space_still_returns_min_width()
    {
        var r = TaskbarSlot.Place(new TaskbarInfo(Bar, true, null, 66, 1200, 1700), 300, 140, 40, 8);
        Assert.Equal(140, r.Width);
        var r2 = TaskbarSlot.Place(new TaskbarInfo(Bar, false, null, 0, 1650, 1700), 300, 140, 40, 8);
        Assert.Equal(140, r2.Width);
    }

    [Fact]
    public void Vertically_centered_in_taskbar()
    {
        var r = TaskbarSlot.Place(new TaskbarInfo(Bar, true, null, 700, 1200, 1700), 300, 140, 36, 8);
        Assert.Equal(Bar.Top + (Bar.Height - 36) / 2, r.Top);
    }
}
