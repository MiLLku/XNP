using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 초기 연구 트리 에셋을 자동 생성하는 에디터 유틸리티.
/// 메뉴: Tools > Research > Generate Initial Research Tree
///
/// 생성 경로: Assets/Data/Research/
///   Nodes/   - 개별 ResearchNodeData 에셋
///   Effects/ - ResearchStatBonusEffect 에셋
///   ResearchTreeConfig.asset - 트리 전체 구성
///
/// 주의: 자원 비용(ResourceCost)과 건물/레시피 해금 효과는 에디터에서 수동으로 설정해야 합니다.
/// </summary>
public static class ResearchTreeGenerator
{
    private const string RootPath = "Assets/Data/Research";
    private const string NodesPath = "Assets/Data/Research/Nodes";
    private const string EffectsPath = "Assets/Data/Research/Effects";

    [MenuItem("Tools/Research/Generate Initial Research Tree")]
    public static void Generate()
    {
        EnsureDirectories();

        var created = new Dictionary<string, ResearchNodeData>();

        // ── Tier 0: 루트 ─────────────────────────────────────────────────────

        var root = Node("research_basics", "연구 방법론 기초",
            "연구의 체계적 접근법을 정립합니다. 모든 연구의 시작점.",
            prereqs: new string[] { },
            pointCost: 50f,
            pos: new Vector2(0, 0),
            effects: new[] { StatEffect("fx_research_speed_1", ResearchStatType.ResearchSpeedBonus, 0.1f) }
        );
        created[root.nodeId] = root;

        // ── Tier 1: 4개 브랜치 시작 ───────────────────────────────────────────

        var survivalBasics = Node("survival_basics", "생존 기초",
            "극한 환경에서 직원이 생존할 수 있는 기초 지식을 연구합니다.",
            prereqs: new[] { "research_basics" },
            pointCost: 80f,
            pos: new Vector2(-4, -1),
            effects: new[] { StatEffect("fx_maxhealth_1", ResearchStatType.EmployeeMaxHealthBonus, 10f) }
        );
        created[survivalBasics.nodeId] = survivalBasics;

        var constructionBasics = Node("construction_basics", "건설 기초",
            "효율적인 건설 공법을 연구합니다. 건설 속도가 향상됩니다.",
            prereqs: new[] { "research_basics" },
            pointCost: 80f,
            pos: new Vector2(-1, -1),
            effects: new[] { StatEffect("fx_construction_1", ResearchStatType.ConstructionSpeedBonus, 0.15f) }
        );
        created[constructionBasics.nodeId] = constructionBasics;

        var craftingBasics = Node("crafting_basics", "제작 기초",
            "제작 공정을 최적화하는 기초 기술을 연구합니다.",
            prereqs: new[] { "research_basics" },
            pointCost: 80f,
            pos: new Vector2(2, -1),
            effects: new[] { StatEffect("fx_crafting_1", ResearchStatType.CraftingSpeedBonus, 0.15f) }
        );
        created[craftingBasics.nodeId] = craftingBasics;

        var xenopsEcology = Node("xenops_ecology", "제노프스 생태학",
            "제노프스의 생태와 특성을 체계적으로 연구합니다. 침식 피해에 대한 기초 저항력이 생깁니다.",
            prereqs: new[] { "research_basics" },
            pointCost: 120f,
            pos: new Vector2(5, -1),
            effects: new[] { StatEffect("fx_erosion_1", ResearchStatType.ErosionResistanceBonus, 0.1f) }
        );
        created[xenopsEcology.nodeId] = xenopsEcology;

        // ── Tier 2: 생존 브랜치 ───────────────────────────────────────────────

        var harvestOpt = Node("harvest_opt", "채집 최적화",
            "식물 채집 효율을 높이는 기술을 연구합니다. 수확량이 증가합니다.",
            prereqs: new[] { "survival_basics" },
            pointCost: 120f,
            pos: new Vector2(-5, -2),
            effects: new[] { StatEffect("fx_harvest_1", ResearchStatType.HarvestYieldBonus, 0.2f) }
        );
        created[harvestOpt.nodeId] = harvestOpt;

        var fieldMedicine = Node("field_medicine", "야전 의학",
            "전장 환경에서 직원의 체력을 유지하는 의술을 연구합니다. 최대 체력 및 침식 저항이 향상됩니다.",
            prereqs: new[] { "survival_basics" },
            pointCost: 100f,
            pos: new Vector2(-3, -2),
            effects: new ResearchUnlockEffect[]
            {
                StatEffect("fx_maxhealth_2", ResearchStatType.EmployeeMaxHealthBonus, 15f),
                StatEffect("fx_erosion_2", ResearchStatType.ErosionResistanceBonus, 0.1f)
            }
        );
        created[fieldMedicine.nodeId] = fieldMedicine;

        // ── Tier 2: 건설 브랜치 ───────────────────────────────────────────────

        var metalStructures = Node("metal_structures", "금속 구조물",
            "금속 재료를 이용한 건축 기법을 연구합니다. 건설 속도 향상 및 금속 건물이 해금됩니다.",
            prereqs: new[] { "construction_basics" },
            pointCost: 150f,
            pos: new Vector2(-1, -2),
            effects: new[] { StatEffect("fx_construction_2", ResearchStatType.ConstructionSpeedBonus, 0.1f) }
            // TODO: BuildingData 참조 추가 (ResearchBuildingUnlockEffect)
        );
        created[metalStructures.nodeId] = metalStructures;

        // ── Tier 2: 제작 브랜치 ───────────────────────────────────────────────

        var smeltingTech = Node("smelting_tech", "제련 기술",
            "고온 제련 공정을 연구합니다. 제작 속도 향상 및 고급 제련 레시피가 해금됩니다.",
            prereqs: new[] { "crafting_basics" },
            pointCost: 120f,
            pos: new Vector2(1, -2),
            effects: new[] { StatEffect("fx_crafting_2", ResearchStatType.CraftingSpeedBonus, 0.1f) }
            // TODO: CraftingRecipe 참조 추가 (ResearchRecipeUnlockEffect)
        );
        created[smeltingTech.nodeId] = smeltingTech;

        var alchemyBasics = Node("alchemy_basics", "연금술 기초",
            "이질적인 물질의 혼합과 반응을 연구합니다. 연금술 작업대 레시피가 해금됩니다.",
            prereqs: new[] { "crafting_basics" },
            pointCost: 150f,
            pos: new Vector2(3, -2),
            effects: System.Array.Empty<ResearchUnlockEffect>()
            // TODO: AlchemyTable 레시피 참조 추가
        );
        created[alchemyBasics.nodeId] = alchemyBasics;

        // ── Tier 2: 제노프스 브랜치 ──────────────────────────────────────────

        var erosionResistance = Node("erosion_resistance", "침식 저항 연구",
            "제노프스가 유발하는 침식에 저항하는 체계를 연구합니다. 직원의 침식 피해가 크게 감소합니다.",
            prereqs: new[] { "xenops_ecology" },
            pointCost: 150f,
            pos: new Vector2(4, -2),
            effects: new[] { StatEffect("fx_erosion_3", ResearchStatType.ErosionResistanceBonus, 0.2f) }
        );
        created[erosionResistance.nodeId] = erosionResistance;

        var xenopsSuppression = Node("xenops_suppression", "제노프스 억제",
            "제노프스를 효과적으로 제압하는 기술을 연구합니다. 직원의 전투력이 향상됩니다.",
            prereqs: new[] { "xenops_ecology" },
            pointCost: 180f,
            pos: new Vector2(6, -2),
            effects: new[] { StatEffect("fx_attack_1", ResearchStatType.EmployeeAttackPowerBonus, 10f) }
        );
        created[xenopsSuppression.nodeId] = xenopsSuppression;

        // ── Tier 3 ────────────────────────────────────────────────────────────

        var advancedGardening = Node("advanced_gardening", "고급 원예술",
            "식물 재배 환경을 최적화하는 고급 기술을 연구합니다. 수확량이 크게 증가합니다.",
            prereqs: new[] { "harvest_opt" },
            pointCost: 150f,
            pos: new Vector2(-5, -3),
            effects: new[] { StatEffect("fx_harvest_2", ResearchStatType.HarvestYieldBonus, 0.3f) }
        );
        created[advancedGardening.nodeId] = advancedGardening;

        var reinforcedConstruction = Node("reinforced_construction", "강화 건축",
            "내구성이 향상된 건축 공법을 연구합니다. 건설 속도가 크게 향상됩니다.",
            prereqs: new[] { "metal_structures" },
            pointCost: 200f,
            pos: new Vector2(-1, -3),
            effects: new[] { StatEffect("fx_construction_3", ResearchStatType.ConstructionSpeedBonus, 0.2f) }
        );
        created[reinforcedConstruction.nodeId] = reinforcedConstruction;

        var xenopsUtilization = Node("xenops_utilization", "제노프스 활용",
            "제압한 제노프스를 자원으로 활용하는 기술을 연구합니다.",
            prereqs: new[] { "xenops_suppression" },
            pointCost: 250f,
            pos: new Vector2(6, -3),
            effects: System.Array.Empty<ResearchUnlockEffect>()
            // TODO: 제노프스 활용 효과 (장비형 슬롯 해금 등) 추가
        );
        created[xenopsUtilization.nodeId] = xenopsUtilization;

        // ── ResearchTreeConfig 생성 ───────────────────────────────────────────

        var configPath = $"{RootPath}/ResearchTreeConfig.asset";
        var config = AssetDatabase.LoadAssetAtPath<ResearchTreeConfig>(configPath)
                     ?? ScriptableObject.CreateInstance<ResearchTreeConfig>();

        config.nodes.Clear();
        foreach (var node in created.Values)
            config.nodes.Add(node);

        if (AssetDatabase.LoadAssetAtPath<ResearchTreeConfig>(configPath) == null)
            AssetDatabase.CreateAsset(config, configPath);
        else
            EditorUtility.SetDirty(config);

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Debug.Log($"[ResearchTreeGenerator] 연구 트리 생성 완료: {created.Count}개 노드, 경로: {RootPath}");
    }

