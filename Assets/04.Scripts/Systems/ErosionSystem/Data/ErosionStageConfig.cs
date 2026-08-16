using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 침식 단계별 설정 ScriptableObject.
/// 인스펙터에서 5단계(Normal~FullErosion)의 임계값, 속도 디버프, 전파 오라를 설정합니다.
///
/// <b>이상행동 파라미터는 2026-07-29 개편으로 제거됐습니다.</b>
/// 정신 이상 발생 판정은 EmployeeMental이 정신 수치로 하며(기준값은 MentalBreakConfig),
/// 침식 수치는 "발생한 정신 이상이 침식 계열일 확률"만 결정합니다.
/// 침식 단계가 관여하는 것은 속도 디버프·전파 오라·변이뿐입니다.
///
/// 생성: Create > StampSystem > Erosion Stage Config
/// </summary>
[CreateAssetMenu(fileName = "ErosionStageConfig", menuName = "StampSystem/Erosion Stage Config")]
public class ErosionStageConfig : ScriptableObject
{
    [Tooltip("침식 단계별 정의 목록. ErosionStage 열거형 순서(0~4)대로 작성하세요.")]
    public List<ErosionStageDefinition> stages = new List<ErosionStageDefinition>();

    /// <summary>
    /// erosionLevel에 해당하는 단계 정의를 반환합니다.
    /// 매칭되는 단계가 없으면 Normal(0)을 반환합니다.
    /// </summary>
    public ErosionStageDefinition GetStageDefinition(float erosionLevel)
    {
        for (int i = stages.Count - 1; i >= 0; i--)
        {
            if (erosionLevel >= stages[i].minErosionLevel)
                return stages[i];
        }
        return stages.Count > 0 ? stages[0] : null;
    }

    /// <summary>
    /// 특정 단계의 정의를 반환합니다.
    /// </summary>
    public ErosionStageDefinition GetStageDefinition(ErosionStage stage)
    {
        foreach (var def in stages)
        {
            if (def.stage == stage)
                return def;
        }
        return null;
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        if (stages.Count != 5)
        {
            Debug.LogWarning($"[ErosionStageConfig] 단계 수가 5개가 아닙니다 (현재: {stages.Count}). Normal~FullErosion 순서로 5개를 설정하세요.");
        }
    }
#endif
}

// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// 침식 단계 하나의 설정 데이터.
/// </summary>
[Serializable]
public class ErosionStageDefinition
{
    [Header("단계 식별")]
    [Tooltip("이 정의가 대응하는 침식 단계")]
    public ErosionStage stage;

    [Tooltip("이 단계의 최소 침식 수치 (이상이면 이 단계)")]
    [Min(0)] public float minErosionLevel;

    [Header("스탯 디버프")]
    [Tooltip("작업 속도 배율. 1.0 = 정상, 0.5 = -50%")]
    [Range(0f, 1f)] public float workSpeedModifier = 1f;

    [Tooltip("이동 속도 배율. 1.0 = 정상, 0.7 = -30%")]
    [Range(0f, 1f)] public float moveSpeedModifier = 1f;

    [Header("침식 전파 오라 (4단계 전용)")]
    [Tooltip("이 직원에서 인근 직원에게 침식 오라를 전파할지 여부")]
    public bool hasErosionAura = false;

    [Tooltip("초당 전파 침식량")]
    [Min(0)] public float auraErosionPerSecond = 0f;

    [Tooltip("전파 오라 반경 (타일)")]
    [Min(0)] public float auraRange = 0f;
}
