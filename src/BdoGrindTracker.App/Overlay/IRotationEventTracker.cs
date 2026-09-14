namespace BdoGrindTracker.App.Overlay;

internal interface IRotationEventTracker
{
    void Observe(string kind, string label, DateTimeOffset at);
    void Interrupt(string status);
    RotationMonitorSnapshot Snapshot(DateTimeOffset now);
    (DateTimeOffset StartedAt, RotationRun Run)[] DrainCompleted();
}
