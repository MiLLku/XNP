using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 침식체 사육 설정 — Resources/HusbandryConfig에서 읽는다 (HusbandryManager가 게임 씬에서 스스로 생긴다).
/// 값은 임시 수치.
/// </summary>
[CreateAssetMenu(fileName = "HusbandryConfig", menuName = "XNP/Husbandry/Config")]
public class HusbandryConfig : ScriptableObject
{
    [Header("불길한 알")]
    [Tooltip("습격 적을 처치했을 때 알이 떨어질 확률")]
    [Range(0f, 1f)] public float eggDropChance = 0.08f;
    [Tooltip("티어 1·2·3 알 아이템 (순서대로)")]
    public ItemData[] eggByTier = new ItemData[3];
    [Tooltip("티어 2 알을 떨구는 적 제놉스 ID")]
    public List<int> tier2XenopsIds = new List<int>();
    [Tooltip("티어 3 알을 떨구는 적 제놉스 ID — 나머지 습격 적은 티어 1")]
    public List<int> tier3XenopsIds = new List<int>();

    [Header("종")]
    public List<NeutralSpeciesData> species = new List<NeutralSpeciesData>();

    [Header("둥지")]
    [Tooltip("둥지 방 몇 칸당 1마리")]
    [Min(1)] public int cellsPerCreature = 4;
    [Tooltip("소음 판정 반경 (칸) — 제련소·용광로")]
    [Min(1f)] public float noiseRadius = 8f;
    [Tooltip("사건 판정 주기(초)")]
    [Min(1f)] public float eventCheckSeconds = 60f;
    [Tooltip("조건 불충족이 하루 이어질 때의 사건 확률 (판정 1회당, 선형으로 오름)")]
    [Range(0f, 1f)] public float eventChancePerUnmetDay = 0.3f;

    [Header("시체")]
    public ItemData corpseItem;
    [Tooltip("시체가 부패해 사라지기까지 (게임일)")]
    [Min(0.1f)] public float corpseDecayDays = 2f;

    public NeutralSpeciesData GetSpecies(int id) => species.Find(s => s != null && s.speciesId == id);

    public int EggTierFor(int xenopsId) => tier3XenopsIds.Contains(xenopsId) ? 3 : tier2XenopsIds.Contains(xenopsId) ? 2 : 1;

    /// <summary>알 아이템 → 티어 (알이 아니면 0)</summary>
    public int TierOfEgg(ItemData item)
    {
        for (int i = 0; i < eggByTier.Length; i++) if (eggByTier[i] != null && eggByTier[i] == item) return i + 1;
        return 0;
    }

    /// <summary>티어 풀에서 무작위 종</summary>
    public NeutralSpeciesData RollSpecies(int tier)
    {
        var pool = species.FindAll(s => s != null && s.tier == tier);
        return pool.Count > 0 ? pool[Random.Range(0, pool.Count)] : null;
    }

    public NeutralSpeciesData SpeciesOfHatchling(ItemData item) => species.Find(s => s != null && s.hatchlingItem == item);
}
