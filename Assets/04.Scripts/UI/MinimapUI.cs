using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// 미니맵.
/// - 탐색하지 않은 칸은 검정, 탐색한 칸은 그 타일 그림의 평균색 (건설된 바닥 타일은 회색, 그 밖의 건물은 표시 안 함)
/// - 플레이어 화면 범위를 사각형으로 표시
/// - +/- 버튼으로 확대·축소 (보이는 칸 수가 min~max 사이)
/// - 클릭하면 그 자리로 화면 이동, 누른 채 끌면 화면이 따라온다
///
/// 확대 상태에서는 카메라를 가운데 두고 따라간다. 끄는 동안에는 보이는 범위를 고정한다 —
/// 고정하지 않으면 화면 이동 → 범위 이동 → 같은 마우스 위치가 다른 좌표를 가리켜 폭주한다.
/// </summary>
public class MinimapUI : MonoBehaviour, IPointerDownHandler, IDragHandler, IPointerUpHandler
{
    [Header("연결")]
    [SerializeField] private RawImage mapImage;
    [Tooltip("플레이어 화면 범위 표시. mapImage의 자식이어야 한다.")]
    [SerializeField] private RectTransform viewRect;
    [SerializeField] private Button zoomInButton;
    [SerializeField] private Button zoomOutButton;

    [Header("확대·축소 (미니맵 세로에 보이는 칸 수 — 가로는 미니맵 비율만큼 더 보인다)")]
    [SerializeField] private float minVisibleCells = 60f;
    [SerializeField] private float maxVisibleCells = GameMap.MAP_HEIGHT;
    [SerializeField] private float zoomStep = 1.5f;

    [Header("표시")]
    [SerializeField] private float refreshInterval = 0.5f;
    [SerializeField] private Color32 airColor = new Color32(58, 70, 88, 255);
    [SerializeField] private Color32 structureColor = new Color32(150, 150, 150, 255);
    [SerializeField] private Color32 unknownTileColor = new Color32(110, 100, 90, 255);
    [SerializeField] private Color32 waterColor = new Color32(60, 130, 220, 255);
    [SerializeField] private Color32 lavaColor = new Color32(235, 100, 30, 255);

    private Texture2D texture;
    private Color32[] pixels;
    private float visibleCells;
    private Vector2 windowOrigin; // 미니맵 왼쪽 아래가 가리키는 칸 좌표
    private Vector2 windowSize;   // 미니맵에 보이는 칸 수 (가로, 세로)
    private bool dragging;
    private float nextRefresh;

    private void Awake()
    {
        visibleCells = maxVisibleCells;
        zoomInButton?.onClick.AddListener(() => SetZoom(visibleCells / zoomStep));
        zoomOutButton?.onClick.AddListener(() => SetZoom(visibleCells * zoomStep));

        texture = new Texture2D(GameMap.MAP_WIDTH, GameMap.MAP_HEIGHT, TextureFormat.RGBA32, false)
        {
            filterMode = FilterMode.Point,
            wrapMode = TextureWrapMode.Clamp,
        };
        pixels = new Color32[GameMap.MAP_WIDTH * GameMap.MAP_HEIGHT];
        if (mapImage != null) mapImage.texture = texture;
        SetZoom(visibleCells);
    }

    private void OnDestroy()
    {
        if (texture != null) Destroy(texture);
    }

    private void SetZoom(float cells)
    {
        visibleCells = Mathf.Clamp(cells, minVisibleCells, maxVisibleCells);
        if (zoomInButton != null) zoomInButton.interactable = visibleCells > minVisibleCells;
        if (zoomOutButton != null) zoomOutButton.interactable = visibleCells < maxVisibleCells;
    }

    private void LateUpdate()
    {
        if (Time.unscaledTime >= nextRefresh)
        {
            nextRefresh = Time.unscaledTime + refreshInterval;
            RebuildTexture();
        }
        UpdateView();
    }

