namespace BdoGrindTracker.App.UI;

internal static class CapturePermissionExplanation
{
    internal static bool Show(IWin32Window owner)
    {
        var continueButton = new TaskDialogButton("Weiter zur Windows-Abfrage");
        var page = new TaskDialogPage
        {
            Caption = AppBranding.Name,
            Heading = "Loot erkennen – ohne gelben Rahmen",
            Text = "Grindcrest lässt Windows das Black-Desert-Fenster aufnehmen, um deinen Loot daraus zu erkennen.\n\n" +
                "Windows kennzeichnet diese Fensteraufnahme normalerweise mit einem gelben Rahmen um das Spielfenster. " +
                "Die Windows-Abfrage erlaubt Grindcrest, diesen Aufnahmehinweis auszublenden.\n\n" +
                "Erlaube dort das Ausblenden des Rahmens, wenn du ohne gelben Rahmen spielen möchtest. " +
                "Wenn du ablehnst, funktioniert das Tracking weiterhin; Windows kann dann den gelben Rahmen anzeigen.",
            Icon = TaskDialogIcon.Information,
            AllowCancel = true,
            DefaultButton = continueButton,
            Buttons = { continueButton, new TaskDialogButton("Abbrechen") },
        };
        return TaskDialog.ShowDialog(owner, page) == continueButton;
    }
}