    // ── 헬퍼 ──────────────────────────────────────────────────────────────────

    private static void EnsureDirectories()
    {
        if (!AssetDatabase.IsValidFolder("Assets/Data"))
            AssetDatabase.CreateFolder("Assets", "Data");
        if (!AssetDatabase.IsValidFolder(RootPath))
            AssetDatabase.CreateFolder("Assets/Data", "Research");
        if (!AssetDatabase.IsValidFolder(NodesPath))
            AssetDatabase.CreateFolder(RootPath, "Nodes");
        if (!AssetDatabase.IsValidFolder(EffectsPath))
            AssetDatabase.CreateFolder(RootPath, "Effects");
    }

    private static ResearchNodeData Node(
        string id, string name, string desc,
        string[] prereqs, float pointCost, Vector2 pos,
        ResearchUnlockEffect[] effects)
    {
        var path = $"{NodesPath}/{id}.asset";
        var node = AssetDatabase.LoadAssetAtPath<ResearchNodeData>(path)
                   ?? ScriptableObject.CreateInstance<ResearchNodeData>();

        node.nodeId = id;
        node.nodeName = name;
        node.description = desc;
        node.prerequisiteNodeIds = new List<string>(prereqs);
        node.researchPointCost = pointCost;
        node.treePosition = pos;
        node.resourceCosts = new List<ResourceCost>();
        node.unlockEffects = new List<ResearchUnlockEffect>(effects);

        if (AssetDatabase.LoadAssetAtPath<ResearchNodeData>(path) == null)
            AssetDatabase.CreateAsset(node, path);
        else
            EditorUtility.SetDirty(node);

        return node;
    }

    private static ResearchStatBonusEffect StatEffect(string assetName, ResearchStatType statType, float value)
    {
        var path = $"{EffectsPath}/{assetName}.asset";
        var effect = AssetDatabase.LoadAssetAtPath<ResearchStatBonusEffect>(path)
                     ?? ScriptableObject.CreateInstance<ResearchStatBonusEffect>();

        effect.statType = statType;
        effect.bonusValue = value;

        if (AssetDatabase.LoadAssetAtPath<ResearchStatBonusEffect>(path) == null)
            AssetDatabase.CreateAsset(effect, path);
        else
            EditorUtility.SetDirty(effect);

        return effect;
    }
}
