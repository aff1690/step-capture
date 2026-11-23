namespace StepCapture.Core.Model;

/// <summary>
/// A titled note for one numbered callout on a step's screenshot. It is linked to the callout by its id,
/// so it keeps following the callout when callouts are renumbered.
/// </summary>
public sealed class CalloutNote
{
    public Guid CalloutId { get; set; }
    public string Title { get; set; } = "";

    /// <summary>Formatted text, same markup as <see cref="Step.Notes"/>.</summary>
    public string Details { get; set; } = "";

    public bool IsEmpty => string.IsNullOrWhiteSpace(Title) && string.IsNullOrWhiteSpace(Details);

    public CalloutNote Clone() => new() { CalloutId = CalloutId, Title = Title, Details = Details };
}

/// <summary>A callout note with the callout's current number and color (null number: its callout was deleted).</summary>
public sealed record NumberedCalloutNote(CalloutNote Note, int? Number, uint Color);

public static class CalloutNotes
{
    /// <summary>
    /// Makes sure every numbered callout has a note, and drops empty notes whose callout is gone.
    /// Notes with text whose callout was deleted are kept (Undo brings the callout back).
    /// Returns true if anything changed.
    /// </summary>
    public static bool Sync(Step step)
    {
        bool changed = false;
        var calloutIds = step.Markup.Where(m => m.Kind == MarkupKind.Number).Select(m => m.Id).ToList();
        foreach (var id in calloutIds)
            if (!step.CalloutNotes.Any(n => n.CalloutId == id))
            {
                step.CalloutNotes.Add(new CalloutNote { CalloutId = id });
                changed = true;
            }
        changed |= step.CalloutNotes.RemoveAll(n => n.IsEmpty && !calloutIds.Contains(n.CalloutId)) > 0;
        return changed;
    }

    /// <summary>Notes in callout order (1, 2, 3...), then notes whose callout was deleted.</summary>
    public static List<NumberedCalloutNote> Ordered(Step step)
    {
        var callouts = step.Markup.Where(m => m.Kind == MarkupKind.Number).ToDictionary(m => m.Id);
        return step.CalloutNotes
            .Select(n => callouts.TryGetValue(n.CalloutId, out var c)
                ? new NumberedCalloutNote(n, int.TryParse(c.Text, out int num) ? num : null, c.Color)
                : new NumberedCalloutNote(n, null, 0xFF8A94A6))
            .OrderBy(x => x.Number is null ? 1 : 0).ThenBy(x => x.Number)
            .ToList();
    }

    /// <summary>Notes to export: linked to a callout and not empty, in callout order.</summary>
    public static List<NumberedCalloutNote> ForExport(Step step) =>
        Ordered(step).Where(x => x.Number is not null && !x.Note.IsEmpty).ToList();
}
