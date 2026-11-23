namespace StepCapture.Core.Model;

public sealed class Guide
{
    public string Title { get; set; } = "Untitled guide";
    public string Description { get; set; } = "";
    public DateTimeOffset Created { get; set; } = DateTimeOffset.Now;
    public List<Step> Steps { get; set; } = new();

    /// <summary>
    /// The number shown for each step, in order: 1, 2, 3... for regular steps and null for overview steps,
    /// which don't count.
    /// </summary>
    public IReadOnlyList<int?> StepNumbers() => Numbers(Steps);

    public static IReadOnlyList<int?> Numbers(IEnumerable<Step> steps)
    {
        var result = new List<int?>();
        int n = 0;
        foreach (var s in steps) result.Add(s.IsOverview ? null : ++n);
        return result;
    }

    /// <summary>Number of regular (numbered) steps.</summary>
    public int NumberedStepCount => Steps.Count(s => !s.IsOverview);

    /// <summary>"1 step", "5 steps".</summary>
    /// <summary>The line under the title: "5 steps · November 23, 2026", or just the date when every step is an overview.</summary>
    public string MetaLine => NumberedStepCount == 0 ? Created.ToString("MMMM d, yyyy") : $"{StepCountText} · {Created:MMMM d, yyyy}";

    public string StepCountText => NumberedStepCount == 1 ? "1 step" : $"{NumberedStepCount} steps";
}
