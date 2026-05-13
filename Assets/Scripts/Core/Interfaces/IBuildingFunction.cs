using UnityEngine;

/// <summary>
/// 건물 기능을 가진 컴포넌트를 위한 인터페이스
/// CraftingTable, Furnace 등이 구현해야 합니다.
/// </summary>
public interface IBuildingFunction
{
    /// <summary>
    /// 건물이 비활성화될 때 호출됩니다 (기반 파괴 등).
    /// </summary>
    void OnBuildingDisabled();
    
    /// <summary>
    /// 건물이 다시 활성화될 때 호출됩니다 (기반 복구 등).
    /// </summary>
    void OnBuildingEnabled();
    
    /// <summary>
    /// 현재 작동 중인지 여부를 반환합니다.
    /// </summary>
    bool IsOperating { get; }
}