using System.Collections.Concurrent;
using BdoGrindTracker.App.Components;
using BdoGrindTracker.App.Services;

namespace BdoGrindTracker.App.Tests;

public sealed class WindowCloseDialogControllerTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    [Fact]
    public async Task UnavailableControllerDoesNotPresentOrSave()
    {
        var controller = new WindowCloseDialogController();
        var saves = 0;
        var changes = 0;
        controller.Changed += () => changes++;

        Assert.False(controller.IsAvailable);
        Assert.Null(await controller.AskAsync(_ => { saves++; return Success(); }).WaitAsync(Timeout));
        await controller.ShowErrorAsync("Title", "Message", "Detail").WaitAsync(Timeout);
        Assert.Null(controller.State);
        Assert.Equal(0, saves);
        Assert.Equal(0, changes);

        controller.Attach();
        Assert.True(controller.IsAvailable);
        controller.Detach();
        Assert.False(controller.IsAvailable);
        Assert.Null(await controller.AskAsync(_ => { saves++; return Success(); }).WaitAsync(Timeout));
        await controller.ShowErrorAsync("Title", "Message", null).WaitAsync(Timeout);
        Assert.Null(controller.State);
        Assert.Equal(0, saves);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ChoiceCompletesOnlyAfterDurableSaveSucceeds(bool closeToTray)
    {
        var controller = AvailableController();
        var save = Completion<TrackerCommandResult>();
        var saves = new List<bool>();
        var pending = controller.AskAsync(choice => { saves.Add(choice); return save.Task; });
        var state = Assert.IsType<WindowCloseDialogState>(controller.State);
        Assert.Equal(WindowCloseDialogKind.Choice, state.Kind);
        Assert.False(state.Saving);
        Assert.Null(state.Error);
        Assert.False(pending.IsCompleted);

        var choosing = controller.ChooseAsync(state.Id, closeToTray);

        Assert.Equal(new[] { closeToTray }, saves);
        Assert.True(controller.State!.Saving);
        Assert.False(pending.IsCompleted);
        Assert.False(choosing.IsCompleted);
        var duplicate = controller.AskAsync(_ => throw new InvalidOperationException("The original saver must be retained"));
        Assert.Same(pending, duplicate);
        await controller.ChooseAsync(state.Id, !closeToTray).WaitAsync(Timeout);
        Assert.Equal(new[] { closeToTray }, saves);
        save.SetResult(TrackerCommandResult.Success);
        await choosing.WaitAsync(Timeout);
        Assert.Equal((bool?)closeToTray, await pending.WaitAsync(Timeout));
        Assert.Null(controller.State);
        Assert.True(controller.IsAvailable);
    }

    [Fact]
    public async Task FailedSaveRetainsChoiceAndCanRetryWithTheOtherSelection()
    {
        var controller = AvailableController();
        var retry = Completion<TrackerCommandResult>();
        var saves = new List<bool>();
        var pending = controller.AskAsync(choice =>
        {
            saves.Add(choice);
            return saves.Count == 1 ? Task.FromResult(new TrackerCommandResult("Disk is full")) : retry.Task;
        });
        var id = controller.State!.Id;

        await controller.ChooseAsync(id, true).WaitAsync(Timeout);

        var failed = Assert.IsType<WindowCloseDialogState>(controller.State);
        Assert.Equal(id, failed.Id);
        Assert.Equal(WindowCloseDialogKind.Choice, failed.Kind);
        Assert.False(failed.Saving);
        Assert.Equal("Disk is full", failed.Error);
        Assert.False(pending.IsCompleted);

        var choosing = controller.ChooseAsync(id, false);
        Assert.True(controller.State!.Saving);
        Assert.Null(controller.State.Error);
        Assert.False(pending.IsCompleted);
        retry.SetResult(TrackerCommandResult.Success);
        await choosing.WaitAsync(Timeout);
        Assert.Equal(false, await pending.WaitAsync(Timeout));
        Assert.Equal(new[] { true, false }, saves);
        Assert.Null(controller.State);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task SynchronousOrAsynchronousSaveExceptionIsDisplayedAndSupportsRetry(bool synchronous)
    {
        var controller = AvailableController();
        var saves = 0;
        var pending = controller.AskAsync(_ =>
        {
            if (++saves > 1) return Success();
            var error = new IOException("Settings could not be written");
            if (synchronous) throw error;
            return Task.FromException<TrackerCommandResult>(error);
        });
        var id = controller.State!.Id;

        await controller.ChooseAsync(id, false).WaitAsync(Timeout);

        Assert.Equal("Settings could not be written", controller.State!.Error);
        Assert.False(controller.State.Saving);
        Assert.False(pending.IsCompleted);
        await controller.ChooseAsync(id, true).WaitAsync(Timeout);
        Assert.Equal(true, await pending.WaitAsync(Timeout));
        Assert.Equal(2, saves);
        Assert.Null(controller.State);
    }

    [Fact]
    public async Task DuplicateAskSharesTaskAndPreservesOriginalSaveCallback()
    {
        var controller = AvailableController();
        var firstSaves = 0;
        var ignoredSaves = 0;
        var pending = controller.AskAsync(_ => { firstSaves++; return Success(); });
        var state = controller.State;

        var duplicate = controller.AskAsync(_ => { ignoredSaves++; return Success(); });

        Assert.Same(pending, duplicate);
        Assert.Same(state, controller.State);
        await controller.ChooseAsync(state!.Id, true).WaitAsync(Timeout);
        Assert.Equal(true, await pending.WaitAsync(Timeout));
        Assert.Equal(1, firstSaves);
        Assert.Equal(0, ignoredSaves);
    }

    [Fact]
    public async Task ErrorPresentationCannotReplacePendingChoice()
    {
        var controller = AvailableController();
        var pending = controller.AskAsync(_ => Success());
        var state = controller.State;

        await controller.ShowErrorAsync("Other title", "Other message", "Other detail").WaitAsync(Timeout);

        Assert.Same(state, controller.State);
        Assert.False(pending.IsCompleted);
        controller.Dismiss(state!.Id);
        Assert.Null(await pending.WaitAsync(Timeout));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DismissOrCancelBeforeSelectionCompletesWithoutSaving(bool cancelPending)
    {
        var controller = AvailableController();
        var saves = 0;
        var pending = controller.AskAsync(_ => { saves++; return Success(); });
        var id = controller.State!.Id;

        if (cancelPending) controller.CancelPending();
        else controller.Dismiss(id);

        Assert.Null(await pending.WaitAsync(Timeout));
        Assert.Null(controller.State);
        Assert.Equal(0, saves);
        await controller.ChooseAsync(id, true).WaitAsync(Timeout);
        controller.Dismiss(id);
        controller.CancelPending();
        Assert.Equal(0, saves);
        Assert.True(controller.IsAvailable);
    }

    [Fact]
    public async Task DismissDuringSavingIsIgnored()
    {
        var controller = AvailableController();
        var save = Completion<TrackerCommandResult>();
        var pending = controller.AskAsync(_ => save.Task);
        var id = controller.State!.Id;
        var choosing = controller.ChooseAsync(id, false);
        var saving = controller.State;

        controller.Dismiss(id);
        await controller.ShowErrorAsync("Unrelated error", "Message", "Detail").WaitAsync(Timeout);

        Assert.Same(saving, controller.State);
        Assert.False(pending.IsCompleted);
        save.SetResult(TrackerCommandResult.Success);
        await choosing.WaitAsync(Timeout);
        Assert.Equal(false, await pending.WaitAsync(Timeout));
        Assert.Null(controller.State);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(0, 1)]
    [InlineData(0, 2)]
    [InlineData(1, 0)]
    [InlineData(1, 1)]
    [InlineData(1, 2)]
    [InlineData(2, 0)]
    [InlineData(2, 1)]
    [InlineData(2, 2)]
    public async Task ForcedCancellationIgnoresLateSaveSuccessFailureOrException(int cancellation, int lateResult)
    {
        var controller = AvailableController();
        var oldSave = Completion<TrackerCommandResult>();
        var oldSaves = 0;
        var oldPending = controller.AskAsync(_ => { oldSaves++; return oldSave.Task; });
        var oldId = controller.State!.Id;
        var oldChoosing = controller.ChooseAsync(oldId, true);

        if (cancellation == 0) controller.CancelPending();
        else if (cancellation == 1) controller.Detach();
        else controller.FailPresentation(oldId);

        Assert.Null(await oldPending.WaitAsync(Timeout));
        Assert.Null(controller.State);
        if (cancellation != 0)
        {
            Assert.False(controller.IsAvailable);
            controller.Attach();
        }
        var newSaves = 0;
        var newPending = controller.AskAsync(_ => { newSaves++; return Success(); });
        var newState = Assert.IsType<WindowCloseDialogState>(controller.State);
        Assert.True(newState.Id > oldId);

        controller.Dismiss(oldId);
        controller.FailPresentation(oldId);
        Assert.True(controller.IsAvailable);
        await controller.ChooseAsync(oldId, false).WaitAsync(Timeout);
        if (lateResult == 0) oldSave.SetResult(TrackerCommandResult.Success);
        else if (lateResult == 1) oldSave.SetResult(new TrackerCommandResult("Late failure"));
        else oldSave.SetException(new IOException("Late exception"));
        await oldChoosing.WaitAsync(Timeout);

        Assert.Same(newState, controller.State);
        Assert.False(newPending.IsCompleted);
        Assert.Equal(1, oldSaves);
        Assert.Equal(0, newSaves);
        await controller.ChooseAsync(newState.Id, false).WaitAsync(Timeout);
        Assert.Equal(false, await newPending.WaitAsync(Timeout));
        Assert.Equal(1, newSaves);
        Assert.Null(controller.State);
    }

    [Fact]
    public async Task DetachCancelsPendingChoiceAndPreventsNewPresentationUntilAttached()
    {
        var controller = AvailableController();
        var saves = 0;
        var pending = controller.AskAsync(_ => { saves++; return Success(); });

        controller.Detach();

        Assert.Null(await pending.WaitAsync(Timeout));
        Assert.False(controller.IsAvailable);
        Assert.Null(controller.State);
        Assert.Null(await controller.AskAsync(_ => { saves++; return Success(); }).WaitAsync(Timeout));
        await controller.ShowErrorAsync("Title", "Message", "Detail").WaitAsync(Timeout);
        Assert.Equal(0, saves);
        Assert.Null(controller.State);
    }

    [Fact]
    public async Task ErrorWaitsForAcknowledgementAndDoesNotActAsAChoice()
    {
        var controller = AvailableController();
        var pending = controller.ShowErrorAsync("Could not close", "Please try again", "Disk is full");
        var state = Assert.IsType<WindowCloseDialogState>(controller.State);

        Assert.Equal(WindowCloseDialogKind.Error, state.Kind);
        Assert.Equal("Could not close", state.Title);
        Assert.Equal("Please try again", state.Message);
        Assert.Equal("Disk is full", state.Error);
        Assert.False(state.Saving);
        Assert.False(pending.IsCompleted);
        controller.Dismiss(state.Id + 1);
        controller.FailPresentation(state.Id + 1);
        Assert.True(controller.IsAvailable);
        await controller.ChooseAsync(state.Id, true).WaitAsync(Timeout);
        Assert.Same(state, controller.State);
        Assert.False(pending.IsCompleted);

        controller.Dismiss(state.Id);

        await pending.WaitAsync(Timeout);
        Assert.Null(controller.State);
        Assert.True(controller.IsAvailable);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task ErrorCanBeCancelledDetachedOrFailedToPresent(int cancellation)
    {
        var controller = AvailableController();
        var pending = controller.ShowErrorAsync("Title", "Message", null);
        var id = controller.State!.Id;

        if (cancellation == 0) controller.CancelPending();
        else if (cancellation == 1) controller.Detach();
        else controller.FailPresentation(id);

        await pending.WaitAsync(Timeout);
        Assert.Null(controller.State);
        Assert.Equal(cancellation == 0, controller.IsAvailable);
    }

    [Fact]
    public async Task FailedPresentationCancelsChoiceWithoutSaving()
    {
        var controller = AvailableController();
        var saves = 0;
        var pending = controller.AskAsync(_ => { saves++; return Success(); });
        var state = controller.State;

        controller.FailPresentation(state!.Id + 1);
        Assert.Same(state, controller.State);
        Assert.True(controller.IsAvailable);
        Assert.False(pending.IsCompleted);
        controller.FailPresentation(state.Id);

        Assert.Null(await pending.WaitAsync(Timeout));
        Assert.Equal(0, saves);
        Assert.Null(controller.State);
        Assert.False(controller.IsAvailable);
        Assert.Null(await controller.AskAsync(_ => { saves++; return Success(); }).WaitAsync(Timeout));
        await controller.ShowErrorAsync("Title", "Message", null).WaitAsync(Timeout);
        Assert.Null(controller.State);
        Assert.Equal(0, saves);
    }

    [Fact]
    public async Task IdsIncreaseAcrossAcknowledgementCancellationAndReattachment()
    {
        var controller = AvailableController();
        var first = controller.AskAsync(_ => Success());
        var firstId = controller.State!.Id;
        await controller.ChooseAsync(firstId, true).WaitAsync(Timeout);
        Assert.Equal(true, await first.WaitAsync(Timeout));
        var error = controller.ShowErrorAsync("Title", "Message", "Detail");
        var errorId = controller.State!.Id;
        Assert.True(errorId > firstId);
        controller.Dismiss(errorId);
        await error.WaitAsync(Timeout);
        controller.Detach();
        controller.Attach();
        var next = controller.AskAsync(_ => Success());
        var nextState = controller.State;
        Assert.True(nextState!.Id > errorId);

        controller.Dismiss(firstId);
        controller.FailPresentation(errorId);
        await controller.ChooseAsync(firstId, false).WaitAsync(Timeout);

        Assert.Same(nextState, controller.State);
        Assert.True(controller.IsAvailable);
        Assert.False(next.IsCompleted);
        controller.CancelPending();
        Assert.Null(await next.WaitAsync(Timeout));
    }

    [Fact]
    public async Task ConcurrentAsksShareOnePendingTaskAndSaveOnlyOnce()
    {
        var controller = AvailableController();
        var pending = new ConcurrentBag<Task<bool?>>();
        var saves = 0;
        await Task.WhenAll(Enumerable.Range(0, 32).Select(index => Task.Run(() =>
        {
            pending.Add(controller.AskAsync(_ => { Interlocked.Increment(ref saves); return Success(); }));
        }))).WaitAsync(Timeout);
        var first = pending.First();
        Assert.Equal(32, pending.Count);
        Assert.All(pending, task => Assert.Same(first, task));
        Assert.Equal(0, saves);

        await controller.ChooseAsync(controller.State!.Id, true).WaitAsync(Timeout);

        Assert.All(await Task.WhenAll(pending).WaitAsync(Timeout), result => Assert.Equal(true, result));
        Assert.Equal(1, saves);
        Assert.Null(controller.State);
    }

    [Fact]
    public async Task ConcurrentChoicesInvokeSaveOnlyOnce()
    {
        var controller = AvailableController();
        var save = Completion<TrackerCommandResult>();
        var saves = 0;
        var savedChoice = false;
        var pending = controller.AskAsync(choice =>
        {
            savedChoice = choice;
            Interlocked.Increment(ref saves);
            return save.Task;
        });
        var id = controller.State!.Id;
        var choosing = new ConcurrentBag<Task>();
        await Task.WhenAll(Enumerable.Range(0, 32).Select(index => Task.Run(() =>
        {
            choosing.Add(controller.ChooseAsync(id, index % 2 == 0));
        }))).WaitAsync(Timeout);

        Assert.Equal(1, saves);
        Assert.True(controller.State!.Saving);
        Assert.False(pending.IsCompleted);
        save.SetResult(TrackerCommandResult.Success);
        await Task.WhenAll(choosing).WaitAsync(Timeout);
        Assert.Equal((bool?)savedChoice, await pending.WaitAsync(Timeout));
        Assert.Equal(1, saves);
        Assert.Null(controller.State);
    }

    [Fact]
    public async Task ChangedNotificationsPublishStateWithoutHoldingTheControllerLock()
    {
        var controller = AvailableController();
        var states = new List<WindowCloseDialogState?>();
        controller.Changed += () =>
        {
            WindowCloseDialogState? observed = null;
            var reader = new Thread(() => observed = controller.State) { IsBackground = true };
            reader.Start();
            Assert.True(reader.Join(Timeout), "A Changed observer could not read State from another thread.");
            states.Add(observed);
        };
        var saves = 0;
        var pending = controller.AskAsync(_ => ++saves == 1
            ? Task.FromResult(new TrackerCommandResult("Retry required")) : Success());
        var id = controller.State!.Id;

        await controller.ChooseAsync(id, true).WaitAsync(Timeout);
        await controller.ChooseAsync(id, false).WaitAsync(Timeout);

        Assert.Equal(false, await pending.WaitAsync(Timeout));
        Assert.Collection(states,
            state => { Assert.Equal(WindowCloseDialogKind.Choice, state!.Kind); Assert.False(state.Saving); },
            state => { Assert.True(state!.Saving); Assert.Null(state.Error); },
            state => { Assert.False(state!.Saving); Assert.Equal("Retry required", state.Error); },
            state => { Assert.True(state!.Saving); Assert.Null(state.Error); },
            state => Assert.Null(state));
    }

    [Fact]
    public async Task CancellationDoesNotRunPendingContinuationsInline()
    {
        var controller = AvailableController();
        var pending = controller.AskAsync(_ => Success());
        var completingThread = Environment.CurrentManagedThreadId;
        var cancelling = 0;
        var inline = 0;
        var continuation = pending.ContinueWith(_ =>
        {
            if (Environment.CurrentManagedThreadId == completingThread && Volatile.Read(ref cancelling) != 0)
                Interlocked.Exchange(ref inline, 1);
        }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);

        Volatile.Write(ref cancelling, 1);
        controller.CancelPending();
        Volatile.Write(ref cancelling, 0);

        await continuation.WaitAsync(Timeout);
        Assert.Equal(0, inline);
        Assert.Null(await pending.WaitAsync(Timeout));
    }

    private static WindowCloseDialogController AvailableController()
    {
        var controller = new WindowCloseDialogController();
        controller.Attach();
        return controller;
    }

    private static Task<TrackerCommandResult> Success() => Task.FromResult(TrackerCommandResult.Success);
    private static TaskCompletionSource<T> Completion<T>() => new(TaskCreationOptions.RunContinuationsAsynchronously);
}
