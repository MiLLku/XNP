using System;
using System.Collections.Generic;

/// <summary>
/// 레이드 시스템 상태 저장 데이터.
/// SaveData.raidSystem 필드로 사용됩니다.
/// </summary>
[Serializable]
public class RaidSystemSaveData
{
    /// <summary>현재 레이드 상태 (RaidState enum 정수값)</summary>
    public int raidState;

    /// <summary>활성 레이드 ID (-1 = 없음)</summary>
    public int activeRaidId = -1;

    /// <summary>현재 웨이브 인덱스</summary>
    public int currentWaveIndex;

    /// <summary>지난 습격 시각·다음 자동 습격 시각 (게임일, 구 세이브에는 없음 — -1이면 새로 잡음)</summary>
    public float lastRaidGameDay = -1f;
    public float nextRaidGameDay = -1f;

    /// <summary>다음 웨이브까지 남은 대기 시간 (초)</summary>
    public float waveTimer;

    /// <summary>
    /// 다 내보낸 웨이브 수와 진행 중 웨이브에서 이미 내보낸 마리 수 — 불러오면 남은 몫을 이어서 내보낸다.
    /// -1 = 기록 없음(구 세이브) → 예전처럼 남은 적 감시만 한다.
    /// </summary>
    public int wavesSpawned = -1;
    public int spawnedInWave;

    /// <summary>아직 나오지 않은 스폰 계획 (planWave[i] 웨이브의 planEntries[i])</summary>
    public List<RaidSpawnEntry> planEntries = new List<RaidSpawnEntry>();
    public List<int> planWave = new List<int>();

    /// <summary>이번 레이드에서 스폰된 제놉스 인스턴스 ID 목록</summary>
    public List<int> spawnedEntityIds = new List<int>();

    /// <summary>닫힌 거점·스포너 습격이 남긴 구조물·호위·스포너 적 (구 세이브에는 없음 — 비어 있음)</summary>
    public List<int> lingeringIds = new List<int>();
    public int lingeringStage;

    /// <summary>이번 레이드의 스폰 수량 배수 (시작 시점 계산값 보존)</summary>
    public float activeMultiplier = 1f;

    /// <summary>진행 중인 습격의 종류 (RaidType 정수값, 구 세이브에는 없음 — 0 = 일반)</summary>
    public int raidType;

    /// <summary>정화 가동 중 방해 습격인지 (다른 층 시설을 노림) · 노리는 지점 · 층 이름 — 없던 세이브는 false</summary>
    public bool disruption;
    public float disruptionX;
    public float disruptionY;
    public string disruptionLayer;

    /// <summary>진행 중인 습격의 위협 단계 (구 세이브에는 없음 — 0)</summary>
    public int threatStage;

    /// <summary>최근 습격 종류 (RaidType 정수값, 0번 = 바로 직전) — 구 세이브에는 없음</summary>
    public List<int> recentRaidTypes = new List<int>();

    /// <summary>위장 중인 위장체(직원 복제체) — 직원 목록 밖이라 여기 저장</summary>
    public List<DisguiseSaveData> disguises = new List<DisguiseSaveData>();

    public RaidSystemSaveData()
    {
        activeRaidId = -1;
        spawnedEntityIds = new List<int>();
    }
}

/// <summary>위장 중인 위장체 하나 — 복제체 직원 데이터·원본 직원·경고 쿨다운</summary>
[Serializable]
public class DisguiseSaveData
{
    public EmployeeSaveData employee;
    public int sourceEmployeeId;
    public float alertCooldown;
}
