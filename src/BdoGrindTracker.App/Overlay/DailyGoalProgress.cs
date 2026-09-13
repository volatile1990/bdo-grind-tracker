namespace BdoGrindTracker.App.Overlay;

public sealed record DailyGoalProgress(decimal Earned = 0, decimal? Target = null, string? Error = null)
{
    public double Fraction => Target is > 0 ? (double)Math.Clamp(Earned / Target.Value, 0, 1) : 0;
    public string Percentage => Error is null && Target is > 0
        ? (Math.Max(0, Earned) / Target.Value * 100).ToString("0", System.Globalization.CultureInfo.GetCultureInfo("de-DE")) + " %" : "—";
    public string Value => Error is not null ? "Ziel nicht verfügbar" : Target is > 0
        ? Components.Presentation.Silver(Earned) + " / " + Components.Presentation.Silver(Target.Value) : "Kein Tagesziel";
    public string Detail => Error ?? (Target is not > 0 ? "In Grind Goals festlegen" : Earned >= Target.Value ? "Tagesziel erreicht" : "Silber netto · heute");
}
