using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;

/// <summary>
/// 구역을 색으로 칠해 보여주는 오버레이.
///
/// 구역은 눈에 보이지 않는 판정이라 이게 없으면 어디를 지정했는지 확인할 수 없습니다.
/// 구역 칠하기 모드에 들어가면 자동으로 켜지고, 모드를 나가면 꺼집니다.
///
/// RoomOverlayRenderer와 같은 방식(TileFlags.None + SetColor)으로 타일을 물들입니다.
/// </summary>
public class ZoneOverlayRenderer : DestroySingleton<ZoneOverlayRenderer>
{
    #region 인스펙터

    [Header("연결")]
    [Tooltip("구역 오버레이 전용 타일맵")]
    [SerializeField] private Tilemap overlayTilemap;

    [Tooltip("색을 입힐 타일 (TileHighlighter/RoomOverlay와 같은 것을 써도 됩니다)")]
    [SerializeField] private TileBase overlayTile;

    [Header("표시")]
    [Range(0f, 1f)]
    [Tooltip("오버레이 불투명도")]
    [SerializeField] private float alpha = 0.4f;

    [Tooltip("구역 모드에 들어갈 때 자동으로 켤지 여부")]
    [SerializeField] private bool autoShowInZoneMode = true;

    [Tooltip("구역 모드가 아닐 때도 항상 표시할지 여부")]
    [SerializeField] private bool alwaysVisible = false;

    #endregion

    #region 상태

    private bool visible;

    /// <summary>지금 칠해둔 칸 목록 (지울 때 사용)</summary>
    private readonly List<Vector3Int> paintedCells = new List<Vector3Int>();

    /// <summary>오버레이가 켜져 있는지</summary>
    public bool IsVisible => visible;

    #endregion

    #region 생명주기

    private void Start()
    {
        if (overlayTilemap == null)
            Debug.LogWarning("[ZoneOverlayRenderer] overlayTilemap이 연결되지 않았습니다.");
        if (overlayTile == null)
            Debug.LogWarning("[ZoneOverlayRenderer] overlayTile이 연결되지 않았습니다.");

        if (ZoneManager.instance != null)
        {
            ZoneManager.instance.OnZoneTilesChanged += HandleZoneChanged;
            ZoneManager.instance.OnZoneCreated      += HandleZoneCreated;
            ZoneManager.instance.OnZoneDeleted      += HandleZoneChanged;
        }

        if (InteractionManager.instance != null)
            InteractionManager.instance.OnModeChanged += HandleModeChanged;

        SetVisible(alwaysVisible);
    }

    private void OnDestroy()
    {
        if (ZoneManager.instance != null)
        {
            ZoneManager.instance.OnZoneTilesChanged -= HandleZoneChanged;
            ZoneManager.instance.OnZoneCreated      -= HandleZoneCreated;
            ZoneManager.instance.OnZoneDeleted      -= HandleZoneChanged;
        }

        if (InteractionManager.instance != null)
            InteractionManager.instance.OnModeChanged -= HandleModeChanged;
    }

    private void HandleZoneChanged(int zoneId)
    {
        if (visible) Redraw();
    }

    private void HandleZoneCreated(Zone zone)
    {
        if (visible) Redraw();
    }

    private void HandleModeChanged(InteractionManager.InteractMode mode)
    {
        if (!autoShowInZoneMode) return;

        if (mode == InteractionManager.InteractMode.Zone) SetVisible(true);
        else if (!alwaysVisible)                          SetVisible(false);
    }

    #endregion

    #region 공개 API

    /// <summary>오버레이를 켜고 끕니다.</summary>
    public void Toggle() => SetVisible(!visible);

    /// <summary>오버레이 표시 여부를 설정합니다.</summary>
    public void SetVisible(bool value)
    {
        visible = value;

        if (visible) Redraw();
        else Clear();
    }

    /// <summary>구역 모드를 벗어나도 계속 표시할지 설정합니다.</summary>
    public void SetAlwaysVisible(bool value)
    {
        alwaysVisible = value;
        if (value) SetVisible(true);
    }

    #endregion

    #region 그리기

    /// <summary>모든 구역을 다시 칠합니다.</summary>
    public void Redraw()
    {
        Clear();

        if (overlayTilemap == null || overlayTile == null) return;
        if (ZoneManager.instance == null) return;

        foreach (var zone in ZoneManager.instance.GetAllZones())
        {
            if (zone == null) continue;

            Color color = zone.displayColor;
            color.a = alpha;

            foreach (var tile in zone.tiles)
            {
                var position = new Vector3Int(tile.x, tile.y, 0);
                overlayTilemap.SetTile(position, overlayTile);
                overlayTilemap.SetTileFlags(position, TileFlags.None);
                overlayTilemap.SetColor(position, color);
                paintedCells.Add(position);
            }
        }
    }

    /// <summary>칠해둔 칸을 모두 지웁니다.</summary>
    private void Clear()
    {
        if (overlayTilemap == null) return;

        foreach (var cell in paintedCells)
            overlayTilemap.SetTile(cell, null);

        paintedCells.Clear();
    }

    #endregion
}
