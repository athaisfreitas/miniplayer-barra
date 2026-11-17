using System.Drawing;

namespace MiniplayerBarra.Ui;

internal record TaskbarInfo(Rectangle Taskbar, bool Centered, Rectangle? Widgets, int AppsLeft, int AppsRight, int RightLimit);

internal static class TaskbarSlot
{
    public static Rectangle Place(TaskbarInfo info, int desiredWidth, int minWidth, int height, int margin)
    {
        int top = info.Taskbar.Top + (info.Taskbar.Height - height) / 2;
        if (info.Centered)
        {
            int left = info.Widgets is { } w ? w.Right + margin : info.Taskbar.Left + margin;
            int available = info.AppsLeft - margin - left;
            return new Rectangle(left, top, Width(available, desiredWidth, minWidth), height);
        }
        else
        {
            int right = info.RightLimit - margin;
            int available = right - (info.AppsRight + margin);
            int width = Width(available, desiredWidth, minWidth);
            return new Rectangle(right - width, top, width, height);
        }
    }

    private static int Width(int available, int desired, int min) => Math.Max(min, Math.Min(available, desired));
}
