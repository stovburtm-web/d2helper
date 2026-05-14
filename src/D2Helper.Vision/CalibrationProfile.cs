using System.Text.Json;
using System.Text.Json.Serialization;

namespace D2Helper.Vision;

/// <summary>
/// Калібрування мінімапи: де вона знаходиться у вікні Dota + чи розгорнута 180°
/// (коли граєш за Dire і в налаштуваннях ввімкнено "Use team-coloured minimap orientation").
///
/// Зберігається у <c>%LocalAppData%\D2Helper\vision.json</c>.
/// Координати — у клієнтській системі вікна Dota (не глобальній екранній),
/// бо HUD масштабується разом із розміром вікна; client-coords стабільні до resize.
/// </summary>
public sealed record CalibrationProfile
{
    /// <summary>Лівий край мінімапи в client-coords вікна Dota.</summary>
    public int Left { get; init; }
    /// <summary>Верхній край.</summary>
    public int Top { get; init; }
    /// <summary>Ширина (мінімапа квадратна, але дозволяємо різницю на 1-2px).</summary>
    public int Width { get; init; } = 256;
    /// <summary>Висота.</summary>
    public int Height { get; init; } = 256;

    /// <summary>
    /// True якщо мінімапа візуально розвернута на 180° (база гравця у лівому-нижньому,
    /// тобто гравець за Dire з ввімкненою "Rotate minimap" опцією). У такому випадку
    /// world-to-minimap трансформ дзеркалить осі.
    /// </summary>
    public bool IsRotated180 { get; init; }

    /// <summary>
    /// Дефолт для 1920×1080 без HUD-scale — мінімапа в лівому-нижньому куті ~256×256.
    /// Гравець все одно має натиснути "Calibrate" для точності.
    /// </summary>
    public static CalibrationProfile Default(int clientWidth, int clientHeight)
    {
        // Підгоняємо пропорційно. У стандартній HUD 1920×1080 мінімапа ~265×255, відступ 8px знизу/зліва.
        var size = Math.Min(clientWidth, clientHeight) * 256 / 1080;
        return new CalibrationProfile
        {
            Left = 8,
            Top = clientHeight - size - 8,
            Width = size,
            Height = size,
        };
    }

    public static string DefaultPath()
    {
        var root = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(root, "D2Helper", "vision.json");
    }

    public static CalibrationProfile? TryLoad(string? path = null)
    {
        try
        {
            path ??= DefaultPath();
            if (!File.Exists(path)) return null;
            return JsonSerializer.Deserialize<CalibrationProfile>(File.ReadAllText(path), JsonOpts);
        }
        catch
        {
            return null;
        }
    }

    public void Save(string? path = null)
    {
        path ??= DefaultPath();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(this, JsonOpts));
    }

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };
}
