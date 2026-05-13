using UnityEngine;

/// <summary>
/// 제련소.
/// 광석을 제련하여 금속 주괴를 생산하는 생산 건물.
/// 일정 확률로 보너스 주괴를 추가 생산합니다.
/// </summary>
public class Smelter : ProductionBuilding
{
    #region 상수

    /// <summary>화로 빛 기본 강도</summary>
    private const float FURNACE_GLOW_INTENSITY = 2f;

    #endregion

    #region 필드

    [Header("제련소 전용")]
    [SerializeField] private ParticleSystem lavaParticles;
    [SerializeField] private Light furnaceGlow;
    [SerializeField] [Range(0f, 1f)] private float bonusChance = 0.2f;

    #endregion

    #region ProductionBuilding 오버라이드

    protected override string GetBuildingName()
    {
        return "제련소";
    }

    /// <summary>
    /// 제작 시작 시 용암 파티클과 화로 빛을 활성화합니다.
    /// </summary>
    protected override void OnProductionStarted()
    {
        base.OnProductionStarted();

        if (lavaParticles != null)
            lavaParticles.Play();

        if (furnaceGlow != null)
        {
            furnaceGlow.enabled = true;
            furnaceGlow.intensity = FURNACE_GLOW_INTENSITY;
        }

        Debug.Log("[제련소] 용광로 점화!");
    }

    /// <summary>
    /// 제작 완료 시 보너스 생산을 확인하고 이펙트를 정리합니다.
    /// 기본 생산량은 부모 OnCraftingComplete에서 이미 추가되었으므로,
    /// 여기서는 보너스 분만 추가로 지급합니다.
    /// </summary>
    protected override void OnProductionCompleted(CraftingRecipe recipe, int amount)
    {
        // 보너스 체크 (확률적으로 추가 생산)
        int bonusAmount = 0;
        for (int i = 0; i < amount; i++)
        {
            if (Random.value < bonusChance)
            {
                bonusAmount++;
            }
        }

        if (bonusAmount > 0 && InventoryManager.instance != null)
        {
            int bonusOutput = recipe.outputAmount * bonusAmount;
            InventoryManager.instance.AddItem(recipe.outputItem, bonusOutput);
            Debug.Log($"[제련소] 보너스 생산! +{bonusOutput}개 추가!");
        }

        Debug.Log("[제련소] 제련 완료!");

        // 이펙트 정리
        StopSmelterEffects();
    }

    /// <summary>
    /// 제작 취소 시 이펙트를 정리합니다.
    /// </summary>
    protected override void OnProductionCancelled()
    {
        base.OnProductionCancelled();
        StopSmelterEffects();
    }

    #endregion

    #region 내부 메서드

    /// <summary>
    /// 제련소 전용 이펙트 (파티클, 화로 빛)를 정지합니다.
    /// </summary>
    private void StopSmelterEffects()
    {
        if (lavaParticles != null)
            lavaParticles.Stop();

        if (furnaceGlow != null)
            furnaceGlow.enabled = false;
    }

    #endregion
}
