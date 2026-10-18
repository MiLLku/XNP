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
        => recipe != null ? $"레시피 해금: {recipe.outputItem?.itemName}{CodexSuffix()}" : "레시피 해금 (미설정)";

    private string CodexSuffix()
    {
        if (recipe.requiredCodexXenopsId == 0) return "";
        var xd = GameDatabase.Instance != null ? GameDatabase.Instance.GetXenopsData(recipe.requiredCodexXenopsId) : null;
        return $" ({(xd != null ? xd.xenopsName : "제놉스")} 도감 100%)";
    }
}
