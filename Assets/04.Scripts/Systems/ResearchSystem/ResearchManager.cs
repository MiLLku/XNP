using UnityEngine;

/// <summary>
/// 연구 포인트 전역 관리자.
/// 모든 연구 작업대에서 쌓인 포인트를 누적 관리합니다.
///
/// 저장 위치: Assets/Scripts/Systems/ResearchSystem/ResearchManager.cs
/// </summary>
public class ResearchManager : DestroySingleton<ResearchManager>, ISaveModule
{
    [Header("연구 포인트")]
    [SerializeField] private float totalResearchPoints = 0f;

    // ─── 프로퍼티 ─────────────────────────────────────────

    /// <summary>현재 누적 연구 포인트 총량</summary>
    public float TotalResearchPoints => totalResearchPoints;

    // ─── 포인트 조작 ──────────────────────────────────────

    /// <summary>
    /// 연구 포인트를 추가합니다.
    /// 연구 작업대의 틱(tick)마다 호출됩니다.
    /// </summary>
    /// <param name="amount">추가할 포인트 양</param>
    public void AddPoints(float amount)
    {
        if (amount <= 0f) return;

        totalResearchPoints += amount;
        GameMessageBus.Publish(new ResearchPointsChangedMessage(totalResearchPoints));

        if (Mathf.FloorToInt(totalResearchPoints) > Mathf.FloorToInt(totalResearchPoints - amount))
        {
            Debug.Log($"[ResearchManager] 연구 포인트: {totalResearchPoints:F1}");
        }
    }

    /// <summary>
    /// 연구 포인트를 소비합니다. (향후 연구 기술 해금 시 사용)
    /// </summary>
    /// <param name="amount">소비할 포인트 양</param>
    /// <returns>포인트가 충분하여 소비 성공 시 true</returns>
    public bool SpendPoints(float amount)
    {
        if (!HasPoints(amount))
        {
            Debug.LogWarning($"[ResearchManager] 포인트 부족 (필요: {amount:F1}, 현재: {totalResearchPoints:F1})");
            return false;
        }

        totalResearchPoints -= amount;
        GameMessageBus.Publish(new ResearchPointsChangedMessage(totalResearchPoints));
        Debug.Log($"[ResearchManager] 포인트 소비: -{amount:F1} → 잔여: {totalResearchPoints:F1}");
        return true;
    }

    /// <summary>
    /// 지정한 양만큼 포인트가 있는지 확인합니다.
    /// </summary>
    public bool HasPoints(float amount)
    {
        return totalResearchPoints >= amount;
    }

    // ─── ISaveModule 구현 ──────────────────────────────────

    /// <summary>
    /// 저장 순서. Event(80) 이전에 복원되도록 90으로 설정합니다.
    /// </summary>
    public int SaveOrder => 90;

    /// <summary>
    /// 현재 연구 포인트를 SaveData에 기록합니다.
    /// </summary>
    public void Capture(SaveData data)
    {
        data.researchPoints = totalResearchPoints;
        Debug.Log($"[ResearchManager] 저장: 연구 포인트 {totalResearchPoints:F1}");
    }

    /// <summary>
    /// SaveData로부터 연구 포인트를 복원합니다.
    /// </summary>
    public void Restore(SaveData data)
    {
        totalResearchPoints = data.researchPoints;
        GameMessageBus.Publish(new ResearchPointsChangedMessage(totalResearchPoints));
        Debug.Log($"[ResearchManager] 복원: 연구 포인트 {totalResearchPoints:F1}");
    }

    /// <summary>
    /// PostRestore: 크로스 레퍼런스 없으므로 빈 구현.
    /// </summary>
    public void PostRestore(SaveData data) { }
}
