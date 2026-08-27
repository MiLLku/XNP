using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;

/// <summary>
/// 방을 색으로 칠해 보여주는 디버그 오버레이.
///
/// 방 시스템은 눈에 보이지 않는 판정이라, 이게 없으면 "왜 여기가 실외로 잡히지"를 추적할 수 없습니다.
/// 실외는 칠하지 않으므로 <b>칠해진 곳이 곧 밀폐된 공간</b>입니다.
///
/// TileHighlighter와 같은 방식(TileFlags.None + SetColor)으로 타일을 물들입니다.
/// </summary>
public class RoomOverlayRenderer : DestroySingleton<RoomOverlayRenderer>
{
    /// <summary>오버레이가 무엇을 색으로 보여줄지</summary>
    public enum OverlayMode
    {
        /// <summary>방마다 다른 색 — 밀폐 판정 확인용</summary>
        RoomId,
        /// <summary>온도 — 파랑(추움) → 회색(보통) → 빨강(더움)</summary>
        Temperature,
        /// <summary>침식 — 회색(깨끗) → 자홍(오염). 온도 색과 겹치지 않게 골랐습니다.</summary>
        Erosion,
    }

    #region 인스펙터

    [Header("연결")]
    [Tooltip("오버레이 전용 타일맵")]
    [SerializeField] private Tilemap overlayTilemap;

    [Tooltip("색을 입힐 타일 (TileHighlighter와 같은 것을 써도 됩니다)")]
    [SerializeField] private TileBase overlayTile;

    [Header("표시")]
    [Range(0f, 1f)]
    [Tooltip("오버레이 불투명도")]
    [SerializeField] private float alpha = 0.45f;

    [Tooltip("시작할 때 켤지 여부")]
    [SerializeField] private bool visibleOnStart = false;

    [Header("온도 모드")]
    [SerializeField] private OverlayMode mode = OverlayMode.RoomId;

    [Tooltip("이 온도 이하는 완전한 파랑")]
    [SerializeField] private float coldTemperature = -10f;

    [Tooltip("이 온도 이상은 완전한 빨강")]
    [SerializeField] private float hotTemperature = 60f;

    [Tooltip("온도·침식 모드에서 색을 새로 칠하는 주기(초)")]
    [SerializeField] private float colorRefreshInterval = 0.5f;

    [Header("침식 모드")]
    [Tooltip("이 침식 수치 이상은 완전한 자홍색")]
    [SerializeField] private float maxErosionForColor = 150f;

    #endregion

    #region 상태

    private bool visible;

    /// <summary>지금 칠해둔 칸 목록 (지울 때 사용)</summary>
    private readonly List<Vector3Int> paintedCells = new List<Vector3Int>();

    /// <summary>오버레이가 켜져 있는지</summary>
    public bool IsVisible => visible;

    /// <summary>현재 표시 모드</summary>
    public OverlayMode Mode => mode;

    private float refreshTimer;

    #endregion

    #region 생명주기

    private void Start()
    {
        if (overlayTilemap == null)
            Debug.LogWarning("[RoomOverlayRenderer] overlayTilemap이 연결되지 않았습니다.");
        if (overlayTile == null)
            Debug.LogWarning("[RoomOverlayRenderer] overlayTile이 연결되지 않았습니다.");

        if (RoomManager.instance != null)
            RoomManager.instance.OnRoomsRebuilt += HandleRoomsRebuilt;

        SetVisible(visibleOnStart);
    }

    private void OnDestroy()
    {
        if (RoomManager.instance != null)
            RoomManager.instance.OnRoomsRebuilt -= HandleRoomsRebuilt;
    }

    private void HandleRoomsRebuilt()
    {
        if (visible) Redraw();
    }

    private void Update()
    {
        // 온도·침식은 계속 변하므로 주기적으로 색만 다시 입힌다 (타일은 그대로 두어 갱신 비용을 줄인다)
        if (!visible || mode == OverlayMode.RoomId) return;

        refreshTimer += Time.deltaTime;
        if (refreshTimer < colorRefreshInterval) return;

        refreshTimer = 0f;
        RefreshColors();
    }

    #endregion

    #region 공개 API

    /// <summary>오버레이를 켜고 끕니다.</summary>
    public void Toggle() => SetVisible(!visible);

    /// <summary>표시 모드를 바꿉니다.</summary>
    public void SetMode(OverlayMode value)
    {
        mode = value;
        if (visible) RefreshColors();
    }

    /// <summary>모드를 다음 것으로 넘깁니다. (방 번호 → 온도 → 침식 → 방 번호)</summary>
    public void CycleMode()
    {
        switch (mode)
        {
            case OverlayMode.RoomId:      SetMode(OverlayMode.Temperature); break;
            case OverlayMode.Temperature: SetMode(OverlayMode.Erosion);     break;
            default:                      SetMode(OverlayMode.RoomId);      break;
        }
    }

