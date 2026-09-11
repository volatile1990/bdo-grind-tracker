namespace BdoGrindTracker.Core;

/// <summary>Names used to interpret recorded text, independent of installed item catalogs.</summary>
public sealed record LifetimeParsingCatalogEntry
{
    public string Name { get; }
    public IReadOnlyList<string> Aliases { get; }
    public bool IsFixedUnit { get; }

    public LifetimeParsingCatalogEntry(string name, IReadOnlyList<string> aliases, bool isFixedUnit = false)
    {
        ArgumentNullException.ThrowIfNull(aliases);
        if (string.IsNullOrWhiteSpace(name) || name.Length > 2048 || aliases.Count > 16 ||
            aliases.Any(alias => string.IsNullOrWhiteSpace(alias) || alias.Length > 2048))
            throw new ArgumentException("Invalid lifetime parsing catalog entry.");
        Name = name;
        Aliases = Array.AsReadOnly(aliases.ToArray());
        IsFixedUnit = isFixedUnit;
    }
}

/// <summary>An immutable versioned snapshot of the currently permitted normal loot names.</summary>
public sealed record LifetimeParsingContext
{
    public long Revision { get; }
    public IReadOnlyList<LifetimeParsingCatalogEntry> Catalog { get; }

    public LifetimeParsingContext(long revision, IReadOnlyList<LifetimeParsingCatalogEntry> catalog)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        Revision = revision;
        Catalog = Array.AsReadOnly(catalog.ToArray());
        Validate();
    }

    public void Validate()
    {
        if (Revision < 0 || Catalog.Count is 0 or > 1024 || Catalog.Any(entry => entry is null) ||
            Catalog.Select(entry => entry.Name).Distinct(StringComparer.Ordinal).Count() != Catalog.Count)
            throw new ArgumentException("Invalid lifetime parsing context.");
    }

    public bool HasSameCatalog(LifetimeParsingContext other) =>
        Catalog.Count == other.Catalog.Count && Catalog.Zip(other.Catalog).All(pair =>
            pair.First.Name == pair.Second.Name && pair.First.IsFixedUnit == pair.Second.IsFixedUnit &&
            pair.First.Aliases.SequenceEqual(pair.Second.Aliases, StringComparer.Ordinal));
}
