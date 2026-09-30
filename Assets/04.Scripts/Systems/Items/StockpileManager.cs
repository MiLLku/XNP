using System.Collections.Generic;
using System.Linq;
using UnityEngine;

/// <summary>
/// 창고(Stockpile) 건물 위치를 추적하는 싱글톤.
/// 직원이 운반 완료 후 가장 가까운 창고를 조회하는 데 사용됩니다.
/// </summary>
public class StockpileManager : DestroySingleton<StockpileManager>
{
    private readonly List<Stockpile> _stockpiles = new();

    // ── 등록 ────────────────────────────────────────────────────────────────

    public void Register(Stockpile stockpile)
    {
        if (stockpile == null || _stockpiles.Contains(stockpile)) return;
        _stockpiles.Add(stockpile);
        Debug.Log($"[StockpileManager] 창고 등록: {stockpile.gameObject.name} @ {stockpile.transform.position}");
    }

    public void Unregister(Stockpile stockpile)
    {
        _stockpiles.Remove(stockpile);
    }

    // ── 쿼리 ────────────────────────────────────────────────────────────────

    /// <summary>
    /// 지정 타일에서 가장 가까운 운영 중인 Stockpile을 반환합니다.
    /// 창고가 없으면 null.
    /// </summary>
    public Stockpile GetNearestStockpile(Vector2Int from)
    {
        return _stockpiles
            .Where(s => s != null && s.IsOperational)
            .OrderBy(s => Vector2Int.Distance(from,
                new Vector2Int(
                    Mathf.FloorToInt(s.transform.position.x),
                    Mathf.FloorToInt(s.transform.position.y)
                )))
            .FirstOrDefault();
    }

    /// <summary>
    /// from에서 닿을 수 있는 가장 가까운 운영 중 Stockpile. 창고는 있어도 전부 닿을 수 없으면 null.
    /// (GetNearestStockpile의 null은 "창고 자체가 없음"이라 인벤토리 직행 폴백과 구분해야 한다)
    /// </summary>
    public Stockpile GetNearestReachableStockpile(Vector2Int from)
    {
        return _stockpiles
            .Where(s => s != null && s.IsOperational && ReachabilityMap.CanReach(from, s.GetDepositPosition()))
            .OrderBy(s => Vector2Int.Distance(from,
                new Vector2Int(
                    Mathf.FloorToInt(s.transform.position.x),
                    Mathf.FloorToInt(s.transform.position.y)
                )))
            .FirstOrDefault();
    }

    /// <summary>from에서 닿을 수 있는 운영 중 Stockpile이 하나라도 있는지.</summary>
    public bool HasReachableStockpile(Vector2Int from)
        => _stockpiles.Any(s => s != null && s.IsOperational && ReachabilityMap.CanReach(from, s.GetDepositPosition()));

    public bool HasAnyStockpile => _stockpiles.Any(s => s != null && s.IsOperational);

    /// <summary>
    /// 운영 중인 창고 중 해당 아이템을 amount 이상 보유한 곳이 하나라도 있는지 확인합니다.
    /// 출고(Withdraw) 작업 가용성 판정에 사용 — 자재가 없으면 직원을 보내지 않아
    /// 헛걸음·재시도 스팸을 방지합니다. (GetNearestStockpileWith의 위치 정렬 없는 경량 버전)
    /// </summary>
    public bool HasItemAnywhere(ItemData item, int amount)
    {
        if (item == null || amount <= 0) return false;
        return _stockpiles.Any(s => s != null && s.IsOperational && s.HasItem(item, amount));
    }

    /// <summary>
    /// 지정 타일에서 가장 가까운, 해당 아이템을 충분히 보유한 운영 중 Stockpile을 반환합니다.
    /// 출고(Withdraw) 작업에서 사용합니다.
    /// </summary>
    public Stockpile GetNearestStockpileWith(Vector2Int from, ItemData item, int amount)
    {
        if (item == null || amount <= 0) return null;

        // 닿을 수 없는 창고는 후보에서 뺀다 — 가장 가까워도 못 가면 헛걸음 후 실패만 반복한다
        return _stockpiles
            .Where(s => s != null && s.IsOperational && s.HasItem(item, amount)
                        && ReachabilityMap.CanReach(from, s.GetDepositPosition()))
            .OrderBy(s => Vector2Int.Distance(from,
                new Vector2Int(
                    Mathf.FloorToInt(s.transform.position.x),
                    Mathf.FloorToInt(s.transform.position.y)
                )))
            .FirstOrDefault();
    }
}
