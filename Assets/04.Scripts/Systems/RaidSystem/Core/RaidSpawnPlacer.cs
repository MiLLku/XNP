using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 레이드 스폰 위치 선정 헬퍼 (RaidSpawnLocation별).
///
///   FogGround      — 안개(미탐사) 속 + 서 있을 지면 + 위로 하늘이 열린 칸
///   FogUnderground — 안개 속 + 지면 + 위 SKY_SCAN_HEIGHT칸 내 막힘(공동)
///   BaseInterior   — 기지 앵커(건설물·직원) 반경 내 지면 칸, 안개 무관
///
/// 안개 유형은 기지 앵커 반경(NEAR_BASE_RADIUS) 내 후보를 우선 선택한다
/// (EventEffectApplier.SpawnXenopsInFogArea와 동일한 정책).
/// 후보가 없으면 false — 호출자는 defaultSpawnPoint 등으로 폴백할 것.
/// </summary>
public static class RaidSpawnPlacer
{
    /// <summary>기지 앵커에서 이 반경(타일) 안의 후보를 우선/제한한다.</summary>
    private const float NEAR_BASE_RADIUS = 16f;

    /// <summary>지상/지하 판정: 위로 이 칸수 안에 솔리드가 있으면 '지하'.</summary>
    private const int SKY_SCAN_HEIGHT = 12;

    /// <summary>
    /// 스폰 위치를 선정합니다.
    /// </summary>
    /// <returns>후보를 찾았는지. 실패 시 호출자가 폴백해야 한다.</returns>
    public static bool TryFindPosition(RaidSpawnLocation location, out Vector3 position)
    {
        position = Vector3.zero;

        var mapGen = MapGenerator.instance;
        var gameMap = mapGen != null ? mapGen.GameMapInstance : null;
        if (gameMap == null) return false;

        var fogMgr = FogOfWarManager.instance;
        var anchors = CollectBaseAnchors();

        var candidates = new List<Vector2Int>();
        var nearBase   = new List<Vector2Int>();

        for (int x = 0; x < GameMap.MAP_WIDTH; x++)
        {
            for (int y = 1; y < GameMap.MAP_HEIGHT; y++)
            {
                if (gameMap.TileGrid[x, y] != 0) continue;     // 공기 아님
                if (gameMap.TileGrid[x, y - 1] == 0) continue; // 발밑 지면 없음

                bool inFog = fogMgr == null || !fogMgr.IsRevealed(x, y);
                bool near  = IsNearAnchors(anchors, x, y);

                switch (location)
                {
                    case RaidSpawnLocation.FogGround:
                        if (!inFog || !IsSkyOpen(gameMap, x, y)) continue;
                        break;

                    case RaidSpawnLocation.FogUnderground:
                        if (!inFog || IsSkyOpen(gameMap, x, y)) continue;
                        break;

                    case RaidSpawnLocation.BaseInterior:
                        // 안개 무관 — 기지 반경 내에서만
                        if (!near) continue;
                        break;
                }

                candidates.Add(new Vector2Int(x, y));
                if (near) nearBase.Add(new Vector2Int(x, y));
            }
        }

        if (candidates.Count == 0) return false;

        // 안개 유형은 기지 근처 후보 우선 (없으면 전체에서)
        var pool = (location != RaidSpawnLocation.BaseInterior && nearBase.Count > 0)
            ? nearBase : candidates;

        var pick = pool[Random.Range(0, pool.Count)];
        position = new Vector3(pick.x + 0.5f, pick.y + 0.5f, 0f);
        return true;
    }

    /// <summary>위로 SKY_SCAN_HEIGHT칸 안에 솔리드 타일이 없으면 '하늘 열림(지상)'.</summary>
    private static bool IsSkyOpen(GameMap gameMap, int x, int y)
    {
        int top = Mathf.Min(GameMap.MAP_HEIGHT, y + 1 + SKY_SCAN_HEIGHT);
        for (int yy = y + 1; yy < top; yy++)
        {
            if (gameMap.TileGrid[x, yy] != 0) return false;
        }
        return true;
    }

    /// <summary>기지 앵커(건설물 + 생존 직원 위치) 수집.</summary>
    private static List<Vector2> CollectBaseAnchors()
    {
        var anchors = new List<Vector2>();

        var buildings = UnityEngine.Object.FindObjectsByType<Building>(FindObjectsSortMode.None);
        foreach (var b in buildings)
        {
            if (b != null) anchors.Add(b.transform.position);
        }

        if (EmployeeManager.instance != null)
        {
            foreach (var emp in EmployeeManager.instance.AllEmployees)
            {
                if (emp != null && emp.State != EmployeeState.Dead)
                    anchors.Add(emp.transform.position);
            }
        }
        return anchors;
    }

    private static bool IsNearAnchors(List<Vector2> anchors, int x, int y)
    {
        if (anchors.Count == 0) return false;

        float sqr = NEAR_BASE_RADIUS * NEAR_BASE_RADIUS;
        Vector2 world = new Vector2(x + 0.5f, y + 0.5f);

        foreach (var a in anchors)
        {
            if ((a - world).sqrMagnitude <= sqr) return true;
        }
        return false;
    }
}