    /// <summary>오버레이 표시 여부를 설정합니다.</summary>
    public void SetVisible(bool value)
    {
        visible = value;

        if (visible) Redraw();
        else Clear();
    }

    #endregion

    #region 그리기

    /// <summary>방 전체를 다시 칠합니다.</summary>
    public void Redraw()
    {
        Clear();

        if (overlayTilemap == null || overlayTile == null) return;

        var manager = RoomManager.instance;
        if (manager == null) return;

        foreach (var pair in manager.Rooms)
        {
            Room room = pair.Value;
            Color color = GetColor(room);

            foreach (var cell in room.Cells)
            {
                var position = new Vector3Int(cell.x, cell.y, 0);
                overlayTilemap.SetTile(position, overlayTile);
                overlayTilemap.SetTileFlags(position, TileFlags.None);
                overlayTilemap.SetColor(position, color);
                paintedCells.Add(position);
            }
        }
    }

    /// <summary>칠해둔 칸의 색만 다시 입힙니다. 타일 배치는 건드리지 않습니다.</summary>
    public void RefreshColors()
    {
        if (overlayTilemap == null || overlayTile == null) return;

        var manager = RoomManager.instance;
        if (manager == null) return;

        foreach (var pair in manager.Rooms)
        {
            Room room = pair.Value;
            Color color = GetColor(room);

            foreach (var cell in room.Cells)
                overlayTilemap.SetColor(new Vector3Int(cell.x, cell.y, 0), color);
        }
    }

    /// <summary>현재 모드에 맞는 색</summary>
    private Color GetColor(Room room)
    {
        switch (mode)
        {
            case OverlayMode.Temperature: return GetTemperatureColor(room.Temperature);
            case OverlayMode.Erosion:     return GetErosionColor(room.Erosion);
            default:                      return GetRoomColor(room.Id);
        }
    }

    /// <summary>
    /// 침식을 색으로 바꿉니다.
    /// 실외 기본 침식을 기준(회색)으로 두므로, <b>바깥보다 더러운 방만</b> 물듭니다.
    /// 세척으로 기준 아래까지 내린 청정실은 청록으로 표시됩니다.
    /// </summary>
    private Color GetErosionColor(float erosion)
    {
        float baseline = TerrainErosionManager.instance != null
            ? TerrainErosionManager.instance.OutdoorErosion
            : 0f;

        Color color;
        if (erosion >= baseline)
        {
            float span = Mathf.Max(1f, maxErosionForColor - baseline);
            float t = Mathf.Clamp01((erosion - baseline) / span);
            color = Color.Lerp(new Color(0.7f, 0.7f, 0.7f), new Color(0.85f, 0.1f, 0.85f), t);
        }
        else
        {
            float t = baseline > 0f ? Mathf.Clamp01((baseline - erosion) / baseline) : 0f;
            color = Color.Lerp(new Color(0.7f, 0.7f, 0.7f), new Color(0.2f, 0.9f, 0.75f), t);
        }

        color.a = alpha;
        return color;
    }

    /// <summary>
    /// 온도를 색으로 바꿉니다.
    /// 기준(주변 온도)에서 멀어질수록 파랑/빨강이 짙어지므로, 한눈에 데워진 방을 찾을 수 있습니다.
    /// </summary>
    private Color GetTemperatureColor(float temperature)
    {
        float neutral = TemperatureManager.instance != null
            ? TemperatureManager.instance.OutdoorTemperature
            : 20f;

        Color color;
        if (temperature >= neutral)
        {
            float t = hotTemperature > neutral ? Mathf.Clamp01((temperature - neutral) / (hotTemperature - neutral)) : 0f;
            color = Color.Lerp(new Color(0.7f, 0.7f, 0.7f), new Color(1f, 0.15f, 0.05f), t);
        }
        else
        {
            float t = neutral > coldTemperature ? Mathf.Clamp01((neutral - temperature) / (neutral - coldTemperature)) : 0f;
            color = Color.Lerp(new Color(0.7f, 0.7f, 0.7f), new Color(0.15f, 0.4f, 1f), t);
        }

        color.a = alpha;
        return color;
    }

    /// <summary>칠해둔 칸을 모두 지웁니다.</summary>
    public void Clear()
    {
        if (overlayTilemap == null) { paintedCells.Clear(); return; }

        foreach (var position in paintedCells)
            overlayTilemap.SetTile(position, null);

        paintedCells.Clear();
    }

    /// <summary>
    /// 방 번호로 색을 만듭니다.
    /// 황금비를 곱해 색상환을 돌리면 번호가 붙어 있어도 색이 뚜렷하게 갈립니다.
    /// </summary>
    private Color GetRoomColor(int roomId)
    {
        float hue = (roomId * 0.618033988f) % 1f;
        Color color = Color.HSVToRGB(hue, 0.75f, 1f);
        color.a = alpha;
        return color;
    }

    #endregion
}
