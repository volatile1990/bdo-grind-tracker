using System.Text.Json;

namespace BdoGrindTracker.App.Persistence;

internal sealed record WindowPlacement(int X, int Y, int Width, int Height, bool Maximized)
{
    public Rectangle Bounds => new(X, Y, Width, Height);
    public Rectangle Fit(IReadOnlyList<Rectangle> workAreas)
    {
        var work = workAreas.FirstOrDefault(area => area.IntersectsWith(Bounds));
        if (work == Rectangle.Empty) work = workAreas[0];
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
            File.WriteAllText(_path + ".tmp", JsonSerializer.Serialize(placement));
            File.Move(_path + ".tmp", _path, true);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
    }
}
