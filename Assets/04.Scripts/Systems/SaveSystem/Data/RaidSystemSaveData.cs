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

    /// <summary>다음 웨이브까지 남은 대기 시간 (초)</summary>
    public float waveTimer;

    /// <summary>이번 레이드에서 스폰된 제놉스 인스턴스 ID 목록</summary>
    public List<int> spawnedEntityIds = new List<int>();

    /// <summary>이번 레이드의 스폰 수량 배수 (시작 시점 계산값 보존)</summary>
    public float activeMultiplier = 1f;

    public RaidSystemSaveData()
    {
        activeRaidId = -1;
        spawnedEntityIds = new List<int>();
    }
}
