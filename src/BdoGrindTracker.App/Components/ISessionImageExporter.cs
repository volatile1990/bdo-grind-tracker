namespace BdoGrindTracker.App.Components;

/// <summary>Optional desktop integration; browser previews use the PNG download and clipboard APIs.</summary>
public interface ISessionImageExporter
{
    Task<bool> SaveAsync(string pngDataUrl, string fileName);
    Task CopyAsync(string pngDataUrl);
}
