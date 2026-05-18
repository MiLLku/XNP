using UnityEngine;

/// <summary>
/// 수확 가능한 오브젝트를 위한 인터페이스
/// </summary>
public interface IHarvestable
{
    /// <summary>
    /// 수확이 가능한지 확인
    /// </summary>
    bool CanHarvest();
    
    /// <summary>
    /// 수확을 실행
    /// </summary>
    void Harvest();
    
    /// <summary>
    /// 수확에 필요한 시간을 반환
    /// </summary>
    float GetHarvestTime();
    
    /// <summary>
    /// 수확 작업 타입을 반환
    /// </summary>
    WorkType GetHarvestType();
}