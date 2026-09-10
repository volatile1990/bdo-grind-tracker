namespace BdoGrindTracker.App.Overlay;

/// <summary>One selection and geometry calculation for the editor and the real desktop window.</summary>
public sealed record OverlayLootView(
    string Label, string Detail,
    IReadOnlyList<OverlayLootItem> Items, IReadOnlyList<OverlayLootItem> VisibleItems,
    int HiddenCount, int Columns, double CellWidth, double CellHeight,
    double HeaderHeight, double FooterHeight,
    double ItemSize, double FontScale, double Gap);

public static class OverlayLootPresentation
{
    public const double PaddingX = 10;
    public const double PaddingY = 8;
    public const double Gap = 4;

    public static OverlayLootView Create(OverlayWidget widget, OverlaySnapshot snapshot)
    {
        var source = snapshot.Drops;
        var filter = widget.Kind == "drop-item" ? "selected" : widget.Kind == "rare-drops" ? "rare" : widget.ItemFilter;
        IEnumerable<OverlayLootItem> selection = source;
        if (filter == "selected")
        {
            // Explicitly selected items keep their slots even before their first drop.
            // There is no independent timer, counter or rate conversion here.
            var actual = source.ToDictionary(item => item.CanonicalName, StringComparer.OrdinalIgnoreCase);
            var catalog = snapshot.ItemCatalog.ToDictionary(item => item.CanonicalName, StringComparer.OrdinalIgnoreCase);
            selection = widget.ItemNames.Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(widget.Kind == "drop-item" ? 1 : 24)
                .Select(name => actual.GetValueOrDefault(name) ??
                    (catalog.GetValueOrDefault(name) is { } item ? item with { Quantity = 0, QuantityText = "0" } :
                        new OverlayLootItem(name, name, "0")));
        }
        else if (filter == "rare") selection = source.Where(item => item.IsRare);
        else if (filter == "trash") selection = source.Where(item => item.IsTrash);

        selection = widget.ItemSort switch
        {
            "quantity" => selection.OrderByDescending(item => item.Quantity).ThenBy(item => item.CanonicalName, StringComparer.Ordinal),
            "name" => selection.OrderBy(item => item.Name, StringComparer.CurrentCultureIgnoreCase),
            _ when filter != "selected" => selection.OrderByDescending(item => item.IsTrash).ThenBy(item => item.Name, StringComparer.CurrentCultureIgnoreCase),
            _ => selection,
        };
        var items = Array.AsReadOnly(selection.ToArray());
        var view = widget.Kind == "drop-item" ? "card" : widget.ItemView;
        var count = Math.Min(items.Count, view == "card" ? 1 : Math.Clamp(widget.ItemLimit, 1, 24));
        var visible = Array.AsReadOnly(items.Take(count).ToArray());
        var hiddenCount = items.Count - count;
        var width = Math.Max(0, widget.Width - PaddingX * 2);
        var innerHeight = Math.Max(0, widget.Height - PaddingY * 2);
        var itemSize = widget.ItemSize;
        var fontScale = widget.FontScale;
        var header = widget.ShowLabel ? 18 * fontScale : 0;
        var footer = hiddenCount > 0 ? 18 * fontScale : 0;
        var cellHeight = view switch
        {
            "list" => Math.Max(24, Math.Max(itemSize * .55, 20 * fontScale)),
            "card" => Math.Max(widget.ShowIcon ? itemSize : 0, 62 * fontScale),
            _ => itemSize,
        };
        // Lists/cards need room beside the icon for the name and quantity. The
        // renderers fit the actual text; this reserves a useful shared layout.
        var preferredWidth = view switch
        {
            "list" => (widget.ShowIcon ? itemSize * .55 + 6 : 0) + 80 * fontScale,
            "card" => (widget.ShowIcon ? itemSize + 12 : 0) + 64 * fontScale,
            _ => itemSize,
        };
        var layoutCount = Math.Max(1, count);
        var columns = view switch
        {
            "list" or "card" => 1,
            "strip" => layoutCount,
            _ => Math.Clamp((int)Math.Floor((width + Gap) / (itemSize + Gap)), 1, layoutCount),
        };
        double Fit(int candidateColumns)
        {
            var rows = (layoutCount + candidateColumns - 1) / candidateColumns;
            var requiredWidth = candidateColumns * preferredWidth + (candidateColumns - 1) * Gap;
            var requiredHeight = header + footer + rows * cellHeight + (rows - 1) * Gap;
            return Math.Min(1, Math.Min(width / requiredWidth, innerHeight / requiredHeight));
        }
        var scale = Fit(columns);
        if (view is not ("list" or "card" or "strip") && scale < 1)
        {
            // Keep the preferred grid when it fits. Otherwise select the grid
            // with the largest uniformly scaled cells, retaining every slot.
            for (var candidate = 1; candidate <= layoutCount; candidate++)
            {
                var candidateScale = Fit(candidate);
                if (candidateScale <= scale) continue;
                columns = candidate;
                scale = candidateScale;
            }
        }
        header *= scale;
        footer *= scale;
        var cellWidth = view is "list" or "card" ? width : itemSize * scale;
        cellHeight = view == "card" ? Math.Max(0, innerHeight - header - footer) : cellHeight * scale;
        var label = filter switch
        {
            "rare" => "Seltene Drops · Live-Session",
            "trash" => "Trashloot · Live-Session",
            _ when widget.Kind == "drop-item" => "Live-Session",
            _ => "Drops · Live-Session",
        };
        return new(label, "Gesammelte Mengen der Live-Session, einschließlich manueller Korrekturen.",
            items, visible, hiddenCount, columns, cellWidth, cellHeight, header, footer,
            itemSize * scale, fontScale * scale, Gap * scale);
    }
}
