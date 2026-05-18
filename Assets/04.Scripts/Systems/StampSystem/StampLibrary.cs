using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 스탬프 라이브러리 ScriptableObject.
/// 등록된 스탬프를 키로 빠르게 조회할 수 있습니다.
/// </summary>
[CreateAssetMenu(fileName = "StampLibrary", menuName = "StampSystem/StampLibrary")]
public class StampLibrary : ScriptableObject
{
    [SerializeField]
    private List<StampData> stampList = new List<StampData>();

    /// <summary>키 기반 빠른 조회용 딕셔너리 (런타임)</summary>
    private Dictionary<string, StampData> _stampLookup;

    private void OnEnable()
    {
        _stampLookup = new Dictionary<string, StampData>();
        foreach (var stamp in stampList)
        {
            if (stamp != null && !string.IsNullOrEmpty(stamp.key))
            {
                _stampLookup[stamp.key] = stamp;
            }
        }
    }

    /// <summary>
    /// 키로 스탬프 데이터를 조회합니다.
    /// </summary>
    /// <param name="key">스탬프 고유 키</param>
    /// <returns>스탬프 데이터 (없으면 null)</returns>
    public StampData GetStamp(string key)
    {
        if (_stampLookup == null) OnEnable();
        _stampLookup.TryGetValue(key, out StampData data);
        return data;
    }

    /// <summary>
    /// 모든 스탬프 리스트를 반환합니다 (에디터용).
    /// </summary>
    public List<StampData> GetAllStamps()
    {
        return stampList;
    }

    /// <summary>
    /// 스탬프 리스트를 통째로 교체합니다 (에디터용).
    /// </summary>
    /// <param name="stamps">새 스탬프 리스트</param>
    public void SetStamps(List<StampData> stamps)
    {
        stampList = stamps;
        OnEnable();
    }
}
