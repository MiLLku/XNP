using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 파종 예정지 — 건설 현장과 같은 2단계 흐름입니다.
///   1) 묘목 1개를 예약하고 <b>운반 작업</b>(WithdrawOrder)으로 이곳까지 가져옵니다.
///   2) 도착하면 <b>파종 작업</b>(WorkType.Sowing, <see cref="SowOrder"/>)을 등록하고,
///      원예가 가능한 직원이 심으면 개체가 성장도 0으로 생깁니다.
///
/// 예약·환불 규칙은 ConstructionSite와 같습니다 — 출고 시점에 창고에서 이미 빠지므로
/// 완료 시에는 예약 잠금만 풀고, 도착 후 취소되면 묘목을 바닥에 떨어뜨려 돌려줍니다.
/// </summary>
/// <summary>
/// 파종 예정지의 주인 — 밭·수경재배기의 재배 칸. 지정되면 심은 결과를 월드 개체가 아니라 주인에게 넘깁니다.
/// </summary>
public interface ISowOwner
{
    /// <summary>작물이 놓이는 위치</summary>
    Vector3 SowPosition { get; }

    /// <summary>직원이 서서 씨앗을 인계하고 심는 위치 (설 수 있는 칸)</summary>
    Vector3 WorkPosition { get; }

    /// <summary>지금 심을 수 있는지 (수경재배기: 결정체 보유)</summary>
    bool CanSowNow { get; }

    /// <summary>파종 완료 — 주인이 작물을 만듭니다</summary>
    void OnSown(ItemData seed);
}

public class SowSite : MonoBehaviour, IMaterialReceiver
{
    /// <summary>활성 파종 예정지 전체 (칸 중복 검사·세이브용)</summary>
    public static readonly List<SowSite> All = new List<SowSite>();

    private const int PRIORITY = 4;

    public ItemData Sapling { get; private set; }

    /// <summary>묘목 예약 ID (세이브용)</summary>
    public int ReservationId => reservationId;
    public Vector2Int Cell { get; private set; }

    /// <summary>재배 칸 주인 (월드 파종이면 null)</summary>
    public ISowOwner Owner { get; private set; }

    /// <summary>묘목이 도착해 심기만 남았는지</summary>
    public bool IsDelivered { get; private set; }

    private int reservationId = -1;
    private WorkOrder withdrawOrder;
    private WorkOrder sowOrder;
    private bool finished;

    #region 생성

    /// <summary>재고에서 묘목 1개를 뺄 수 있는지 (예약분 제외, 바닥 더미 포함)</summary>
    public static bool HasStock(ItemData sapling)
        => InventoryManager.instance != null && InventoryManager.instance.GetWorldAvailable(sapling) >= 1;

    /// <summary>해당 칸에 파종 예정지가 있으면 반환</summary>
    public static SowSite At(Vector2Int cell) => All.Find(s => s != null && s.Owner == null && s.Cell == cell);

    /// <summary>파종 예정지를 만듭니다 (플레이어 클릭). 설치 조건·재고가 안 맞으면 null.</summary>
    public static SowSite Create(ItemData sapling, Vector2Int cell)
    {
        if (sapling == null || !sapling.IsSowable || InventoryManager.instance == null) return null;
        if (At(cell) != null || !PlantPlacement.CanPlaceAt(sapling.plantEntity, cell)) return null;

        int reservation = InventoryManager.instance.TryReserve(
            new List<ResourceCost> { new ResourceCost { item = sapling, amount = 1 } });
        if (reservation < 0) return null; // 묘목 재고 없음

        return Build(sapling, cell, false, reservation);
    }

    /// <summary>
    /// 세이브 복원 — 예약은 인벤토리가 ID째로 복원하므로 새로 잡지 않고 이어받습니다.
    /// 로드 직전 개체들이 아직 파괴 대기 중일 수 있어 배치 검사는 하지 않습니다 (심을 때 다시 검사).
    /// </summary>
    public static SowSite Restore(ItemData sapling, Vector2Int cell, bool delivered, int reservationId)
    {
        if (sapling == null || !sapling.IsSowable || At(cell) != null) return null;
        return Build(sapling, cell, delivered, reservationId);
    }

    /// <summary>재배 칸의 파종 요청. 씨앗 재고가 없으면 null (재배 칸이 나중에 다시 시도).</summary>
    public static SowSite CreateForOwner(ItemData seed, ISowOwner owner)
    {
        if (seed == null || !seed.IsSowable || owner == null || InventoryManager.instance == null) return null;

        int reservation = InventoryManager.instance.TryReserve(
            new List<ResourceCost> { new ResourceCost { item = seed, amount = 1 } });
        if (reservation < 0) return null;

        return Build(seed, Vector2Int.FloorToInt(owner.SowPosition), false, reservation, owner);
    }

    /// <summary>재배 칸 세이브 복원 — 예약 ID를 이어받습니다.</summary>
    public static SowSite RestoreForOwner(ItemData seed, ISowOwner owner, bool delivered, int reservationId)
    {
        if (seed == null || !seed.IsSowable || owner == null) return null;
        return Build(seed, Vector2Int.FloorToInt(owner.SowPosition), delivered, reservationId, owner);
    }

    private static SowSite Build(ItemData sapling, Vector2Int cell, bool delivered, int reservation, ISowOwner owner = null)
    {
        var go = new GameObject($"SowSite_{sapling.itemName}_{cell.x}_{cell.y}");
        go.transform.position = new Vector3(cell.x, cell.y, 0f);
        var site = go.AddComponent<SowSite>();
        site.Sapling = sapling;
        site.Cell = cell;
        site.Owner = owner;
        if (owner != null) go.transform.position = owner.SowPosition;
        site.reservationId = reservation;
        site.BuildGhost();

        if (delivered) site.OnMaterialDelivered(sapling, 1);
        else site.RequestDelivery();
        return site;
    }