    // ponytail: 주기마다 전체(9만 칸)를 다시 칠한다. 느려지면 타일·탐색 변경 이벤트로 바뀐 칸만.
    private void RebuildTexture()
    {
        var map = MapGenerator.instance != null ? MapGenerator.instance.GameMapInstance : null;
        var fog = FogOfWarManager.instance;
        var fluid = FluidManager.instance;
        if (map == null) return;
        var mapRenderer = MapGenerator.instance.MapRendererInstance;

        var black = new Color32(0, 0, 0, 255);
        int w = GameMap.MAP_WIDTH;
        for (int y = 0; y < GameMap.MAP_HEIGHT; y++)
        {
            for (int x = 0; x < w; x++)
            {
                Color32 c;
                if (fog != null && !fog.IsExplored(x, y)) c = black;
                else
                {
                    int id = map.TileGrid[x, y];
                    FluidType liquid = fluid != null ? fluid.GetFluid(x, y) : FluidType.None;
                    if (liquid != FluidType.None)
                        c = liquid == FluidType.Lava ? lavaColor : waterColor;
                    else if (id == (int)TileType.Air)
                        c = mapRenderer != null && mapRenderer.IsStructureCell(x, y) ? structureColor : airColor;
                    else
                    {
                        c = mapRenderer != null ? mapRenderer.GetTileColor(id) : default;
                        if (c.a == 0) c = unknownTileColor; // 그림이 없는 타일
                    }
                }
                pixels[y * w + x] = c;
            }
        }

        texture.SetPixelData(pixels, 0);
        texture.Apply(false);
    }

    private void UpdateView()
    {
        var cam = Camera.main;
        if (cam == null || mapImage == null) return;

        Vector2 camPos = cam.transform.position;
        Vector2 camHalf = new Vector2(cam.orthographicSize * cam.aspect, cam.orthographicSize);

        if (!dragging)
        {
            // 맵이 가로로 길어서 미니맵도 가로로 길다 — 세로 칸 수에 미니맵 비율을 곱해 가로 칸 수를 정한다
            Rect area = mapImage.rectTransform.rect;
            float aspect = area.height > 0f ? area.width / area.height : 1f;
            windowSize = new Vector2(
                Mathf.Min(visibleCells * aspect, GameMap.MAP_WIDTH),
                Mathf.Min(visibleCells, GameMap.MAP_HEIGHT));
            windowOrigin = new Vector2(
                Mathf.Clamp(camPos.x - windowSize.x * 0.5f, 0f, GameMap.MAP_WIDTH - windowSize.x),
                Mathf.Clamp(camPos.y - windowSize.y * 0.5f, 0f, GameMap.MAP_HEIGHT - windowSize.y));
        }

        mapImage.uvRect = new Rect(
            windowOrigin.x / GameMap.MAP_WIDTH, windowOrigin.y / GameMap.MAP_HEIGHT,
            windowSize.x / GameMap.MAP_WIDTH, windowSize.y / GameMap.MAP_HEIGHT);

        if (viewRect == null) return;
        viewRect.anchorMin = (camPos - camHalf - windowOrigin) / windowSize;
        viewRect.anchorMax = (camPos + camHalf - windowOrigin) / windowSize;
        viewRect.offsetMin = viewRect.offsetMax = Vector2.zero;
    }

    public void OnPointerDown(PointerEventData eventData)
    {
        dragging = true;
        MoveCameraTo(eventData);
    }

    public void OnDrag(PointerEventData eventData) => MoveCameraTo(eventData);

    public void OnPointerUp(PointerEventData eventData) => dragging = false;

    private void MoveCameraTo(PointerEventData eventData)
    {
        if (mapImage == null || CameraController.Instance == null) return;

        RectTransform rect = mapImage.rectTransform;
        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(rect, eventData.position, eventData.pressEventCamera, out Vector2 local))
            return;

        Vector2 n = Rect.PointToNormalized(rect.rect, local); // 0~1로 잘린다 — 테두리 밖으로 끌어도 가장자리에 머문다
        CameraController.Instance.FocusOn(windowOrigin + n * windowSize);
    }
}
