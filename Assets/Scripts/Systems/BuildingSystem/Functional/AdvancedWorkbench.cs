using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// 고급 작업대 - 업그레이드 가능한 생산 건물
/// 레벨에 따라 제작 속도 증가 및 특별 레시피 해금
/// </summary>
public class AdvancedWorkbench : ProductionBuilding
{
    [Header("업그레이드 시스템")]
    [SerializeField] private int currentLevel = 1;
    [SerializeField] private int maxLevel = 5;
    [SerializeField] private float speedBonusPerLevel = 0.1f; // 레벨당 10% 속도 증가

    [Header("레벨별 해금 레시피")]
    [SerializeField] private List<CraftingRecipe> level2Recipes; // 레벨 2 해금
    [SerializeField] private List<CraftingRecipe> level3Recipes; // 레벨 3 해금
    [SerializeField] private List<CraftingRecipe> level4Recipes; // 레벨 4 해금
    [SerializeField] private List<CraftingRecipe> level5Recipes; // 레벨 5 해금

    protected override string GetBuildingName()
    {
        return $"고급 작업대 Lv.{currentLevel}";
    }

    protected override void Start()
    {
        base.Start();
        UpdateAvailableRecipes();
    }

    /// <summary>
    /// 레벨에 따라 사용 가능한 레시피 업데이트
    /// </summary>
    private void UpdateAvailableRecipes()
    {
        // 기본 레시피는 항상 사용 가능
        // 레벨에 따라 추가 레시피 해금

        if (currentLevel >= 2 && level2Recipes != null)
        {
            foreach (var recipe in level2Recipes)
            {
                if (!availableRecipes.Contains(recipe))
                    availableRecipes.Add(recipe);
            }
        }

        if (currentLevel >= 3 && level3Recipes != null)
        {
            foreach (var recipe in level3Recipes)
            {
                if (!availableRecipes.Contains(recipe))
                    availableRecipes.Add(recipe);
            }
        }

        if (currentLevel >= 4 && level4Recipes != null)
        {
            foreach (var recipe in level4Recipes)
            {
                if (!availableRecipes.Contains(recipe))
                    availableRecipes.Add(recipe);
            }
        }

        if (currentLevel >= 5 && level5Recipes != null)
        {
            foreach (var recipe in level5Recipes)
            {
                if (!availableRecipes.Contains(recipe))
                    availableRecipes.Add(recipe);
            }
        }

        Debug.Log($"[고급 작업대] 레벨 {currentLevel} - {availableRecipes.Count}개 레시피 사용 가능");
    }

    /// <summary>
    /// 건물 업그레이드
    /// </summary>
    public void UpgradeBuilding()
    {
        if (currentLevel >= maxLevel)
        {
            Debug.LogWarning("[고급 작업대] 이미 최대 레벨입니다!");
            return;
        }

        currentLevel++;
        UpdateAvailableRecipes();

        Debug.Log($"[고급 작업대] 레벨 {currentLevel}로 업그레이드! 제작 속도 +{speedBonusPerLevel * 100}%");
    }

    protected override void StartProduction(Employee worker)
    {
        if (_pendingRecipe == null || worker == null) return;

        // 레벨에 따른 제작 시간 감소
        float speedMultiplier = 1f + (speedBonusPerLevel * (currentLevel - 1));
        float adjustedTime = _pendingRecipe.craftingTime / speedMultiplier;

        Debug.Log($"[고급 작업대] 제작 시간: {_pendingRecipe.craftingTime}초 → {adjustedTime:F1}초 (Lv.{currentLevel} 보너스)");

        // 임시로 제작 시간 조정
        float originalTime = _pendingRecipe.craftingTime;
        _pendingRecipe.craftingTime = adjustedTime;

        base.StartProduction(worker);

        // 원래 시간으로 복구 (다른 건물에 영향 안 가도록)
        _pendingRecipe.craftingTime = originalTime;
    }

    protected override void OnProductionCompleted(CraftingRecipe recipe, int amount)
    {
        base.OnProductionCompleted(recipe, amount);

        Debug.Log($"[고급 작업대 Lv.{currentLevel}] 고급 제작 완료!");
    }

    // Public 메서드
    public int CurrentLevel => currentLevel;
    public bool CanUpgrade => currentLevel < maxLevel;
    public float CurrentSpeedBonus => speedBonusPerLevel * (currentLevel - 1);
}
