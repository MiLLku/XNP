using UnityEngine;

/// <summary>
/// 연금술 작업대.
/// 약초와 재료를 가공하여 포션을 제작하는 생산 건물.
/// 제작 중 거품 파티클 효과를 표시합니다.
/// </summary>
public class AlchemyTable : ProductionBuilding
{
    #region 필드

    [Header("연금술 작업대 전용")]
    [SerializeField] private ParticleSystem bubbleParticles;
    [SerializeField] private Color potionColor = Color.green;

    #endregion

    #region ProductionBuilding 오버라이드

    protected override string GetBuildingName()
    {
        return "연금술 작업대";
    }

    /// <summary>
    /// 제작 시작 시 거품 파티클을 활성화합니다.
    /// </summary>
    protected override void OnProductionStarted()
    {
        base.OnProductionStarted();

        if (bubbleParticles != null)
        {
            var main = bubbleParticles.main;
            main.startColor = potionColor;
            bubbleParticles.Play();
        }

        Debug.Log("[연금술 작업대] 포션 제조 시작!");
    }

    /// <summary>
    /// 제작 완료 시 거품 파티클을 정지합니다.
    /// </summary>
    protected override void OnProductionCompleted(CraftingRecipe recipe, int amount)
    {
        base.OnProductionCompleted(recipe, amount);

        if (bubbleParticles != null)
            bubbleParticles.Stop();

        Debug.Log("[연금술 작업대] 포션 제조 완료!");
    }

    /// <summary>
    /// 제작 취소 시 거품 파티클을 정지합니다.
    /// </summary>
    protected override void OnProductionCancelled()
    {
        base.OnProductionCancelled();

        if (bubbleParticles != null)
            bubbleParticles.Stop();

        Debug.Log("[연금술 작업대] 포션 제조 취소됨.");
    }

    #endregion
}
