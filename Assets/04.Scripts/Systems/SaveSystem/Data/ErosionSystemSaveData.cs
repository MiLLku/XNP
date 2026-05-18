using System;

/// <summary>
/// 침식 시스템 전역 상태 저장 데이터.
/// SaveData.erosionSystem 필드로 사용됩니다.
/// </summary>
[Serializable]
public class ErosionSystemSaveData
{
    /// <summary>포스트 레이드 회복 활성화 여부</summary>
    public bool isPostRaidRecoveryActive;

    /// <summary>포스트 레이드 회복 남은 시간 (초)</summary>
    public float postRaidRecoveryTimer;
}