    private void OnEnable() => All.Add(this);
    private void OnDisable() => All.Remove(this);

    /// <summary>심을 모습을 반투명하게 보여 줍니다.</summary>
    private void BuildGhost()
    {
        var sr = gameObject.AddComponent<SpriteRenderer>();
        PlantPreview.Apply(sr, Sapling.plantEntity);
        sr.color = new Color(1f, 1f, 1f, 0.45f);
    }

    private void RequestDelivery()
    {
        if (WorkSystemManager.instance == null) return;

        withdrawOrder = WorkSystemManager.instance.CreateWorkOrder(
            $"파종 묘목 운반: {Sapling.itemName}", WorkType.Hauling, 0, PRIORITY);
        withdrawOrder.AddTarget(new WithdrawOrder(new MaterialRequest(Sapling, 1, this, reservationId)));
    }

    #endregion

    #region IMaterialReceiver

    public Vector3 GetDeliveryPosition() => Owner != null ? Owner.WorkPosition : new Vector3(Cell.x + 0.5f, Cell.y, 0f);

    public bool IsRequestStillValid() => this != null && !finished && !IsDelivered;

    public void OnMaterialDelivered(ItemData itemData, int amount)
    {
        if (finished || IsDelivered) return;
        IsDelivered = true;

        if (withdrawOrder != null && WorkSystemManager.instance != null)
        {
            WorkSystemManager.instance.RemoveWorkOrder(withdrawOrder, isCancellation: false);
            withdrawOrder = null;
        }

        if (WorkSystemManager.instance == null) return;
        sowOrder = WorkSystemManager.instance.CreateWorkOrder($"파종: {Sapling.itemName}", WorkType.Sowing, 0, PRIORITY);
        sowOrder.AddTarget(new SowOrder { site = this, position = GetDeliveryPosition() });
    }

    public void OnMaterialRequestFailed(ItemData itemData, int amount)
    {
        // 건설과 같은 정책 — 일시적 실패일 수 있으니 유지하고 다른 직원이 재시도
        Debug.LogWarning($"[SowSite] 묘목 운반 실패 @{Cell} — 재시도 대기");
    }

    #endregion

    #region 완료·취소

    /// <summary>파종 작업 완료 — 개체를 성장도 0으로 생성합니다. 자리가 막혔으면 취소(환불)합니다.</summary>
    public void CompleteSowing()
    {
        if (finished) return;

        if (Owner != null)
        {
            finished = true;
            ReleaseReservation();
            sowOrder = null;
            Owner.OnSown(Sapling);
            Destroy(gameObject);
            return;
        }

        if (!PlantPlacement.CanPlaceAt(Sapling.plantEntity, Cell))
        {
            Debug.LogWarning($"[SowSite] {Cell} 자리가 막혀 파종을 취소합니다.");
            Cancel();
            return;
        }

        finished = true;
        ReleaseReservation(); // 묘목은 출고 시점에 이미 창고에서 빠졌다
        sowOrder = null;      // 완료 처리 중인 작업물 — 큐가 비면 WorkSystemManager가 스스로 정리한다
        PlantPlacement.Spawn(Sapling.plantEntity, Cell, 0f);
        Destroy(gameObject);
    }

    /// <summary>파종을 취소합니다. 이미 도착한 묘목은 바닥에 떨어뜨려 돌려줍니다.</summary>
    public void Cancel()
    {
        if (finished) return;
        finished = true;

        RemoveOrder(ref withdrawOrder, true);
        RemoveOrder(ref sowOrder, true);

        if (IsDelivered)
            ItemRefundHelper.SpawnRefunds(new Dictionary<ItemData, int> { { Sapling, 1 } }, transform.position, Vector2Int.one);

        ReleaseReservation();
        Destroy(gameObject);
    }

    private void ReleaseReservation()
    {
        if (reservationId >= 0 && InventoryManager.instance != null)
            InventoryManager.instance.CancelReservation(reservationId);
        reservationId = -1;
    }

    private static void RemoveOrder(ref WorkOrder order, bool cancel)
    {
        if (order != null && WorkSystemManager.instance != null)
            WorkSystemManager.instance.RemoveWorkOrder(order, isCancellation: cancel);
        order = null;
    }

    #endregion
}

/// <summary>식생물 미리보기(파종 고스트·예정지 표시)에 쓸 스프라이트와 배율 — 성장도 0 모습.</summary>
public static class PlantPreview
{
    public static void Get(EntityDefinition def, out Sprite sprite, out float scale)
    {
        sprite = null;
        scale = 1f;
        if (def == null || def.prefab == null) return;

        var plant = def.prefab.GetComponentInChildren<PlantBase>();
        if (plant != null) plant.GetPreview(out sprite, out scale);
        if (sprite == null && def.prefab.TryGetComponent(out SpriteRenderer sr)) sprite = sr.sprite;
    }

    /// <summary>렌더러에 미리보기 스프라이트·배율·정렬을 입힙니다 (정렬은 프리팹을 따라 타일에 가리지 않게).</summary>
    public static void Apply(SpriteRenderer target, EntityDefinition def)
    {
        Get(def, out Sprite sprite, out float scale);
        target.sprite = sprite;
        target.transform.localScale = Vector3.one * scale;

        if (def != null && def.prefab != null && def.prefab.TryGetComponent(out SpriteRenderer src))
        {
            target.sortingLayerID = src.sortingLayerID;
            target.sortingOrder = src.sortingOrder + 1;
        }
    }
}
