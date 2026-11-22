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
    private const int FormatVersion = 1;

    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() },
    };

    private sealed record StepDto(Guid Id, StepKind Kind, string Caption, string Notes, string AppName,
        DateTimeOffset Timestamp, string? Image);

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
                string? image = null;
                if (s.ImagePng is { Length: > 0 } png)
                {
                    image = $"images/{s.Id:N}.png";
                    var entry = zip.CreateEntry(image, CompressionLevel.NoCompression); // PNG is already compressed
                    using var es = entry.Open();
                    es.Write(png);
                }
                steps.Add(new StepDto(s.Id, s.Kind, s.Caption, s.Notes, s.AppName, s.Timestamp, image));
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
            byte[]? png = null;
            if (s.Image is not null && zip.GetEntry(s.Image) is { } entry)
            {
                using var es = entry.Open();
                using var ms = new MemoryStream();
                es.CopyTo(ms);
                png = ms.ToArray();
            }
            guide.Steps.Add(new Step
            {
                Id = s.Id, Kind = s.Kind, Caption = s.Caption, Notes = s.Notes,
                AppName = s.AppName, Timestamp = s.Timestamp, ImagePng = png,
            });
        }
        return guide;
    }
}
