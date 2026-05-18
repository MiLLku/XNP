using System;

/// <summary>
/// 게임 시간(DayCycle) 저장 데이터.
/// </summary>
[Serializable]
public class DayCycleSaveData
{
    /// <summary>경과 일수 (1일차부터)</summary>
    public int day = 1;

    /// <summary>하루 내 경과 비율 (0.0 = 자정, 0.5 = 정오)</summary>
    public float timeNormalized = 0f;
}
