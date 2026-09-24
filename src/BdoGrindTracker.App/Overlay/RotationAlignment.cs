namespace BdoGrindTracker.App.Overlay;

/// <summary>
/// Where a rotation picked up mid-way began. Every step its first message fits is a candidate. Each candidate walks the
/// messages that followed, across the rotation's end into the next one, and counts the required steps it would have to
/// skip. Monster names (evidence) have to fit the step a candidate stands in, and a name its step always shows turns
/// its absence into evidence too: a Magaia final phase that reaches the AFK phase without Priest of the End is no third
/// cycle, and one without Elion's Tears no first. The candidate that needs fewer skips than every other one is where
/// the rotation began; until one does, nothing is certain.
/// </summary>
internal static class RotationAlignment
{
    /// <summary>The candidate that the first message ended the previous rotation (Magaia's AFK end opens the next one).</summary>
    internal const int RotationEnd = -1;

    /// <param name="candidates">Step indices the first message fits, or <see cref="RotationEnd"/>.</param>
    /// <param name="kinds">The messages from the first one on, in capture order.</param>
    /// <param name="before">Names read right before the first message: they belong to the step before the candidate.</param>
    internal static int? Resolve(RotationDefinition definition, IReadOnlyList<int> candidates, IReadOnlyList<string> kinds,
        IReadOnlyList<string>? before = null)
    {
        if (candidates.Count < 2) return candidates.Count == 1 ? candidates[0] : null;
        var scored = candidates.Select(start => (Start: start, Skips: Skips(definition, start, kinds, before)))
            .OrderBy(candidate => candidate.Skips).ToArray();
        return scored[0].Skips < scored[1].Skips ? scored[0].Start : null;
    }

    /// <summary>Required steps (and rotation ends, and names) a rotation begun at <paramref name="start"/> could not have seen.</summary>
    internal static int Skips(RotationDefinition definition, int start, IReadOnlyList<string> kinds, IReadOnlyList<string>? before = null)
    {
        var steps = definition.Steps;
        // One slot per step and one for the rotation's end; slot j belongs to rotation j / slots.
        var slots = steps.Length + 1;
        var position = start == RotationEnd ? steps.Length : start;
        var skips = 0;
        var visited = start == RotationEnd ? new HashSet<string>() : [steps[start].Id];
        // Names read while standing in the current step.
        var named = new HashSet<string>();
        // Names read before tracking began belong to the step before the candidate (the previous rotation's last step
        // before the rotation's end or the first step).
        var previous = start is RotationEnd or 0 ? steps.Length - 1 : start - 1;
        foreach (var kind in before ?? [])
            if (definition.Evidence?.TryGetValue(kind, out var seenBefore) == true && !seenBefore.Contains(steps[previous].Id)) skips++;
        foreach (var kind in kinds.Skip(1))
        {
            // A monster name moves nothing; it only has to fit the step the candidate stands in.
            if (definition.Evidence?.TryGetValue(kind, out var seenIn) == true)
            {
                if (position % slots == steps.Length || !seenIn.Contains(steps[position % slots].Id)) skips++;
                named.Add(kind);
                continue;
            }
            if (Ambient(kind)) continue;
            // A banner repeating inside its own phase (Elion's Tears' orbs) says nothing new.
            if (position % slots < steps.Length && steps[position % slots].Matches(kind) &&
                steps.Count(step => step.Matches(kind)) == 1) continue;
            int? target = null;
            for (var next = position + 1; next <= position + 2 * slots && target is null; next++)
                if (Fits(next, kind)) target = next;
            // Nothing within two rotations fits: the message itself is out of place.
            if (target is not { } found) { skips++; continue; }
            // Leaving a step whose name never showed: it was not that step.
            if (position % slots < steps.Length && definition.ExpectedEvidence?.TryGetValue(steps[position % slots].Id, out var expected) == true &&
                !named.Contains(expected)) skips++;
            named.Clear();
            for (var between = position + 1; between <= found; between++)
            {
                if (between % slots == steps.Length)
                {
                    if (between < found) skips++; // A rotation end whose banner was not read.
                    visited.Clear();
                    continue;
                }
                var step = steps[between % slots];
                if (between < found && Required(step)) skips++;
                if (between == found)
                {
                    visited.Add(step.Id);
                    if (step.Requires is { } branch) visited.Add(branch);
                }
            }
            position = found;
        }
        return skips;

        // Side messages once the rotation stands past their step, seen or not (Hermesia's offerings after Drakania).
        bool Ambient(string kind) => definition.AmbientMessages?.Contains(kind) == true &&
            (definition.AmbientAfter is not { } after || visited.Contains(after) ||
             position % slots < steps.Length && Array.FindIndex(steps, step => step.Id == after) is var index && index >= 0 &&
             index < position % slots);
        bool Fits(int slot, string kind) => slot % slots == steps.Length
            ? definition.AfkEndMessages.Contains(kind) || definition.StartMessages.Contains(kind) && !steps.Any(step => step.Matches(kind))
            : steps[slot % slots].Matches(kind) && Reachable(steps[slot % slots]);
        // A reliable middle (Event Horizon's distortion) fills in its own opening, so it needs no branch seen first.
        bool Reachable(RotationStep step) => step.Requires is not { } branch || visited.Contains(branch) || step.Midpoint > 0;
        bool Required(RotationStep step) => !step.Optional ||
            step.RequiredWhenBranchObserved && step.Requires is { } branch && visited.Contains(branch);
    }
}
