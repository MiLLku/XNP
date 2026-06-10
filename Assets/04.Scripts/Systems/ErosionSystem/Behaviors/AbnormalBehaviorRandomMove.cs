using UnityEngine;

/// <summary>
/// 이상 행동 — 무작위 이동.
/// 현재 작업을 취소하고 주변 반경 5타일 내의 통과 가능한 위치로 무작위 이동합니다.
/// </summary>
public class AbnormalBehaviorRandomMove : AbnormalBehaviorBase
{
    private const int MAX_RADIUS   = 5;
    private const int MAX_ATTEMPTS = 12;

    public override AbnormalBehaviorType BehaviorType => AbnormalBehaviorType.RandomMove;

    public override float Execute(Employee employee)
    {
        employee.CancelWork();

        var movement = employee.GetComponent<EmployeeMovement>();
        if (movement == null || MapGenerator.instance == null)
        {
            Debug.Log($"[AbnormalBehavior] {employee.DisplayName}: 무작위 이동 발동 (컴포넌트 없음)");
            return 5f;
        }

        var gameMap = MapGenerator.instance.GameMapInstance;
        if (gameMap == null)
        {
            Debug.Log($"[AbnormalBehavior] {employee.DisplayName}: 무작위 이동 발동 (맵 없음)");
            return 5f;
        }

        Vector3 pos = employee.transform.position;
        int cx = Mathf.FloorToInt(pos.x);
        int cy = Mathf.FloorToInt(pos.y);

        // 반경 MAX_RADIUS 내에서 통과 가능하고 발판 있는 타일 탐색
        for (int attempt = 0; attempt < MAX_ATTEMPTS; attempt++)
        {
            int rx = cx + Random.Range(-MAX_RADIUS, MAX_RADIUS + 1);
            int ry = cy + Random.Range(-MAX_RADIUS, MAX_RADIUS + 1);

            if (rx < 0 || rx >= GameMap.MAP_WIDTH || ry < 0 || ry >= GameMap.MAP_HEIGHT) continue;
            if (!gameMap.IsPassableTile(rx, ry))      continue; // 공기/사다리 타일이어야 함
            if (gameMap.DoesTileBlockMovement(rx, ry)) continue; // 건물이 막고 있으면 제외
            if (!gameMap.IsSolidGround(rx, ry - 1))   continue; // 발 아래 발판 있어야 함

            Vector3 destination = new Vector3(rx + 0.5f, ry, 0f);
            movement.MoveTo(destination);

            Debug.Log($"[AbnormalBehavior] {employee.DisplayName}: 무작위 이동 → ({rx}, {ry})");
            return 5f;
        }

        Debug.Log($"[AbnormalBehavior] {employee.DisplayName}: 무작위 이동 발동 (유효 위치 없음)");
        return 5f;
    }
}
