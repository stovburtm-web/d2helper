namespace D2Helper.Vision;

/// <summary>
/// Двосторонній мапінг між world-координатами Dota (-8288..8288 по X/Y)
/// та координатами всередині кропу мінімапи (0..widthPx).
///
/// Якщо гравець за Dire і вмикає "Rotate minimap" — мінімапа візуально
/// дзеркалить осі (база Dire опиняється у лівому-нижньому замість правого-верхнього).
/// В такому випадку world->minimap робить додатковий flip X/Y.
/// </summary>
public static class WorldToMinimap
{
    // Грубі межі ігрового світу Dota 2. Точне значення може варіюватись на ±200,
    // але для відображення на мінімапі 256×256 px це <1px помилки.
    public const float WorldMin = -8288f;
    public const float WorldMax = 8288f;
    public const float WorldSpan = WorldMax - WorldMin; // 16576

    /// <summary>
    /// World (X, Y) → пікселі всередині мінімапи (0..widthPx, 0..heightPx).
    /// У мінімапі Y росте вниз (як в більшості UI), у Dota world Y росте вгору — тому інверсія.
    /// </summary>
    public static (float Px, float Py) ToMinimap(
        float worldX, float worldY,
        int minimapWidthPx, int minimapHeightPx,
        bool isRotated180)
    {
        var nx = (worldX - WorldMin) / WorldSpan;        // 0..1
        var ny = (worldY - WorldMin) / WorldSpan;        // 0..1

        if (isRotated180)
        {
            nx = 1f - nx;
            ny = 1f - ny;
        }

        var px = nx * minimapWidthPx;
        var py = (1f - ny) * minimapHeightPx;            // інверсія Y
        return (px, py);
    }

    /// <summary>Обернений мапінг: пікселі ROI → world coords.</summary>
    public static (float WorldX, float WorldY) ToWorld(
        float px, float py,
        int minimapWidthPx, int minimapHeightPx,
        bool isRotated180)
    {
        var nx = px / minimapWidthPx;
        var ny = 1f - py / minimapHeightPx;

        if (isRotated180)
        {
            nx = 1f - nx;
            ny = 1f - ny;
        }

        return (nx * WorldSpan + WorldMin, ny * WorldSpan + WorldMin);
    }
}
