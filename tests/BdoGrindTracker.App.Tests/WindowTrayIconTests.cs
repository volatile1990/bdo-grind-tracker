using System.Runtime.ExceptionServices;
using BdoGrindTracker.App.Services;
using BdoGrindTracker.App.UI;

namespace BdoGrindTracker.App.Tests;

public sealed class WindowTrayIconTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MinimizeUsesTheConfiguredBehaviorWithoutRecreatingTheWindow(bool minimizeToTray)
    {
        WithWindow(form =>
        {
            var preferences = new TrackerPreferences { MinimizeToTray = minimizeToTray };
            var saves = 0;
            using var tray = new WindowTrayIcon(form, () => preferences, () => saves++, () => true);
            var handle = form.Handle;
            var resizes = 0;
            form.Resize += (_, _) => resizes++;

            form.WindowState = FormWindowState.Minimized;
            Application.DoEvents();

            Assert.Equal(FormWindowState.Minimized, form.WindowState);
            Assert.True(resizes > 0, "The shown native form must deliver its minimize Resize event.");
            Assert.Equal(!minimizeToTray, form.Visible);
            Assert.Equal(minimizeToTray ? 1 : 0, saves);
            Assert.False(form.IsDisposed);
            Assert.Equal(handle, form.Handle);
        });
    }

    [Theory]
    [InlineData(FormWindowState.Normal)]
    [InlineData(FormWindowState.Maximized)]
    public void RepeatedMinimizeAndRestorePreservesStateAndNormalBounds(FormWindowState state)
    {
        WithWindow(form =>
        {
            var normalBounds = form.Bounds;
            form.WindowState = state;
            Application.DoEvents();
            var preferences = new TrackerPreferences { MinimizeToTray = true };
            using var tray = new WindowTrayIcon(form, () => preferences, () => { }, () => true);
            var handle = form.Handle;

            for (var cycle = 0; cycle < 3; cycle++)
            {
                form.WindowState = FormWindowState.Minimized;
                Application.DoEvents();
                Assert.False(form.Visible);

                tray.RestoreWindow();
                Application.DoEvents();

                Assert.True(form.Visible);
                Assert.Equal(state, form.WindowState);
                Assert.Equal(normalBounds, state == FormWindowState.Normal ? form.Bounds : form.RestoreBounds);
                Assert.Equal(handle, form.Handle);
            }

            form.WindowState = FormWindowState.Normal;
            Assert.Equal(normalBounds, form.Bounds);
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void UserCloseEitherDisposesOrHidesTheWindow(bool closeToTray)
    {
        WithWindow(form =>
        {
            var preferences = new TrackerPreferences { CloseToTray = closeToTray, CloseBehaviorConfigured = true };
            var saves = 0;
            using var tray = new WindowTrayIcon(form, () => preferences, () => saves++, () => true);
            CloseReason? reason = null;
            form.FormClosing += (_, e) =>
            {
                reason = e.CloseReason;
                e.Cancel = tray.TryHideOnClose(e.CloseReason);
            };

            form.Close();
            Application.DoEvents();

            Assert.Equal(CloseReason.UserClosing, reason);
            Assert.False(form.Visible);
            Assert.Equal(!closeToTray, form.IsDisposed);
            Assert.Equal(closeToTray ? 1 : 0, saves);
            if (closeToTray)
            {
                tray.RestoreWindow();
                Assert.True(form.Visible);
                Assert.Equal(FormWindowState.Normal, form.WindowState);
            }
        });
    }

    [Theory]
    [InlineData(CloseReason.None)]
    [InlineData(CloseReason.WindowsShutDown)]
    [InlineData(CloseReason.MdiFormClosing)]
    [InlineData(CloseReason.TaskManagerClosing)]
    [InlineData(CloseReason.FormOwnerClosing)]
    [InlineData(CloseReason.ApplicationExitCall)]
    public void NonUserCloseReasonsBypassCloseToTray(CloseReason reason)
    {
        WithWindow(form =>
        {
            var preferences = new TrackerPreferences { CloseToTray = true, CloseBehaviorConfigured = true };
            var saves = 0;
            using var tray = new WindowTrayIcon(form, () => preferences, () => saves++, () => true);

            // Exercise the close decision without asking Windows or the test host to exit.
            Assert.False(tray.TryHideOnClose(reason));
            Assert.True(form.Visible);
            Assert.Equal(0, saves);
        });
    }

    [Fact]
    public void ExplicitExitClosesAnAlreadyHiddenWindowDespiteCloseToTray()
    {
        WithWindow(form =>
        {
            var preferences = new TrackerPreferences { CloseToTray = true, CloseBehaviorConfigured = true };
            using var tray = new WindowTrayIcon(form, () => preferences, () => { }, () => true);
            var cancellations = new List<bool>();
            form.FormClosing += (_, e) =>
            {
                e.Cancel = tray.TryHideOnClose(e.CloseReason);
                cancellations.Add(e.Cancel);
            };
            form.Close();
            Assert.False(form.Visible);
            Assert.False(form.IsDisposed);

            tray.RequestExit();
            Application.DoEvents();

            Assert.Equal(new[] { true, false }, cancellations);
            Assert.True(form.IsDisposed);
        });
    }

    [Fact]
    public void FailedExitRestoresTheWindowAndReenablesCloseToTray()
    {
        WithWindow(form =>
        {
            var preferences = new TrackerPreferences { MinimizeToTray = true, CloseToTray = true, CloseBehaviorConfigured = true };
            using var tray = new WindowTrayIcon(form, () => preferences, () => { }, () => true);
            var shutdownAttempts = 0;
            form.FormClosing += (_, e) =>
            {
                e.Cancel = true;
                if (!tray.TryHideOnClose(e.CloseReason)) shutdownAttempts++;
            };
            var normalBounds = form.Bounds;
            form.WindowState = FormWindowState.Minimized;
            Assert.False(form.Visible);

            // Model the main form's canceled asynchronous close and failed durable save.
            tray.RequestExit();
            Assert.Equal(1, shutdownAttempts);
            Assert.False(form.IsDisposed);
            tray.CancelExit();
            Application.DoEvents();

            Assert.True(form.Visible);
            Assert.Equal(FormWindowState.Normal, form.WindowState);
            Assert.Equal(normalBounds, form.Bounds);

            form.Close();
            Assert.False(form.Visible);
            Assert.False(form.IsDisposed);
            Assert.Equal(1, shutdownAttempts);

            // An explicit retry can still reach the shutdown path.
            tray.RequestExit();
            Assert.Equal(2, shutdownAttempts);
        });
    }

    [Fact]
    public void ChangedPreferencesApplyToTheNextMinimizeAndClose()
    {
        WithWindow(form =>
        {
            var preferences = new TrackerPreferences();
            using var tray = new WindowTrayIcon(form, () => preferences, () => { }, () => true);
            form.FormClosing += (_, e) => e.Cancel = tray.TryHideOnClose(e.CloseReason);

            form.WindowState = FormWindowState.Minimized;
            Assert.True(form.Visible);
            tray.RestoreWindow();

            preferences = preferences with { MinimizeToTray = true, CloseToTray = true, CloseBehaviorConfigured = true };
            form.WindowState = FormWindowState.Minimized;
            Assert.False(form.Visible);
            tray.RestoreWindow();
            form.Close();
            Assert.False(form.Visible);
            Assert.False(form.IsDisposed);
            tray.RestoreWindow();

            preferences = preferences with { MinimizeToTray = false, CloseToTray = false };
            form.WindowState = FormWindowState.Minimized;
            Assert.True(form.Visible);
            tray.RestoreWindow();
            form.Close();
            Assert.True(form.IsDisposed);
        });
    }

    [Fact]
    public void ShutdownOrUpdateGuardPreventsHidingUntilTheWindowIsAvailableAgain()
    {
        WithWindow(form =>
        {
            var preferences = new TrackerPreferences { MinimizeToTray = true, CloseToTray = true, CloseBehaviorConfigured = true };
            var canHide = false;
            var saves = 0;
            using var tray = new WindowTrayIcon(form, () => preferences, () => saves++, () => canHide);

            form.WindowState = FormWindowState.Minimized;
            Assert.True(form.Visible);
            Assert.False(tray.TryHideOnClose(CloseReason.UserClosing));
            Assert.Equal(0, saves);
            tray.RestoreWindow();

            canHide = true;
            Assert.True(tray.TryHideOnClose(CloseReason.UserClosing));
            Assert.False(form.Visible);
            Assert.Equal(1, saves);
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FirstUserCloseConfiguresOnceAndImmediatelyUsesTheSavedChoice(bool closeToTray)
    {
        WithWindowAsync(async form =>
        {
            var preferences = new TrackerPreferences { CloseToTray = !closeToTray };
            var prompts = 0;
            var saves = 0;
            using var tray = new WindowTrayIcon(form, () => preferences, () => saves++, () => true);
            async Task<bool> Configure()
            {
                prompts++;
                await Task.Yield();
                preferences = preferences with { CloseToTray = closeToTray, CloseBehaviorConfigured = true };
                return true;
            }

            var cancelClose = await tray.HandleCloseAsync(CloseReason.UserClosing, Configure);

            Assert.Equal(closeToTray, cancelClose);
            Assert.Equal(!closeToTray, form.Visible);
            Assert.True(preferences.CloseBehaviorConfigured);
            Assert.Equal(1, prompts);
            Assert.Equal(closeToTray ? 1 : 0, saves);

            tray.RestoreWindow();
            Assert.Equal(closeToTray, await tray.HandleCloseAsync(CloseReason.UserClosing, Configure));
            Assert.Equal(1, prompts);
            Assert.Equal(closeToTray ? 2 : 0, saves);
        });
    }

    [Fact]
    public void UnconfiguredTrayValueCannotBypassTheFirstCloseChoice()
    {
        WithWindow(form =>
        {
            var preferences = new TrackerPreferences { CloseToTray = true };
            using var tray = new WindowTrayIcon(form, () => preferences, () => { }, () => true);

            Assert.False(tray.TryHideOnClose(CloseReason.UserClosing));
            Assert.True(form.Visible);
        });
    }

    [Fact]
    public void CanceledOrUnsuccessfullySavedChoiceKeepsWindowOpenAndAsksAgain()
    {
        WithWindowAsync(async form =>
        {
            var preferences = new TrackerPreferences { CloseToTray = true };
            var prompts = 0;
            var saves = 0;
            using var tray = new WindowTrayIcon(form, () => preferences, () => saves++, () => true);
            async Task<bool> Configure()
            {
                prompts++;
                await Task.Yield();
                // Both cancel and a failed durable preference save report false.
                return false;
            }

            for (var attempt = 1; attempt <= 2; attempt++)
            {
                Assert.True(await tray.HandleCloseAsync(CloseReason.UserClosing, Configure));
                Assert.Equal(attempt, prompts);
                Assert.True(form.Visible);
                Assert.False(form.IsDisposed);
                Assert.False(preferences.CloseBehaviorConfigured);
                Assert.Equal(0, saves);
            }
        });
    }

    [Fact]
    public void SuccessfulCallbackWithoutDurablyConfiguredChoiceKeepsTheWindowOpen()
    {
        WithWindowAsync(async form =>
        {
            var preferences = new TrackerPreferences { CloseToTray = true };
            var prompts = 0;
            var saves = 0;
            using var tray = new WindowTrayIcon(form, () => preferences, () => saves++, () => true);
            async Task<bool> Configure()
            {
                prompts++;
                await Task.Yield();
                return true;
            }

            Assert.True(await tray.HandleCloseAsync(CloseReason.UserClosing, Configure));
            Assert.True(form.Visible);
            Assert.False(preferences.CloseBehaviorConfigured);
            Assert.Equal(0, saves);

            Assert.True(await tray.HandleCloseAsync(CloseReason.UserClosing, Configure));
            Assert.Equal(2, prompts);
        });
    }

    [Fact]
    public void AnotherCloseWhileChoiceIsPendingCancelsWithoutASecondPrompt()
    {
        WithWindowAsync(async form =>
        {
            var preferences = new TrackerPreferences();
            var completion = new TaskCompletionSource<bool>();
            var prompts = 0;
            using var tray = new WindowTrayIcon(form, () => preferences, () => { }, () => true);
            Task<bool> Configure() { prompts++; return completion.Task; }

            var firstClose = tray.HandleCloseAsync(CloseReason.UserClosing, Configure);
            Assert.False(firstClose.IsCompleted);
            Assert.True(await tray.HandleCloseAsync(CloseReason.UserClosing, Configure));
            Assert.Equal(1, prompts);
            Assert.True(form.Visible);

            preferences = preferences with { CloseToTray = true, CloseBehaviorConfigured = true };
            completion.SetResult(true);
            Assert.True(await firstClose);
            Assert.False(form.Visible);
            Assert.Equal(1, prompts);
        });
    }

    [Theory]
    [InlineData(CloseReason.WindowsShutDown)]
    [InlineData(CloseReason.ApplicationExitCall)]
    [InlineData(CloseReason.TaskManagerClosing)]
    public void SystemOrApplicationCloseDoesNotAskForAnInitialChoice(CloseReason reason)
    {
        WithWindowAsync(async form =>
        {
            var preferences = new TrackerPreferences();
            using var tray = new WindowTrayIcon(form, () => preferences, () => { }, () => true);
            var prompts = 0;
            Task<bool> Configure() { prompts++; return Task.FromResult(false); }

            Assert.False(await tray.HandleCloseAsync(reason, Configure));
            Assert.Equal(0, prompts);
            Assert.True(form.Visible);
            Assert.False(preferences.CloseBehaviorConfigured);
        });
    }

    [Theory]
    [InlineData(CloseReason.WindowsShutDown)]
    [InlineData(CloseReason.ApplicationExitCall)]
    public void SystemCloseBypassesPendingChoiceAndLateCompletionCannotHideDuringShutdown(CloseReason reason)
    {
        WithWindowAsync(async form =>
        {
            var preferences = new TrackerPreferences();
            var completion = new TaskCompletionSource<bool>();
            var canHide = true;
            var prompts = 0;
            var saves = 0;
            using var tray = new WindowTrayIcon(form, () => preferences, () => saves++, () => canHide);
            Task<bool> Configure() { prompts++; return completion.Task; }

            var firstClose = tray.HandleCloseAsync(CloseReason.UserClosing, Configure);
            Assert.False(firstClose.IsCompleted);
            Assert.False(await tray.HandleCloseAsync(reason, Configure));
            // The main form now starts its durable shutdown and closes the hide gate.
            canHide = false;
            preferences = preferences with { CloseToTray = true, CloseBehaviorConfigured = true };
            completion.SetResult(true);

            Assert.True(await firstClose);
            Assert.Equal(1, prompts);
            Assert.True(form.Visible);
            Assert.Equal(0, saves);
        });
    }

    [Fact]
    public void ExplicitExitBypassesPendingChoiceAndLateCompletionLeavesDisposedWindowAlone()
    {
        WithWindowAsync(async form =>
        {
            var preferences = new TrackerPreferences();
            var completion = new TaskCompletionSource<bool>();
            var prompts = 0;
            var saves = 0;
            using var tray = new WindowTrayIcon(form, () => preferences, () => saves++, () => true);
            Task<bool> Configure() { prompts++; return completion.Task; }
            form.FormClosing += async (_, e) =>
            {
                e.Cancel = true;
                e.Cancel = await tray.HandleCloseAsync(e.CloseReason, Configure);
            };

            var firstClose = tray.HandleCloseAsync(CloseReason.UserClosing, Configure);
            Assert.False(firstClose.IsCompleted);
            tray.RequestExit();
            Assert.True(form.IsDisposed);

            preferences = preferences with { CloseToTray = true, CloseBehaviorConfigured = true };
            completion.SetResult(true);

            Assert.True(await firstClose);
            Assert.Equal(1, prompts);
            Assert.Equal(0, saves);
            Assert.True(form.IsDisposed);
        });
    }

    [Fact]
    public void DisposingTheTrayDuringConfigurationPreventsLateWindowChanges()
    {
        WithWindowAsync(async form =>
        {
            var preferences = new TrackerPreferences();
            var completion = new TaskCompletionSource<bool>();
            var saves = 0;
            using var tray = new WindowTrayIcon(form, () => preferences, () => saves++, () => true);
            var firstClose = tray.HandleCloseAsync(CloseReason.UserClosing, () => completion.Task);
            Assert.False(firstClose.IsCompleted);

            tray.Dispose();
            preferences = preferences with { CloseToTray = true, CloseBehaviorConfigured = true };
            completion.SetResult(true);

            Assert.True(await firstClose);
            Assert.True(form.Visible);
            Assert.Equal(0, saves);
        });
    }

    private static void WithWindowAsync(Func<Form, Task> action)
    {
        WithWindow(form =>
        {
            var operation = action(form);
            var deadline = Environment.TickCount64 + 5000;
            while (!operation.IsCompleted)
            {
                Assert.True(Environment.TickCount64 < deadline, "The asynchronous tray decision did not complete.");
                Application.DoEvents();
                Thread.Sleep(1);
            }
            operation.GetAwaiter().GetResult();
        });
    }

    private static void WithWindow(Action<Form> action)
    {
        Exception? error = null;
        var thread = new Thread(() =>
        {
            try
            {
                using var icon = (Icon)SystemIcons.Application.Clone();
                using var form = new NonActivatingTestForm { Icon = icon };
                form.Show();
                Application.DoEvents();
                action(form);
            }
            catch (Exception exception) { error = exception; }
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(10)), "The tray window test did not finish on its STA thread.");
        if (error is not null) ExceptionDispatchInfo.Capture(error).Throw();
    }

    private sealed class NonActivatingTestForm : Form
    {
        public NonActivatingTestForm()
        {
            // A real HWND receives the same minimize/restore events as the app.
            // Transparency keeps maximization invisible; disabling prevents Activate()
            // from taking focus. Use valid on-screen bounds so Windows need not repair
            // an off-screen restore rectangle, which would obscure placement regressions.
            Text = "Grindcrest tray test";
            StartPosition = FormStartPosition.Manual;
            AutoScaleMode = AutoScaleMode.None;
            ShowInTaskbar = false;
            Opacity = 0;
            Enabled = false;
            var work = Screen.PrimaryScreen!.WorkingArea;
            Bounds = new Rectangle(work.Left + 30, work.Top + 30, 320, 240);
        }

        protected override bool ShowWithoutActivation => true;

        protected override CreateParams CreateParams
        {
            get
            {
                var parameters = base.CreateParams;
                parameters.ExStyle |= 0x08000000; // WS_EX_NOACTIVATE
                return parameters;
            }
        }
    }
}
