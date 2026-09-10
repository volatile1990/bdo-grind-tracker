namespace BdoGrindTracker.App.Overlay;

/// <summary>One selection and geometry calculation for the editor and the real desktop window.</summary>
public sealed record OverlayLootView(
    string Label, string Detail,
    IReadOnlyList<OverlayLootItem> Items, IReadOnlyList<OverlayLootItem> VisibleItems,
    int HiddenCount, int Columns, double CellWidth, double CellHeight,
    double HeaderHeight, double FooterHeight);

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
        var width = Math.Max(0, widget.Width - PaddingX * 2);
        var innerHeight = Math.Max(0, widget.Height - PaddingY * 2);
        var header = widget.ShowLabel ? Math.Min(innerHeight, 18 * widget.FontScale) : 0;
        var height = Math.Max(0, innerHeight - header);
        var view = widget.Kind == "drop-item" ? "card" : widget.ItemView;
        var cellWidth = view is "list" or "card" ? width : widget.ItemSize;
        var cellHeight = view == "list" ? Math.Max(24, Math.Max(widget.ItemSize * .55, 20 * widget.FontScale)) : widget.ItemSize;
        var columns = view is "list" or "card" ? 1 : Math.Max(0, (int)((width + Gap) / (cellWidth + Gap)));
        int Capacity(double availableHeight)
        {
            if (width <= 0 || availableHeight <= 0) return 0;
            if (view == "card") return availableHeight >= 20 ? 1 : 0;
            var rows = Math.Max(0, (int)((availableHeight + Gap) / (cellHeight + Gap)));
            if (view == "strip") rows = Math.Min(1, rows);
            return columns * rows;
        }
        var count = Math.Min(Math.Clamp(widget.ItemLimit, 1, 24), Capacity(height));
        var footer = items.Count > count ? Math.Min(18, innerHeight) : 0d;
        header = Math.Min(header, Math.Max(0, innerHeight - footer));
        height = Math.Max(0, innerHeight - header);
        if (footer > 0) count = Math.Min(count, Capacity(Math.Max(0, height - footer)));
        if (view == "card") cellHeight = Math.Max(0, height - footer);
        var visible = Array.AsReadOnly(items.Take(count).ToArray());
        var label = filter switch
        {
            "rare" => "Seltene Drops · Live-Session",
            "trash" => "Trashloot · Live-Session",
            _ when widget.Kind == "drop-item" => "Live-Session",
            _ => "Drops · Live-Session",
        };
        return new(label, "Gesammelte Mengen der Live-Session, einschließlich manueller Korrekturen.",
            items, visible, items.Count - visible.Count, columns, cellWidth, cellHeight, header, footer);
    }
}
