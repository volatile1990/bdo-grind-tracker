using System.Runtime.InteropServices;

namespace BdoGrindTracker.App.UI;

internal static class BdoWindowChrome
{
    private const int DwmUseImmersiveDarkMode = 20;
    private const int DwmWindowCornerPreference = 33;
    private const int RoundCornerPreference = 2;
    private const uint WdaNone = 0;

    public static void Apply(Form form)
    {
        if (!OperatingSystem.IsWindows() || !form.IsHandleCreated)
        {
            return;
        }

        try
        {
            var enabled = 1;
            _ = DwmSetWindowAttribute(
                form.Handle,
                DwmUseImmersiveDarkMode,
                ref enabled,
                sizeof(int));
            var cornerPreference = RoundCornerPreference;
            _ = DwmSetWindowAttribute(
                form.Handle,
                DwmWindowCornerPreference,
                ref cornerPreference,
                sizeof(int));

            // Keep ordinary screenshot behavior. WDA_EXCLUDEFROMCAPTURE also removes
            // this window from Snipping Tool's frozen desktop, making it look hidden.
            // Do not apply global capture exclusion just to style the window chrome.
            _ = SetWindowDisplayAffinity(form.Handle, WdaNone);
        }
        catch (DllNotFoundException)
        {
            // Older Windows versions keep the ordinary title bar.
        }
        catch (EntryPointNotFoundException)
        {
            // Older Windows versions keep the ordinary title bar.
        }
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(
        IntPtr windowHandle,
        int attribute,
        ref int attributeValue,
        int attributeSize);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowDisplayAffinity(
        IntPtr windowHandle,
        uint affinity);
}
