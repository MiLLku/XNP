using System.Collections.Generic;
using UnityEngine;

/// <summary>식생물이 요구하는 지형 — 하늘에 열린 칸(지표)인지, 덮인 칸(동굴)인지.</summary>
public enum PlantHabitat
{
    /// <summary>상관없음</summary>
    Any = 0,
    /// <summary>하늘에 열린 칸만 (RoomManager.IsOpenToSky)</summary>
    Surface = 1,
    /// <summary>위가 막힌 칸만</summary>
    Cave = 2,
}

/// <summary>사용 여부를 켤 수 있는 수치 범위. 꺼져 있으면 모든 값이 통과합니다.</summary>
[System.Serializable]
public struct ConditionRange
{
    public bool use;
    public float min;
    public float max;

    public bool Contains(float value) => !use || (value >= min && value <= max);
}

/// <summary>
/// 식생물의 환경 조건 묶음. 생성 조건·성장 조건이 같은 형식을 씁니다.
/// 판정은 <see cref="PlantConditionChecker"/> 한 곳에서만 합니다.
/// </summary>
[System.Serializable]
public class PlantConditions
{
    [Tooltip("환경 침식 수치 범위 (TerrainErosionManager.GetRoomErosionAt)")]
    public ConditionRange erosion;

    [Tooltip("온도 범위 ℃ (TemperatureManager.GetTemperatureAt)")]
    public ConditionRange temperature;

    [Tooltip("지표/동굴 제한")]
    public PlantHabitat habitat = PlantHabitat.Any;
}

/// <summary>
/// 식생물 조건 판정의 단일 출처.
/// 자연 생성·성장 정지·아이템 설명 문구가 모두 여기를 거칩니다.
///
/// 칸은 <b>식물이 서는 칸</b>(바닥 타일 바로 위)을 기준으로 합니다.
/// 매니저가 아직 없으면(맵 생성 도중 등) 그 항목은 통과로 봅니다.
/// </summary>
public static class PlantConditionChecker
{
    /// <summary>환경 조건을 판정합니다. failures가 주어지면 실패 사유를 채웁니다.</summary>
    public static bool Check(PlantConditions c, Vector2Int cell, List<string> failures = null)
    {
        if (c == null) return true;
        bool ok = true;

        if (c.erosion.use && TerrainErosionManager.instance != null)
        {
            float v = TerrainErosionManager.instance.GetRoomErosionAt(cell);
            if (!c.erosion.Contains(v)) { ok = false; failures?.Add($"침식 {v:F0} (필요 {RangeText(c.erosion)})"); }
        }

        if (c.temperature.use && TemperatureManager.instance != null)
        {
            float v = TemperatureManager.instance.GetTemperatureAt(cell);
            if (!c.temperature.Contains(v)) { ok = false; failures?.Add($"온도 {v:F0}℃ (필요 {RangeText(c.temperature)}℃)"); }
        }

        if (c.habitat != PlantHabitat.Any && RoomManager.instance != null)
        {
            bool surface = RoomManager.instance.IsOpenToSky(cell.x, cell.y);
            if (surface != (c.habitat == PlantHabitat.Surface))
            {
                ok = false;
                failures?.Add(c.habitat == PlantHabitat.Surface ? "지표 필요" : "동굴 필요");
            }
        }

        return ok;
    }

    /// <summary>
    /// 자연 생성 판정 — 생성 조건 + 주변 동일 식생물 개수.
    /// 플레이어가 심은 개체도 개수에 포함됩니다.
    /// </summary>
    public static bool CanSpawn(EntityDefinition def, Vector2Int cell, List<string> failures = null)
    {
        if (def == null) return false;
        bool ok = Check(def.spawnConditions, cell, failures);

        if (def.neighborRadius > 0)
        {
            int n = CountNearby(def.id, cell, def.neighborRadius);
            if (n > def.maxNeighbors) { ok = false; failures?.Add($"주변 {def.Label} {n}개 (최대 {def.maxNeighbors})"); }
        }

        return ok;
    }

    /// <summary>반경(칸) 안의 같은 개체 수. 원형 거리 기준.</summary>
    public static int CountNearby(int entityId, Vector2Int cell, float radius)
    {
        Vector2 center = cell;
        float r2 = radius * radius;
        int count = 0;

        foreach (var e in MapEntityIdentity.All)
        {
            if (e.EntityId != entityId) continue;
            if (((Vector2)e.transform.position - center).sqrMagnitude <= r2) count++;
        }
        return count;
    }

    /// <summary>
    /// 적정 온도에 따른 성장 속도 배율 (0~1).
    /// 범위 안이면 1, 벗어난 만큼 선형으로 줄어 falloff에서 최저치.
    /// </summary>
    public static float TemperatureRate(EntityDefinition def, Vector2Int cell)
    {
        if (def == null || !def.optimalTemperature.use || TemperatureManager.instance == null) return 1f;

        float t = TemperatureManager.instance.GetTemperatureAt(cell);
        float off = t < def.optimalTemperature.min ? def.optimalTemperature.min - t
                  : t > def.optimalTemperature.max ? t - def.optimalTemperature.max
                  : 0f;
        return Mathf.Lerp(1f, def.minTemperatureRate, Mathf.Clamp01(off / def.temperatureFalloff));
    }

    /// <summary>아이템 설명 등에 쓸 조건 문구. 조건이 없으면 빈 목록.</summary>
    public static List<string> Describe(PlantConditions c)
    {
        var lines = new List<string>();
        if (c == null) return lines;

        if (c.erosion.use) lines.Add($"침식 {RangeText(c.erosion)}");
        if (c.temperature.use) lines.Add($"온도 {RangeText(c.temperature)}℃");
        if (c.habitat == PlantHabitat.Surface) lines.Add("지표");
        else if (c.habitat == PlantHabitat.Cave) lines.Add("동굴");
        return lines;
    }

    private static string RangeText(ConditionRange r) => $"{r.min:0.#}~{r.max:0.#}";
}
