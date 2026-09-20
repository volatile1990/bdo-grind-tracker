namespace BdoGrindTracker.App.Overlay;

internal interface IRotationEventTracker
{
    void Observe(string kind, string label, DateTimeOffset at);
    void Interrupt(string status);
    void InterruptAt(string status, DateTimeOffset at) => Interrupt(status);
    void Advance(DateTimeOffset at) { }
    void RestoreBoundary(DateTimeOffset at, bool cleanStart) { }
    RotationMonitorSnapshot Snapshot(DateTimeOffset now);
    (DateTimeOffset StartedAt, RotationRun Run)[] DrainCompleted();
    /// <summary>A new loot arrival; returns whether it started a rotation. Only loot-started spots use it.</summary>
    bool ObserveLoot(DateTimeOffset at) => false;
    RotationTimelineEntry[] DrainTimeline() => [];
    (DateTimeOffset StartedAt, RotationRun Run)? ActiveRun() => null;
}
