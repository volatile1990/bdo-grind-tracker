using BdoGrindTracker.App.Localization;

namespace BdoGrindTracker.App.UI;

internal static class WindowClosePrompt
{
    public static bool? Show(IWin32Window owner, string language)
    {
        string T(string text) => AppText.Translate(text, language);
        var exit = new TaskDialogCommandLinkButton(T("Programm beenden"),
            T("Grindcrest vollständig schließen und die Session speichern."));
        var tray = new TaskDialogCommandLinkButton(T("In den Tray minimieren"),
            T("Das Fenster verbergen und die Erfassung im Hintergrund weiterlaufen lassen."));
        var cancel = new TaskDialogButton(T("Abbrechen"));
        var page = new TaskDialogPage
        {
            Caption = AppBranding.Name,
            Heading = T("Was soll beim Schließen passieren?"),
            Text = T("Deine Auswahl wird gespeichert und gilt künftig für das X. Du kannst sie unter Einstellungen → Fenster & Tray ändern."),
            Buttons = { exit, tray, cancel },
            DefaultButton = cancel,
            AllowCancel = true,
        };
        var result = TaskDialog.ShowDialog(owner, page);
        return result == tray ? true : result == exit ? false : null;
    }
}
