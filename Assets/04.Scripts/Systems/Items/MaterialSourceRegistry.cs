using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 창고 밖에 있는 자재 공급원(<see cref="IMaterialSource"/>)을 추적하는 싱글톤.
///
/// 여기 등록되는 것은 <b>창고가 아닌</b> 소스입니다 — 생산 건물의 산출물 보관함,
/// 바닥에 떨어진 더미 등. 창고는 <see cref="StockpileManager"/>가 따로 관리하며
/// 전역 인벤토리 하나를 공유하므로, 두 쪽을 합산해도 중복 계산되지 않습니다.
///
/// 쓰임새:
///   1. 제작·건설이 자재가 충분한지 볼 때 (InventoryManager.GetWorldAvailable)
///   2. 직원이 자재를 가지러 갈 곳을 고를 때 (EmployeeWork.WithdrawWorkAsync)
///
/// 자동 운반이 꺼진 건물은 IsSourceAvailable이 false라 자동으로 제외됩니다.
/// </summary>
public class MaterialSourceRegistry : DestroySingleton<MaterialSourceRegistry>
{
    private readonly List<IMaterialSource> _sources = new();

    #region 등록

    public void Register(IMaterialSource source)
    {
        if (source == null || _sources.Contains(source)) return;
        _sources.Add(source);
    }

    public void Unregister(IMaterialSource source)
    {
        if (source == null) return;
        _sources.Remove(source);
    }

    #endregion

    #region 조회

    /// <summary>
    /// 창고 밖에 있는 해당 자재의 총량. 파괴된 소스는 훑는 김에 정리합니다.
    /// </summary>
    public int GetTotalAvailable(ItemData item)
    {
        if (item == null) return 0;

        int total = 0;
        for (int i = _sources.Count - 1; i >= 0; i--)
        {
            var src = _sources[i];
            if (!IsAlive(src)) { _sources.RemoveAt(i); continue; }
            if (!src.IsSourceAvailable) continue;

            total += src.GetStoredAmount(item);
        }
        return total;
    }

    /// <summary>
    /// 지정 타일에서 가장 가까운, 해당 자재를 요청량만큼 보유한 소스를 반환합니다.
    /// 없으면 null.
    /// </summary>
    public IMaterialSource FindNearestWith(Vector2Int from, ItemData item, int amount)
    {
        if (item == null || amount <= 0) return null;

        IMaterialSource best = null;
        float bestDist = float.MaxValue;

        for (int i = _sources.Count - 1; i >= 0; i--)
        {
            var src = _sources[i];
            if (!IsAlive(src)) { _sources.RemoveAt(i); continue; }
            if (!src.IsSourceAvailable) continue;
            if (src.GetStoredAmount(item) < amount) continue;

            Vector3 pos = src.GetWithdrawPosition();
            float dist = Vector2Int.Distance(from,
                new Vector2Int(Mathf.FloorToInt(pos.x), Mathf.FloorToInt(pos.y)));

            if (dist < bestDist) { best = src; bestDist = dist; }
        }

        return best;
    }

    /// <summary>해당 자재를 요청량만큼 가진 소스가 하나라도 있는지 (경량 판정).</summary>
    public bool HasItemAnywhere(ItemData item, int amount)
    {
        if (item == null || amount <= 0) return false;

        for (int i = _sources.Count - 1; i >= 0; i--)
        {
            var src = _sources[i];
            if (!IsAlive(src)) { _sources.RemoveAt(i); continue; }
            if (!src.IsSourceAvailable) continue;
            if (src.GetStoredAmount(item) >= amount) return true;
        }
        return false;
    }

    #endregion

    /// <summary>
    /// 소스가 아직 살아 있는지.
    /// MonoBehaviour 구현체는 파괴 시 Unity의 가짜 null이 되므로 Object로 캐스팅해 확인합니다.
    /// </summary>
    private static bool IsAlive(IMaterialSource src)
    {
        if (src == null) return false;
        var obj = src as UnityEngine.Object;
        return obj != null;
    }
}
