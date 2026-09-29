using System.Buffers.Binary;
using BdoGrindTracker.App.Components;
using BdoGrindTracker.App.Localization;

namespace BdoGrindTracker.App.UI;

/// <summary>Keeps file dialogs and the clipboard on the window's STA thread, independently of tracking.</summary>
internal sealed class WindowsSessionImageExporter(
    IWin32Window owner, Func<Func<Task>, Task> dispatch, Func<string> language) : ISessionImageExporter
{
    public async Task<bool> SaveAsync(string pngDataUrl, string fileName)
    {
        var bytes = DecodePng(pngDataUrl);
        string? destination = null;
        await dispatch(() =>
        {
            using var dialog = new SaveFileDialog
            {
                Title = AppText.Translate("Session als Bild speichern", language()),
                Filter = "PNG (*.png)|*.png",
                DefaultExt = "png",
                AddExtension = true,
                OverwritePrompt = true,
                FileName = Path.GetFileName(fileName),
                RestoreDirectory = true,
            };
            if (dialog.ShowDialog(owner) == DialogResult.OK) destination = dialog.FileName;
            return Task.CompletedTask;
        });
        if (destination is null) return false;

        // Complete the image before replacing a user-selected existing file.
        var temporary = Path.Combine(Path.GetDirectoryName(destination)!, $".grindcrest-{Guid.NewGuid():N}.tmp");
        try
        {
            await File.WriteAllBytesAsync(temporary, bytes);
            File.Move(temporary, destination, overwrite: true);
        }
        finally
        {
            try { File.Delete(temporary); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
        return true;
    }

    public Task CopyAsync(string pngDataUrl)
    {
        var bytes = DecodePng(pngDataUrl);
        return dispatch(() =>
        {
            using var stream = new MemoryStream(bytes, writable: false);
            using var image = Image.FromStream(stream);
            using var bitmap = new Bitmap(image);
            var data = new DataObject();
            data.SetData(DataFormats.Bitmap, true, bitmap);
            // Apps that understand PNG can paste the original lossless image.
            data.SetData("PNG", false, stream);
            stream.Position = 0;
            Clipboard.SetDataObject(data, copy: true, retryTimes: 5, retryDelay: 100);
            return Task.CompletedTask;
        });
    }

    internal static byte[] DecodePng(string dataUrl)
    {
        const string prefix = "data:image/png;base64,";
        const int maximumBytes = 24 * 1024 * 1024;
        if (!dataUrl.StartsWith(prefix, StringComparison.Ordinal) || dataUrl.Length > prefix.Length + maximumBytes / 3 * 4)
            throw new InvalidDataException("Ungültiges Session-Bild.");
        byte[] bytes;
        try { bytes = Convert.FromBase64String(dataUrl[prefix.Length..]); }
        catch (FormatException error) { throw new InvalidDataException("Ungültiges Session-Bild.", error); }
        ReadOnlySpan<byte> signature = [137, 80, 78, 71, 13, 10, 26, 10];
        if (bytes.Length < 33 || !bytes.AsSpan(0, 8).SequenceEqual(signature) ||
            !bytes.AsSpan(12, 4).SequenceEqual("IHDR"u8))
            throw new InvalidDataException("Ungültiges Session-Bild.");
        var width = BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(16, 4));
        var height = BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(20, 4));
        if (width is 0 or > 16384 || height is 0 or > 16384 || (long)width * height > 32_000_000)
            throw new InvalidDataException("Das Session-Bild ist zu groß.");
        using var stream = new MemoryStream(bytes, writable: false);
        using var image = Image.FromStream(stream, useEmbeddedColorManagement: false, validateImageData: true);
        return bytes;
    }
}
