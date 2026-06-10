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

    public bool HasAnyStockpile => _stockpiles.Any(s => s != null && s.IsOperational);

    /// <summary>
    /// 지정 타일에서 가장 가까운, 해당 아이템을 충분히 보유한 운영 중 Stockpile을 반환합니다.
    /// 출고(Withdraw) 작업에서 사용합니다.
    /// </summary>
    public Stockpile GetNearestStockpileWith(Vector2Int from, ItemData item, int amount)
    {
        if (item == null || amount <= 0) return null;

        return _stockpiles
            .Where(s => s != null && s.IsOperational && s.HasItem(item, amount))
            .OrderBy(s => Vector2Int.Distance(from,
                new Vector2Int(
                    Mathf.FloorToInt(s.transform.position.x),
                    Mathf.FloorToInt(s.transform.position.y)
                )))
            .FirstOrDefault();
    }
}
