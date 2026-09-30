using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 생산 계획 — 건물마다 가진 "무엇을 · 얼마나 · 어떤 재료로" 목록 (스토브, 이후 제작대 등).
/// 목록 맨 위 계획부터 처리합니다. 창은 <see cref="ProductionPlanUI"/>.
/// </summary>
public interface IPlanHost
{
    /// <summary>창 제목</summary>
    string HostName { get; }

    /// <summary>계획 목록 (위에서부터 처리)</summary>
    List<ProductionPlan> Plans { get; }

    /// <summary>이 건물에서 고를 수 있는 레시피 — 계획은 여기의 인덱스를 저장합니다</summary>
    IReadOnlyList<IPlanRecipe> Recipes { get; }

    /// <summary>현재 상태 문구 (연료·진행 등)</summary>
    string DescribeStatus();
}

/// <summary>계획에 올릴 수 있는 레시피</summary>
public interface IPlanRecipe
{
    string DisplayName { get; }

    /// <summary>"재고 X개까지 유지"에서 셀 현재 재고</summary>
    int CountInStock();

    /// <summary>재료 필터(침식·채소·육류·직접 지정)를 쓰는 레시피인지</summary>
    bool UsesIngredientFilter { get; }
}

public enum PlanRepeatMode
{
    /// <summary>N개 만들기</summary>
    Count,
    /// <summary>재고가 N개가 될 때까지 유지</summary>
    UntilStock,
    /// <summary>계속</summary>
    Forever
}

[System.Serializable]
public class ProductionPlan
{
    public const int MAX_TARGET = 999;

    public int recipeIndex;
    public PlanRepeatMode mode = PlanRepeatMode.Count;
    public int target = 1;
    /// <summary>N개 만들기에서 지금까지 만든 개수</summary>
    public int done;
    public bool paused;
    public IngredientFilter filter = new IngredientFilter();

    /// <summary>더 만들어야 하는지</summary>
    public bool WantsMore(IPlanRecipe recipe)
    {
        if (paused || recipe == null) return false;
        switch (mode)
        {
            case PlanRepeatMode.Count:      return done < target;
            case PlanRepeatMode.UntilStock: return recipe.CountInStock() < target;
            default:                        return true;
        }
    }

    public string DescribeMode()
    {
        switch (mode)
        {
            case PlanRepeatMode.Count:      return $"{done}/{target}개 만들기";
            case PlanRepeatMode.UntilStock: return $"재고 {target}개 유지";
            default:                        return "계속";
        }
    }
}

/// <summary>
/// 재료 필터 — 분류(침식·채소·육류) 스위치 + 직접 지정(개별 재료 끄기).
/// 둘 다 통과해야 쓸 수 있습니다.
/// </summary>
[System.Serializable]
public class IngredientFilter
{
    public bool allowErosion = true;
    public bool allowVegetable = true;
    public bool allowMeat = true;
    /// <summary>직접 지정으로 끈 재료 (itemID)</summary>
    public List<int> excludedItemIds = new List<int>();

    public bool Allows(ItemData item)
    {
        if (item == null) return false;
        if (!allowErosion && item.IsErosionFood) return false;
        if (!allowVegetable && item.ingredientCategory == IngredientCategory.Vegetable) return false;
        if (!allowMeat && item.ingredientCategory == IngredientCategory.Meat) return false;
        return !IsExcluded(item);
    }

    public bool IsExcluded(ItemData item) => excludedItemIds.Contains(item.itemID);

    public void SetExcluded(ItemData item, bool excluded)
    {
        excludedItemIds.Remove(item.itemID);
        if (excluded) excludedItemIds.Add(item.itemID);
    }

    /// <summary>게임에 있는 모든 조리 재료 (요리가 아닌 음식)</summary>
    public static List<ItemData> AllIngredients()
    {
        var list = new List<ItemData>();
        var db = GameDatabase.Instance;
        if (db == null || db.allItemData == null) return list;
        foreach (var item in db.allItemData)
            if (item != null && item.isFood && !item.IsMeal) list.Add(item);
        list.Sort((a, b) => a.itemID.CompareTo(b.itemID));
        return list;
    }
}
