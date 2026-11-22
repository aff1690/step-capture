using StepCapture.Core.Model;
using StepCapture.Core.Storage;

namespace StepCapture.Core.Tests;

public sealed class MarkupTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("stepcapture-markup-").FullName;
    public void Dispose() => Directory.Delete(_dir, recursive: true);

    [Fact]
    public void MarkupSurvivesSaveAndOpen()
    {
        var basePng = TestData.Png(100, 60, 10, 10, 10);
        var step = new Step
        {
            Caption = "Click Save",
            BaseImagePng = basePng,
            ImagePng = TestData.Png(100, 60),
            Markup =
            {
                new MarkupItem { Kind = MarkupKind.Arrow, Points = { new(1, 2), new(30, 40) }, Color = 0xFF3E63DD, StrokeWidth = 8 },
                new MarkupItem { Kind = MarkupKind.Text, Points = { new(5, 5) }, Text = "Here", FontSize = 42 },
                new MarkupItem { Kind = MarkupKind.Pen, Points = { new(1, 1), new(2, 3), new(4, 4) } },
            },
        };
        string path = Path.Combine(_dir, "g" + GuideFile.Extension);
        GuideFile.Save(new Guide { Steps = { step } }, path);

        var loaded = Assert.Single(GuideFile.Load(path).Steps);
        Assert.Equal(basePng, loaded.BaseImagePng);
        Assert.Equal(step.ImagePng, loaded.ImagePng);
        Assert.Equal(3, loaded.Markup.Count);
        var arrow = loaded.Markup[0];
        Assert.Equal(MarkupKind.Arrow, arrow.Kind);
        Assert.Equal(new[] { new PointD(1, 2), new PointD(30, 40) }, arrow.Points);
        Assert.Equal(0xFF3E63DDu, arrow.Color);
        Assert.Equal(8, arrow.StrokeWidth);
        Assert.Equal("Here", loaded.Markup[1].Text);
        Assert.Equal(step.Markup[1].Id, loaded.Markup[1].Id);
    }

    [Fact]
    public void StepWithoutMarkupStoresNoBaseImage()
    {
        string path = Path.Combine(_dir, "plain" + GuideFile.Extension);
        GuideFile.Save(new Guide { Steps = { new Step { ImagePng = TestData.Png(10, 10) } } }, path);
        var loaded = Assert.Single(GuideFile.Load(path).Steps);
        Assert.Null(loaded.BaseImagePng);
        Assert.Empty(loaded.Markup);
    }

    [Fact]
    public void CalloutsAreRenumberedInOrder()
    {
        var items = new List<MarkupItem>
        {
            new() { Kind = MarkupKind.Number, Text = "1" },
            new() { Kind = MarkupKind.Arrow },
            new() { Kind = MarkupKind.Number, Text = "3" },
            new() { Kind = MarkupKind.Number, Text = "4" },
        };
        MarkupGeometry.RenumberCallouts(items);
        Assert.Equal(new[] { "1", "", "2", "3" }, items.Select(i => i.Text));
    }

    [Fact]
    public void ShiftConstrainsLinesTo45Degrees()
    {
        var end = MarkupGeometry.Constrain(MarkupKind.Line, new PointD(0, 0), new PointD(100, 10));
        Assert.Equal(0, end.Y, 6);
        Assert.Equal(100.5, end.X, 0);
    }

    [Fact]
    public void ShiftMakesSquares()
    {
        var end = MarkupGeometry.Constrain(MarkupKind.Rectangle, new PointD(10, 10), new PointD(50, -20));
        Assert.Equal(new PointD(50, -30), end);
    }

    [Fact]
    public void DistanceToSegmentMeasuresPerpendicularAndEnds()
    {
        Assert.Equal(5, MarkupGeometry.DistanceToSegment(new(5, 5), new(0, 0), new(10, 0)), 6);
        Assert.Equal(5, MarkupGeometry.DistanceToSegment(new(13, 4), new(0, 0), new(10, 0)), 6);
    }

    [Fact]
    public void ArrowHeadPointsAtTheEnd()
    {
        var (tip, left, right, shaftEnd) = MarkupGeometry.ArrowHead(new(0, 0), new(100, 0), 5);
        Assert.Equal(new PointD(100, 0), tip);
        Assert.True(left.X < 100 && right.X < 100);
        Assert.Equal(-left.Y, right.Y, 6);
        Assert.True(shaftEnd.X < 100 && shaftEnd.X > left.X);
    }

    [Fact]
    public void CloneIsDeep()
    {
        var a = new MarkupItem { Points = { new(1, 1) } };
        var b = a.Clone();
        b.Move(5, 5);
        Assert.Equal(new PointD(1, 1), a.Points[0]);
        Assert.Equal(new PointD(6, 6), b.Points[0]);
    }

    [Fact]
    public void LightColorsAreDetected()
    {
        Assert.True(MarkupGeometry.IsLight(0xFFFFD32C));
        Assert.True(MarkupGeometry.IsLight(0xFFFFFFFF));
        Assert.False(MarkupGeometry.IsLight(0xFFE5484D));
        Assert.False(MarkupGeometry.IsLight(0xFF1A1F2B));
    }
}
