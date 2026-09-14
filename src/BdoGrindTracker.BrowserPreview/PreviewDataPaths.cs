namespace BdoGrindTracker.App.Persistence;

// The linked stores supply layout normalization, but the preview never creates
// them. Fail closed if a future registration accidentally requests real data.
internal sealed class AppDataPaths
{
    public static AppDataPaths Current => throw new InvalidOperationException(
        "Die Browser-Vorschau darf keine Tracker-Datenordner öffnen.");
    public string BaseDirectory => throw new InvalidOperationException(
        "Die Browser-Vorschau speichert ausschließlich im Arbeitsspeicher.");
}
