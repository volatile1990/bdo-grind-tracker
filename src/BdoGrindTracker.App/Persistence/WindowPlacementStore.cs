using System.Text.Json;

namespace BdoGrindTracker.App.Persistence;

internal sealed record WindowPlacement(int X, int Y, int Width, int Height, bool Maximized)
{
    public Rectangle Bounds => new(X, Y, Width, Height);

    public static WindowPlacement? Capture(Rectangle bounds, Rectangle restoreBounds,
        FormWindowState state, FormWindowState lastNonMinimizedState)
    {
        var normalBounds = state == FormWindowState.Normal ? bounds : restoreBounds;
        if (normalBounds.Width <= 0 || normalBounds.Height <= 0) return null;
        var maximized = state == FormWindowState.Maximized ||
            state == FormWindowState.Minimized && lastNonMinimizedState == FormWindowState.Maximized;
        return new(normalBounds.X, normalBounds.Y, normalBounds.Width, normalBounds.Height, maximized);
    }

    public Rectangle Fit(IReadOnlyList<Rectangle> workAreas)
    {
        // A small overlap with the primary monitor must not move a window that
        // was mostly on another monitor back to the primary screen.
        var work = workAreas.OrderByDescending(area =>
        {
            var overlap = Rectangle.Intersect(area, Bounds);
            return (long)Math.Max(0, overlap.Width) * Math.Max(0, overlap.Height);
        }).First();
        var width = Math.Clamp(Width, Math.Min(860, work.Width), work.Width);
        var height = Math.Clamp(Height, Math.Min(640, work.Height), work.Height);
        return new(Math.Clamp(X, work.Left, work.Right - width), Math.Clamp(Y, work.Top, work.Bottom - height), width, height);
    }
}

internal sealed class WindowPlacementStore(string? path = null)
{
    private readonly string _path = path ?? Path.Combine(AppDataPaths.Current.BaseDirectory, "window-placement.json");
    public WindowPlacement? Load()
    {
        try
        {
            var result = JsonSerializer.Deserialize<WindowPlacement>(File.ReadAllText(_path));
            return result is { Width: > 0, Height: > 0 } ? result : null;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException) { return null; }
    }
    public void Save(WindowPlacement placement)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(_path))!);
            AtomicFile.WriteAllText(_path, JsonSerializer.Serialize(placement));
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
    }
}
