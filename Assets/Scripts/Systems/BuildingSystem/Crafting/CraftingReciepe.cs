using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 제작 레시피 데이터 (ScriptableObject).
/// 생산 건물에서 사용할 제작법을 정의합니다.
/// 필요한 재료, 결과물, 제작 시간 등을 포함합니다.
/// StampSystem 메뉴에서 생성 가능합니다.
/// </summary>
[CreateAssetMenu(fileName = "NewRecipe", menuName = "StampSystem/Crafting Recipe")]
public class CraftingRecipe : ScriptableObject
{
    #region 제작 정보

    [Header("제작 정보")]
    [Tooltip("제작법의 고유 ID (예: 5001)")]
    public int recipeID;

    [Tooltip("생산될 아이템 (예: Part_ItemData)")]
    public ItemData outputItem;

    [Tooltip("한 번에 생산될 개수")]
    public int outputAmount = 1;

    #endregion

    #region 제작 비용

    [Header("제작 비용")]
    [Tooltip("제작에 필요한 자원 목록")]
    public List<ResourceCost> requiredMaterials;

    [Tooltip("제작에 걸리는 시간(초)")]
    public float craftingTime = 2f;

    #endregion
}
