using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Serialization;
using StepCapture.Core.Model;

namespace StepCapture.Core.Storage;

/// <summary>
/// Saves and opens guides as ".stepguide" files: a zip archive holding guide.json and one PNG per step.
/// </summary>
public static class GuideFile
{
    public const string Extension = ".stepguide";
    private const int FormatVersion = 3; // 2: editable markup, 3: overview steps

    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() },
    };

    private sealed record StepDto(Guid Id, StepKind Kind, string Caption, string Notes, string AppName,
        DateTimeOffset Timestamp, string? Image, string? BaseImage = null, List<MarkupItem>? Markup = null, bool Overview = false);

    private sealed record GuideDto(int Version, string Title, string Description, DateTimeOffset Created, List<StepDto> Steps);

    public static void Save(Guide guide, string path)
    {
        string tmp = path + ".tmp";
        using (var fs = File.Create(tmp))
        using (var zip = new ZipArchive(fs, ZipArchiveMode.Create))
        {
            var steps = new List<StepDto>();
            foreach (var s in guide.Steps)
            {
                string? image = WriteImage(zip, s.ImagePng, $"images/{s.Id:N}.png");
                string? baseImage = s.Markup.Count > 0 ? WriteImage(zip, s.BaseImagePng, $"images/{s.Id:N}-base.png") : null;
                steps.Add(new StepDto(s.Id, s.Kind, s.Caption, s.Notes, s.AppName, s.Timestamp, image,
                    baseImage, baseImage is null ? null : s.Markup, s.IsOverview));
            }

            var dto = new GuideDto(FormatVersion, guide.Title, guide.Description, guide.Created, steps);
            var jsonEntry = zip.CreateEntry("guide.json", CompressionLevel.Optimal);
            using var js = jsonEntry.Open();
            JsonSerializer.Serialize(js, dto, Json);
        }
        File.Move(tmp, path, overwrite: true);
    }

    public static Guide Load(string path)
    {
        using var zip = ZipFile.OpenRead(path);
        var jsonEntry = zip.GetEntry("guide.json") ?? throw new InvalidDataException("guide.json is missing.");
        GuideDto dto;
        using (var js = jsonEntry.Open())
            dto = JsonSerializer.Deserialize<GuideDto>(js, Json) ?? throw new InvalidDataException("guide.json is empty.");

        var guide = new Guide { Title = dto.Title, Description = dto.Description, Created = dto.Created };
        foreach (var s in dto.Steps)
        {
            var step = new Step
            {
                Id = s.Id, Kind = s.Kind, Caption = s.Caption, Notes = s.Notes,
                AppName = s.AppName, Timestamp = s.Timestamp, ImagePng = ReadImage(zip, s.Image),
                IsOverview = s.Overview,
            };
            if (ReadImage(zip, s.BaseImage) is { } basePng && s.Markup is { Count: > 0 } markup)
            {
                step.BaseImagePng = basePng;
                step.Markup = markup;
            }
            guide.Steps.Add(step);
        }
        return guide;
    }

    private static string? WriteImage(ZipArchive zip, byte[]? png, string name)
    {
        if (png is not { Length: > 0 }) return null;
        var entry = zip.CreateEntry(name, CompressionLevel.NoCompression); // PNG is already compressed
        using var es = entry.Open();
        es.Write(png);
        return name;
    }

    private static byte[]? ReadImage(ZipArchive zip, string? name)
    {
        if (name is null || zip.GetEntry(name) is not { } entry) return null;
        using var es = entry.Open();
        using var ms = new MemoryStream();
        es.CopyTo(ms);
        return ms.ToArray();
    }
}
