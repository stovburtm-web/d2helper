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

    public static PlaybookDefinition LoadRole5Sample()
    {
        var path = DataFileLocator.FindFromAppBase(Path.Combine("data", "playbooks", "role5.sample.json"));
        if (path is null) throw new FileNotFoundException("role5.sample.json not found");
        var json = File.ReadAllText(path);
        var playbook = JsonSerializer.Deserialize<PlaybookDefinition>(json, JsonOpts);
        return playbook ?? throw new InvalidDataException("Cannot parse role5.sample.json");
    }
}
