using System.Text.Json;
using System.Text.Json.Serialization;

namespace D2Helper.Core.Quests;

public static class PlaybookLoader
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public static PlaybookDefinition LoadRole5Sample() => LoadFile("role5.sample.json");

    public static PlaybookDefinition LoadMidMvp() => LoadFile("midlane.mvp.json");

    private static PlaybookDefinition LoadFile(string fileName)
    {
        var path = DataFileLocator.FindFromAppBase(Path.Combine("data", "playbooks", fileName));
        if (path is null) throw new FileNotFoundException(fileName + " not found");
        var json = File.ReadAllText(path);
        var playbook = JsonSerializer.Deserialize<PlaybookDefinition>(json, JsonOpts);
        return playbook ?? throw new InvalidDataException("Cannot parse " + fileName);
    }
}

