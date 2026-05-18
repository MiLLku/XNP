using UnityEngine;

[CreateAssetMenu(fileName = "BuildingUnlock", menuName = "Research/Effects/Building Unlock")]
public class ResearchBuildingUnlockEffect : ResearchUnlockEffect
{
    public BuildingData building;

    public override void Apply()
    {
        if (building == null) return;
        ResearchTreeManager.instance?.RegisterBuildingUnlock(building);
    }

    public override string GetDescription()
        => building != null ? $"건물 해금: {building.buildingName}" : "건물 해금 (미설정)";
}
