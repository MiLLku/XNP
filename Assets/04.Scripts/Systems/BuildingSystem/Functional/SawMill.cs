using UnityEngine;

/// <summary>
/// 제재소 - 나무를 가공하여 목재 부품을 제작하는 생산 건물.
/// ProductionBuilding을 상속받아 기본 생산 기능을 사용합니다.
/// </summary>
public class SawMill : ProductionBuilding
{
    #region ProductionBuilding 오버라이드

    /// <summary>건물 이름을 반환합니다</summary>
    protected override string GetBuildingName()
    {
        return "제재소";
    }

    /// <summary>
    /// 생산이 시작될 때 호출됩니다.
    /// 톱질 사운드를 재생합니다.
    /// </summary>
    protected override void OnProductionStarted()
    {
        base.OnProductionStarted();

        // TODO [사운드]: 톱질 소리 재생
        if (workingSound != null)
        {
            // AudioSource.PlayClipAtPoint(workingSound, transform.position);
        }
    }

    /// <summary>
    /// 생산이 완료될 때 호출됩니다.
    /// </summary>
    /// <param name="recipe">완료된 레시피</param>
    /// <param name="amount">제작 수량</param>
    protected override void OnProductionCompleted(CraftingRecipe recipe, int amount)
    {
        base.OnProductionCompleted(recipe, amount);

        // TODO [이펙트]: 나무 조각 파티클 효과 추가
        Debug.Log($"[제재소] 목재 가공 완료!");
    }

    #endregion
}
