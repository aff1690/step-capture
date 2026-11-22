namespace StepCapture.Core.Model;

public sealed class Guide
{
    public string Title { get; set; } = "Untitled guide";
    public string Description { get; set; } = "";
    public DateTimeOffset Created { get; set; } = DateTimeOffset.Now;
    public List<Step> Steps { get; set; } = new();
}
