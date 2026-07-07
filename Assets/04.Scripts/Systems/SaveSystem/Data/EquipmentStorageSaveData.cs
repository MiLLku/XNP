using System;
using System.Collections.Generic;

/// <summary>
/// 장비 보관소 풀 저장 데이터 (v5).
/// 미장착 장비 인스턴스 목록과 다음 인스턴스 ID를 직렬화합니다.
/// </summary>
[Serializable]
public class EquipmentStorageSaveData
{
    public List<EquipmentInstance> instances = new List<EquipmentInstance>();
    public int nextInstanceId = 1;
}
