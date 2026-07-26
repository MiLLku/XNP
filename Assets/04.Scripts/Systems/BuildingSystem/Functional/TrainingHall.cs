using UnityEngine;

/// <summary>
/// 훈련소 — 재료를 투입해 <b>전 직원의 스킬 포인트 상한</b>을 단계적으로 확장하는 건물.
///
/// 스킬 포인트는 직원당 기본값이 고정되어 있어, 더 많은 스킬을 찍으려면
/// 기지 차원의 투자가 필요하다. 클릭하면 다음 단계 해금을 시도한다.
/// 단계별 필요 재료는 SkillPointManager.upgradeTiers(인스펙터)에서 설정한다.
/// </summary>
public class TrainingHall : MonoBehaviour
{
    /// <summary>훈련소 클릭 시 호출 — 다음 확장 단계를 시도합니다.</summary>
    public void TryUpgrade()
    {
        var mgr = SkillPointManager.instance;
        if (mgr == null)
        {
            Debug.LogWarning("[훈련소] SkillPointManager가 씬에 없습니다.");
            return;
        }

        if (!mgr.HasNextTier)
        {
            Debug.Log("[훈련소] 이미 모든 확장 단계를 해금했습니다.");
            return;
        }

        if (!mgr.TryUnlockNextTier())
            Debug.Log($"[훈련소] 확장 실패 — 필요 재료: {mgr.DescribeNextCost()}");
    }
}
