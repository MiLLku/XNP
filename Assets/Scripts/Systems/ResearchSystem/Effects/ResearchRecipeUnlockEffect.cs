using UnityEngine;

[CreateAssetMenu(fileName = "RecipeUnlock", menuName = "Research/Effects/Recipe Unlock")]
public class ResearchRecipeUnlockEffect : ResearchUnlockEffect
{
    public CraftingRecipe recipe;

    public override void Apply()
    {
        if (recipe == null) return;
        ResearchTreeManager.instance?.RegisterRecipeUnlock(recipe);
    }

    public override string GetDescription()
        => recipe != null ? $"레시피 해금: {recipe.outputItem?.itemName}" : "레시피 해금 (미설정)";
}
