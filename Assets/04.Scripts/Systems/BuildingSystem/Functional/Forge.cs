using UnityEngine;

/// <summary>
/// 대장간 - 광석을 가공하여 금속 부품을 제작하는 생산 건물.
/// ProductionBuilding을 상속받아 기본 생산 기능을 사용합니다.
/// 불꽃 파티클과 불빛 효과가 있습니다.
/// </summary>
public class Forge : ProductionBuilding
{
    #region 필드

    [Header("대장간 전용")]
    [SerializeField] private ParticleSystem fireParticles;
    [SerializeField] private Light forgeLight;

    #endregion

    #region ProductionBuilding 오버라이드

    /// <summary>건물 이름을 반환합니다</summary>
    protected override string GetBuildingName()
    {
        return "대장간";
    }

    /// <summary>
    /// 생산이 시작될 때 호출됩니다.
    /// 불꽃 파티클과 불빛을 활성화합니다.
    /// </summary>
    protected override void OnProductionStarted()
    {
        base.OnProductionStarted();

        if (fireParticles != null)
            fireParticles.Play();

        if (forgeLight != null)
            forgeLight.enabled = true;

        Debug.Log("[대장간] 화로에 불을 지폈습니다!");
    }

    /// <summary>
    /// 생산이 완료될 때 호출됩니다.
    /// 대장간 효과를 정지합니다.
    /// </summary>
    /// <param name="recipe">완료된 레시피</param>
    /// <param name="amount">제작 수량</param>
    protected override void OnProductionCompleted(CraftingRecipe recipe, int amount)
    {
        base.OnProductionCompleted(recipe, amount);
        StopForgeEffects();
        Debug.Log("[대장간] 금속 제련 완료!");
    }

    /// <summary>
    /// 생산이 취소될 때 호출됩니다.
    /// 대장간 효과를 정지합니다.
    /// </summary>
    protected override void OnProductionCancelled()
    {
        base.OnProductionCancelled();
        StopForgeEffects();
    }

    #endregion

    #region 내부 헬퍼

    /// <summary>
    /// 대장간 효과 (불꽃, 불빛)를 정지합니다.
    /// </summary>
    private void StopForgeEffects()
    {
        if (fireParticles != null)
            fireParticles.Stop();

        if (forgeLight != null)
            forgeLight.enabled = false;
    }

    #endregion
}
