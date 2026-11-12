using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 장비 품질 수치 (Resources/EquipmentQualityConfig.asset).
/// 배열 순서는 품질 낮은 순: 끔찍함 · 평범함 · 완벽함 · 뛰어남 · 전설적
/// </summary>
[CreateAssetMenu(fileName = "EquipmentQualityConfig", menuName = "XNP/장비 품질 설정")]
public class EquipmentQualityConfig : ScriptableObject
{
    [System.Serializable]
    public class StationTier
    {
        public int buildingId;
        [Tooltip("제작 점수에 더하는 값 (1단계 0 · 2단계 3 · 3단계 6)")]
        public float bonus;
    }

    [Header("제작 점수 P = 제작 적성 레벨(1~10) + 건물 단계 보너스 + 부속 보너스")]
    [Tooltip("장비를 만드는 건물의 단계 보너스 — 목록에 없는 건물은 0")]
    public List<StationTier> stationTiers = new List<StationTier>();
    [Tooltip("같은 방 부속 건물 종류당 (공구 선반·작업 조명 등)")]
    public float addonBonusPerKind = 2f;
    public float maxAddonBonus = 4f;
    [Tooltip("확률 보간의 상한 점수")]
    public float maxScore = 20f;

    [Header("확률 (%) — 끔찍함 · 평범함 · 완벽함 · 뛰어남 · 전설적, 사이는 선형 보간")]
    public float[] chanceAtMinScore = { 35f, 55f, 9f, 1f, 0f };
    public float[] chanceAtMaxScore = { 0f, 25f, 40f, 27f, 8f };

    [Header("성능(피해·피해 감소·견딜 수 있는 침식/온도)과 최대 내구도 배율")]
    public float[] multipliers = { 0.8f, 1f, 1.1f, 1.25f, 1.5f };

    public float TierBonusOf(int buildingId)
    {
        foreach (var t in stationTiers) if (t != null && t.buildingId == buildingId) return t.bonus;
        return 0f;
    }
}
