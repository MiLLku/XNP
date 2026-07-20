using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 레이드 시나리오 템플릿 ScriptableObject.
/// 웨이브 구성, 난이도, 발동 조건을 정의합니다.
///
/// 생성: Create > StampSystem > Raid Data
/// </summary>
[CreateAssetMenu(fileName = "NewRaid", menuName = "StampSystem/Raid Data")]
public class RaidData : ScriptableObject
{
    #region 기본 정보

    [Header("기본 정보")]
    [Tooltip("레이드 고유 ID (프로젝트 내 중복 금지)")]
    public int raidId;

    [Tooltip("레이드 이름 (예: 그레이 타이드)")]
    public string raidName;

    [TextArea(2, 4)]
    [Tooltip("레이드 설명")]
    public string description;

    [Tooltip("난이도")]
    public RaidDifficulty difficulty = RaidDifficulty.Normal;

    #endregion

    #region 웨이브 구성

    [Header("웨이브 구성")]
    [Tooltip("웨이브 목록. 순서대로 스폰됩니다.")]
    public List<RaidWave> waves = new List<RaidWave>();

    #endregion

    #region 발동 조건

    [Header("발동 조건")]
    [Tooltip("이 레이드가 발동될 수 있는 최소 경과 일수 (0 = 제한 없음)")]
    [Min(0)] public int minDayNumber = 0;

    [Tooltip("이 레이드가 발동될 수 있는 최소 직원 수 (0 = 제한 없음)")]
    [Min(0)] public int minEmployeeCount = 0;

    #endregion

    #region 스폰 위치

    [Header("스폰 위치")]
    [Tooltip("스폰 위치 유형:\n" +
             "• FogGround      — 안개 속 지상(하늘 열린 지면, 기본)\n" +
             "• FogUnderground — 안개 속 지하(위가 막힌 공동)\n" +
             "• BaseInterior   — 기지 내부/근처 (안개 무관 — 잠입·내부 발생형)")]
    public RaidSpawnLocation spawnLocation = RaidSpawnLocation.FogGround;

    #endregion

    #region 배수 (진행 상황 스케일링)

    [Header("배수 — 스폰 수량 = entry.count × 배수 (레이드 시작 시점에 계산)")]
    [Tooltip("기본 배수")]
    [Min(0.1f)] public float baseMultiplier = 1f;

    [Tooltip("경과 1일당 가산 배수 (예: 0.1 = 10일 후 +1.0)")]
    [Min(0)] public float multiplierPerDay = 0f;

    [Tooltip("직원 1명당 가산 배수 (예: 0.2 = 직원 5명이면 +1.0)")]
    [Min(0)] public float multiplierPerEmployee = 0f;

    [Tooltip("배수 상한")]
    [Min(0.1f)] public float maxMultiplier = 3f;

    #endregion

    #region 레이드 설정

    [Header("레이드 설정")]
    [Tooltip("레이드 시작 전 경고 연출 시간 (초)")]
    [Min(0)] public float approachDuration = 10f;

    [Tooltip("모든 웨이브 종료 후 철수까지 대기 시간 (초). 0이면 즉시 완료.")]
    [Min(0)] public float retreatDelay = 0f;

    [Tooltip("이 레이드가 EventManager의 랜덤 풀에 포함될지 여부")]
    public bool includeInRandomPool = true;

    #endregion

#if UNITY_EDITOR
    private void OnValidate()
    {
        if (raidId == 0)
            Debug.LogWarning($"[RaidData] '{name}': raidId가 0입니다. 고유 ID를 설정하세요.");
        if (waves == null || waves.Count == 0)
            Debug.LogWarning($"[RaidData] '{raidName}': 웨이브가 없습니다.");
    }
#endif
}

// ─────────────────────────────────────────────────────────────────────────────

/// <summary>레이드 스폰 위치 유형.</summary>
public enum RaidSpawnLocation
{
    /// <summary>안개 속 지상 — 하늘이 열린 지면 위 (기본)</summary>
    FogGround = 0,

    /// <summary>안개 속 지하 — 위가 막힌 공동</summary>
    FogUnderground = 1,

    /// <summary>기지 내부/근처 — 안개 무관 (잠입·내부 발생형)</summary>
    BaseInterior = 2,
}

// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// 레이드 웨이브 하나의 데이터.
/// </summary>
[Serializable]
public class RaidWave
{
    [Tooltip("직전 웨이브 시작 후 이 웨이브 시작까지 대기 시간 (초). 첫 웨이브는 레이드 시작 후 대기 시간.")]
    [Min(0)] public float delayBeforeWave = 0f;

    [Tooltip("이 웨이브에서 스폰할 제놉스 목록")]
    public List<RaidSpawnEntry> entries = new List<RaidSpawnEntry>();
}

// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// 웨이브 내 스폰 단위 데이터.
/// </summary>
[Serializable]
public class RaidSpawnEntry
{
    [Tooltip("스폰할 제놉스 데이터 ID (GameIDRegistry.Xenops 범위: 6000~6999)")]
    public int xenopsDataId;

    [Tooltip("스폰 수량")]
    [Min(1)] public int count = 1;

    [Tooltip("개체 간 스폰 간격 (초). 한 번에 전부 스폰하려면 0으로 설정.")]
    [Min(0)] public float spawnInterval = 0.5f;

    [Tooltip("스폰 위치 오프셋 — 기본 스폰 지점에서의 랜덤 반경 (타일)")]
    [Min(0)] public float spawnRadius = 2f;
}
