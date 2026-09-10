using System.Text.Json;
using BdoGrindTracker.App.Persistence;

namespace BdoGrindTracker.App.Overlay;

internal sealed class OverlayTemplateStore(string? directory = null)
{
    internal const string FileName = "overlay-templates.json";
    internal const int MaximumTemplates = 30;
    private const int MaximumFileBytes = 8 * 1024 * 1024;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
    };
    private readonly string _path = Path.Combine(directory ?? AppDataPaths.Current.BaseDirectory, FileName);
    private bool _loaded;

    public string? LoadError { get; private set; }

    public IReadOnlyList<OverlayTemplate> Load()
    {
        _loaded = true;
        try
        {
            using var stream = File.OpenRead(_path);
            if (stream.Length > MaximumFileBytes) throw new InvalidDataException("Die Vorlagendatei ist zu groß.");
            var document = JsonSerializer.Deserialize<StoredDocument>(stream, JsonOptions);
            if (document is null || document.Version != 1 || document.Templates is null)
                throw new InvalidDataException("Das Format der Vorlagendatei wird nicht unterstützt.");
            var templates = document.Templates.Select(template =>
            {
                if (template is null || template.Layout is null || template.Layout.Widgets is null ||
                    !Guid.TryParse(template.Id, out var id))
                    throw new InvalidDataException("Eine gespeicherte Vorlage ist unvollständig.");
                return OverlayTemplate.Create(template.Name, template.Layout.ToSettings(), id.ToString("N"));
            }).ToArray();
            ValidateCollection(templates);
            LoadError = null;
            return Array.AsReadOnly(templates);
        }
        catch (Exception exception) when (exception is FileNotFoundException or DirectoryNotFoundException)
        {
            LoadError = null;
            return Array.Empty<OverlayTemplate>();
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException or JsonException or ArgumentException)
        {
            LoadError = "Eigene Vorlagen konnten nicht geladen werden. Die Datei bleibt unverändert. " + exception.Message;
            return Array.Empty<OverlayTemplate>();
        }
    }

    public void Save(IReadOnlyList<OverlayTemplate> templates)
    {
        if (!_loaded) Load();
        if (LoadError is not null) throw new InvalidDataException(LoadError);
        ValidateCollection(templates);
        var document = new StoredDocument
        {
            Version = 1,
            Templates = templates.Select(template => new StoredTemplate
            {
                Id = template.Id, Name = template.Name,
                Layout = StoredLayout.FromSettings(OverlayTemplate.CopyLayout(template.Layout)),
            }).ToArray(),
        };
        var bytes = JsonSerializer.SerializeToUtf8Bytes(document, JsonOptions);
        if (bytes.Length > MaximumFileBytes) throw new InvalidDataException("Die Vorlagen sind zusammen zu groß zum Speichern.");
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        var temporary = _path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                stream.Write(bytes);
                stream.Flush(flushToDisk: true);
            }
            File.Move(temporary, _path, overwrite: true);
        }
        finally
        {
            try { File.Delete(temporary); }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { }
        }
    }

    private static void ValidateCollection(IReadOnlyList<OverlayTemplate> templates)
    {
        ArgumentNullException.ThrowIfNull(templates);
        if (templates.Count > MaximumTemplates) throw new InvalidDataException($"Es sind höchstens {MaximumTemplates} eigene Vorlagen möglich.");
        var ids = new HashSet<Guid>();
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var template in templates)
        {
            if (template is null || !Guid.TryParse(template.Id, out var id) || !ids.Add(id) || template.Layout is null)
                throw new InvalidDataException("Die Vorlagen enthalten eine ungültige oder doppelte Kennung.");
            if (!names.Add(OverlayTemplate.NormalizeName(template.Name)))
                throw new InvalidDataException("Die Vorlagen enthalten doppelte Namen.");
        }
    }

    private sealed record StoredDocument
    {
        public required int Version { get; init; }
        public required StoredTemplate[] Templates { get; init; }
    }

    private sealed record StoredTemplate
    {
        public required string Id { get; init; }
        public required string Name { get; init; }
        public required StoredLayout Layout { get; init; }
    }

    // Keep behavior fields out of the file so a template cannot enable an overlay,
    // change capture exclusion, move a window, or silently replace its hotkeys.
    private sealed record StoredLayout
    {
        public required double Width { get; init; }
        public required double Height { get; init; }
        public required IReadOnlyList<OverlayWidget> Widgets { get; init; }
        public required double Scale { get; init; }
        public required double BackgroundOpacity { get; init; }
        public required bool ShowBorder { get; init; }
        public required bool SnapToGrid { get; init; }

        public OverlaySettings ToSettings() => new()
        {
            Width = Width, Height = Height, Widgets = Widgets,
            Scale = Scale, BackgroundOpacity = BackgroundOpacity,
            ShowBorder = ShowBorder, SnapToGrid = SnapToGrid,
        };

        public static StoredLayout FromSettings(OverlaySettings layout) => new()
        {
            Width = layout.Width, Height = layout.Height, Widgets = layout.Widgets,
            Scale = layout.Scale, BackgroundOpacity = layout.BackgroundOpacity,
            ShowBorder = layout.ShowBorder, SnapToGrid = layout.SnapToGrid,
        };
    }
}
