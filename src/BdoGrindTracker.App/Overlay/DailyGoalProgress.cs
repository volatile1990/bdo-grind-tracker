namespace BdoGrindTracker.App.Overlay;

public sealed record DailyGoalProgress(decimal Earned = 0, decimal? Target = null, string? Error = null)
{
    public string UiLanguage { get; init; } = "de";
    private string T(string source) => Localization.AppText.Translate(source, UiLanguage);
    public double Fraction => Target is not > 0 || Earned <= 0 ? 0 : Earned >= Target.Value ? 1 : (double)(Earned / Target.Value);
    public string Percentage => Error is null && Target is > 0
        // The ratio of two valid decimals can exceed decimal.MaxValue for a tiny goal.
        ? ((double)Math.Max(0, Earned) / (double)Target.Value * 100).ToString("0", Localization.AppText.Culture(UiLanguage)) + " %" : "—";
    public string Value => Error is not null ? T("Ziel nicht verfügbar") : Target is > 0
        ? Components.Presentation.Silver(Earned, UiLanguage) + " / " + Components.Presentation.Silver(Target.Value, UiLanguage) : T("Kein Tagesziel");
    public string Detail => Error is not null ? T(Error) : T(Target is not > 0 ? "In Grind Goals festlegen" : Earned >= Target.Value ? "Tagesziel erreicht" : "Silber netto · heute");
}
