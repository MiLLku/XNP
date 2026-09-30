using UnityEngine;

/// <summary>
/// 조리 레시피 — 아무 조리 재료 N개 → 요리 1개.
/// 영양 = 넣은 재료 영양 합 × <see cref="nutritionMultiplier"/>.
/// 침식 재료가 하나라도 들어가면 <see cref="erosionOutput"/>이 나옵니다.
/// </summary>
[CreateAssetMenu(fileName = "NewCookingRecipe", menuName = "StampSystem/Cooking Recipe")]
public class CookingRecipe : ScriptableObject, IPlanRecipe
{
    public string recipeName = "간단한 식사";

    [Tooltip("일반 결과물")]
    public ItemData output;

    [Tooltip("침식 재료가 섞였을 때 결과물")]
    public ItemData erosionOutput;

    [Tooltip("필요한 조리 재료 개수 (종류 무관)")]
    [Min(1)] public int ingredientCount = 4;

    [Tooltip("조리 작업량 (조리 속도 1, 스토브 배율 1 기준 초)")]
    [Min(0.1f)] public float cookTime = 8f;

    [Tooltip("영양 배율 — 재료 영양 합에 곱함")]
    [Min(0f)] public float nutritionMultiplier = 1.2f;

    public string DisplayName => recipeName;

    public bool UsesIngredientFilter => true;

    public int CountInStock()
    {
        var inv = InventoryManager.instance;
        if (inv == null) return 0;
        // 창고 + 아직 옮기지 않은 바닥 더미까지 — 창고만 세면 운반 전에 하나 더 만든다
        return (output != null ? inv.GetWorldAvailable(output) : 0) + (erosionOutput != null ? inv.GetWorldAvailable(erosionOutput) : 0);
    }
}
