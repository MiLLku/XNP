using System;
using System.Collections.Generic;

/// <summary>
/// 인벤토리 저장 데이터.
/// </summary>
[Serializable]
public class InventorySaveData
{
    /// <summary>보유 아이템 스택 목록</summary>
    public List<ItemStackSaveData> items;

    /// <summary>활성 자원 예약 목록</summary>
    public List<ReservationSaveData> reservations;

    /// <summary>다음 예약 ID (로드 후 이어서 발급)</summary>
    public int nextReservationId;

    public InventorySaveData()
    {
        items = new List<ItemStackSaveData>();
        reservations = new List<ReservationSaveData>();
        nextReservationId = 1;
    }
}

/// <summary>
/// 자원 예약 저장 데이터.
/// </summary>
[Serializable]
public class ReservationSaveData
{
    /// <summary>예약 ID</summary>
    public int reservationId;

    /// <summary>예약된 아이템 목록</summary>
    public List<ItemStackSaveData> reservedItems;

    public ReservationSaveData()
    {
        reservedItems = new List<ItemStackSaveData>();
    }
}

/// <summary>
/// 아이템 스택 저장 데이터.
/// </summary>
[Serializable]
public class ItemStackSaveData
{
    /// <summary>아이템 ID (ItemData ScriptableObject ID)</summary>
    public int itemId;

    /// <summary>수량</summary>
    public int amount;
}

/// <summary>
/// 드롭된 아이템 저장 데이터 (바닥에 떨어진 아이템).
/// </summary>
[Serializable]
public class DroppedItemSaveData
{
    /// <summary>런타임 고유 ID</summary>
    public int instanceId;

    /// <summary>아이템 ID (ItemData ScriptableObject ID)</summary>
    public int itemId;

    /// <summary>수량</summary>
    public int amount;

    /// <summary>월드 X 좌표</summary>
    public float posX;

    /// <summary>월드 Y 좌표</summary>
    public float posY;
}
