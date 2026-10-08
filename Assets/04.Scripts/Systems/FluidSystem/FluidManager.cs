using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>액체 종류. 한 칸에는 한 종류만 들어갑니다.</summary>
public enum FluidType : byte
{
    None = 0,
    /// <summary>물</summary>
    Water = 1,
    /// <summary>용암 — 물보다 느리게 흐르고, 물과 닿으면 흑요석으로 굳는다</summary>
    Lava = 2,
}

/// <summary>액체 저장 데이터 — 액체가 든 칸만 남깁니다 (구 세이브에는 없음 — null이면 액체 없음).</summary>
[Serializable]
public class FluidSaveData
{
    /// <summary>칸 번호 (y * 맵 너비 + x)</summary>
    public int[] cells;
    public byte[] types;
    public float[] amounts;
}

/// <summary>
/// 액체(물·용암) 매니저 — 칸마다 액체의 종류와 양(0~1, 1 = 한 칸 가득)을 갖고 흐름을 계산합니다.
///
/// 흐름 규칙 (틱마다, 아래 칸부터):
///   1. 아래 칸이 비었거나 덜 찼으면 들어갈 만큼 떨어진다.
///   2. 더 못 내려가고 남은 양은 좌우 칸과 반씩 나눠 높이를 맞춘다.
///   위로는 흐르지 않습니다(압력 없음) — 이어진 관의 수위는 맞춰지지 않습니다.
///
/// 액체가 든 칸은 <b>공기 칸으로 봅니다</b> — 방·침식·온도 계산은 액체를 모릅니다.
/// 액체가 지나갈 수 있는 칸은 공기가 통하는 칸과 같습니다(지형·차단 건물·문이 막음).
///
/// 맵 전체를 매 틱 훑지 않습니다. 움직일 가능성이 있는 칸만 목록에 두고, 가만히 고인 칸은 빠집니다.
/// 지형·건물이 바뀌면 그 주변을 다시 깨웁니다.
///
/// 물과 용암이 맞닿으면 용암 칸이 흑요석 타일로 굳습니다(물은 그대로).
/// </summary>
public class FluidManager : DestroySingleton<FluidManager>, ISaveModule
{
    /// <summary>한 칸이 담는 양</summary>
    public const float FULL = 1f;

    [Header("흐름")]
    [Tooltip("흐름 계산 간격(초). 물은 한 틱에 한 칸씩 떨어집니다.")]
    [Min(0.02f)] [SerializeField] private float tickInterval = 0.1f;

    [Tooltip("용암은 물의 몇 틱마다 한 번 움직이는지 (클수록 느림)")]
    [Min(1)] [SerializeField] private int lavaTickDivisor = 6;

    [Tooltip("이보다 적은 양은 옮기지 않습니다. 작을수록 수면이 평평해지지만 가라앉는 데 오래 걸립니다.")]
    [Min(0.00001f)] [SerializeField] private float minFlow = 0.0005f;

    [Tooltip("이보다 얕은 액체는 마른 옆 칸으로 번지지 않고 웅덩이로 남습니다 (바닥 전체에 한없이 얇게 퍼지는 것 방지)")]
    [Range(0f, 0.5f)] [SerializeField] private float puddleDepth = 0.05f;

    [Tooltip("더 흐를 곳이 없는데 이보다 얇게 남은 막은 사라집니다")]
    [Min(0f)] [SerializeField] private float minAmount = 0.002f;

    [Tooltip("프레임이 밀렸을 때 한 프레임에 몰아서 돌릴 최대 틱 수")]
    [Min(1)] [SerializeField] private int maxTicksPerFrame = 4;

    [Header("반응")]
    [Tooltip("용암이 이만큼은 있어야 흑요석으로 굳습니다. 그보다 얇으면 그냥 사라집니다.")]
    [Range(0f, 1f)] [SerializeField] private float obsidianMinLava = 0.25f;

    [Header("담수 분출구")]
    [Tooltip("분출구가 물을 내는 간격(초)")]
    [Min(0.1f)] [SerializeField] private float ventInterval = 2f;

    [Tooltip("한 번에 내는 양 (1 = 한 칸). 너무 적으면 내자마자 마릅니다.")]
    [Range(0.02f, 1f)] [SerializeField] private float ventPulse = 0.1f;

    [Tooltip("분출구 바로 위 칸이 이만큼 차면 멈춥니다 — 자기를 다 덮기 직전까지만 낸다.")]
    [Range(0.1f, 1f)] [SerializeField] private float ventFullLevel = 0.9f;

    [Header("물 → 아이템")]
    [Tooltip("퍼낸 물이 바뀌는 아이템")]
    [SerializeField] private ItemData waterItem;

    [Tooltip("침식이 높은 곳에서 퍼낸 물이 바뀌는 아이템")]
    [SerializeField] private ItemData erodedWaterItem;

    [Tooltip("물 한 칸(가득)이 아이템 몇 개인지")]
    [Min(1)] [SerializeField] private int itemsPerCell = 5;

    [Tooltip("물을 받는 자리의 침식이 이 수치 이상이면 '침식된 물'이 나옵니다")]
    [Min(0f)] [SerializeField] private float erodedWaterErosion = 15f;

    [Header("직원 영향")]
    [Tooltip("물이 가득한 칸에서의 이동 속도 배율. 얕을수록 1에 가깝습니다. 용암도 같은 값을 씁니다.")]
    [Range(0.1f, 1f)] [SerializeField] private float fluidSpeedMultiplier = 0.5f;

    [Tooltip("머리까지 잠기는 물을 지날 때 길찾기 비용에 더 곱하는 값. 클수록 마른 길로 돌아갑니다 — 막지는 않습니다.")]
    [Min(1f)] [SerializeField] private float deepWaterPathCost = 3f;

    [Tooltip("머리 칸의 물이 이만큼 차면 잠긴 것으로 봅니다")]
    [Range(0.1f, 1f)] [SerializeField] private float submergeLevel = 0.5f;

    [Tooltip("잠긴 채 숨을 참을 수 있는 시간(초). 이 뒤로는 체력이 깎입니다.")]
    [Min(0f)] [SerializeField] private float breathSeconds = 20f;

    [Tooltip("숨이 다한 뒤 초당 체력 피해")]
    [Min(0f)] [SerializeField] private float drownDamagePerSecond = 5f;

    [Tooltip("용암에 닿아 있는 동안 초당 체력 피해 (버티는 시간 없음)")]
    [Min(0f)] [SerializeField] private float lavaDamagePerSecond = 30f;

    [Tooltip("발 칸의 물이 이만큼은 돼야 '축축함'이 붙습니다")]
    [Range(0f, 1f)] [SerializeField] private float dampMinDepth = 0.05f;

    [Tooltip("'축축함' 정신력 (음수 = 페널티)")]
    [SerializeField] private float dampMental = -2f;

    [Tooltip("'축축함' 지속 시간 (게임 시간 단위)")]
    [Min(0f)] [SerializeField] private float dampGameHours = 3f;

    [Header("그리기")]
    [Tooltip("XNP/Fluid 셰이더 머티리얼. 비우면 계산만 하고 그리지 않습니다.")]
    [SerializeField] private Material fluidMaterial;

    private const int FluidSortingOrder = 15; // 지형·개체 위, 안개(20) 아래

    private const int W = GameMap.MAP_WIDTH;
    private const int H = GameMap.MAP_HEIGHT;

    private readonly byte[] types = new byte[W * H];
    private readonly float[] amounts = new float[W * H];

    // 다음 틱에 살펴볼 칸. queued는 목록 중복 방지용.
    private readonly bool[] queued = new bool[W * H];
    private List<int> current = new List<int>();
    private List<int> next = new List<int>();

    // 칸마다 (종류, 양, 막힘) — 셰이더가 칸 값 그대로 읽는다
    private Texture2D fluidTexture;
    private Color32[] pixels;
    private bool textureDirty;

    private readonly List<int> vents = new List<int>(); // 담수 분출구 타일의 칸 번호
    private float ventTimer;

    private GameMap gameMap;
    private GameMap subscribedMap;
    private float accumulator;
    private int tickCount;

    /// <summary>액체가 한 칸이라도 바뀔 때마다 오르는 번호 — 그리는 쪽이 다시 그릴지 판단하는 데 씁니다.</summary>
    public int Version { get; private set; }

    /// <summary>지금 흐름을 살피고 있는 칸 수 (0이면 전부 고여 있음)</summary>
    public int ActiveCellCount => next.Count;

    #region 초기화

    private void Start() => EnsureGameMap();

    private void OnDestroy() => Unsubscribe();

    private void EnsureGameMap()
    {
        if (gameMap != null || MapGenerator.instance == null) return;
        gameMap = MapGenerator.instance.GameMapInstance;
        if (gameMap == null || subscribedMap == gameMap) return;

        Unsubscribe();
        gameMap.OnCellChanged += HandleCellChanged;
        gameMap.OnBulkChanged += HandleBulkChanged;
        subscribedMap = gameMap;

        // MapGenerator.Start가 먼저 돌았다면 OnBulkChanged를 놓쳤을 수 있으므로 한 번 채워 둔다
        SetupRenderer();
        HandleBulkChanged();
    }

    /// <summary>맵이 통째로 바뀌었다(생성·로드) — 분출구를 다시 찾고 그림을 다시 채운다.</summary>
    private void HandleBulkChanged()
    {
        vents.Clear();
        if (gameMap != null)
        {
            for (int y = 0; y < H; y++)
            for (int x = 0; x < W; x++)
                if (gameMap.TileGrid[x, y] == (int)TileType.FreshwaterVent) vents.Add(Index(x, y));
        }
        RewriteAllCells();
    }

    private void Unsubscribe()
    {
        if (subscribedMap == null) return;
        subscribedMap.OnCellChanged -= HandleCellChanged;
        subscribedMap.OnBulkChanged -= HandleBulkChanged;
        subscribedMap = null;
    }

    /// <summary>지형·건물이 바뀐 칸 — 막혔으면 그 칸의 액체를 없애고, 주변을 다시 깨운다.</summary>
    private void HandleCellChanged(int x, int y)
    {
        int i = Index(x, y);
        // ponytail: 액체가 있는 칸에 벽을 세우면 그 액체는 사라진다. 문제가 되면 옆 칸으로 밀어낼 것.
        if (types[i] != 0 && !IsOpen(x, y)) ClearCell(i);
        WriteCell(i); // 막힘 여부가 바뀌었을 수 있다
        WakeAround(x, y);

        bool isVent = gameMap.TileGrid[x, y] == (int)TileType.FreshwaterVent;
        if (isVent != vents.Contains(i))
        {
            if (isVent) vents.Add(i); else vents.Remove(i);
        }
    }

    /// <summary>담수 분출구 — 바로 위 칸이 덜 찼으면 물을 낸다. 위가 막혔거나 용암이면 내지 않는다.</summary>
    private void PulseVents()
    {
        foreach (int vent in vents)
        {
            int above = vent + W;
            if (above >= types.Length || types[above] == (byte)FluidType.Lava || amounts[above] >= ventFullLevel) continue;
            AddFluid(above % W, above / W, FluidType.Water, ventPulse);
        }
    }

    #endregion

    #region 그리기

    private void SetupRenderer()
    {
        if (fluidTexture != null) return;
        if (fluidMaterial == null)
        {
            Debug.LogWarning("[FluidManager] fluidMaterial이 연결되지 않아 액체를 그리지 않습니다.");
            return;
        }

        fluidTexture = new Texture2D(W, H, TextureFormat.RGBA32, false, true)
        {
            filterMode = FilterMode.Point,
            wrapMode = TextureWrapMode.Clamp,
        };
        pixels = new Color32[W * H];

        var mesh = new Mesh { name = "FluidQuad" };
        mesh.vertices = new[] { new Vector3(0, 0), new Vector3(W, 0), new Vector3(0, H), new Vector3(W, H) };
        mesh.triangles = new[] { 0, 2, 1, 2, 3, 1 };
        mesh.RecalculateBounds();

        var go = new GameObject("FluidOverlay");
        go.transform.SetParent(transform, false);
        go.transform.position = new Vector3(0, 0, -0.015f);
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        var meshRenderer = go.AddComponent<MeshRenderer>();
        meshRenderer.sharedMaterial = fluidMaterial;
        meshRenderer.sortingOrder = FluidSortingOrder;

        // 셰이더 Properties에 없는 값은 머티리얼에 남지 않아 프로퍼티 블록으로 넘긴다
        var block = new MaterialPropertyBlock();
        block.SetTexture("_FluidData", fluidTexture);
        block.SetVector("_MapSize", new Vector4(W, H, 0, 0));
        meshRenderer.SetPropertyBlock(block);
    }

    private void WriteCell(int i)
    {
        if (pixels == null) return;

        int x = i % W, y = i / W;
        bool blocked = gameMap == null || !IsOpen(x, y); // 흐름과 같은 기준 — 문 옆에서도 수면이 평평하게 붙는다
        pixels[i] = new Color32(types[i], (byte)Mathf.Clamp(Mathf.CeilToInt(amounts[i] * 255f), 0, 255), blocked ? (byte)255 : (byte)0, 255);
        textureDirty = true;

        // 액체 칸은 양옆 칸의 막힘 표시도 다시 맞춘다 — 건물(문)이 등록되기 전에 적힌 값이 남아 있으면
        // 수면이 벽 쪽으로 기울어 보인다
        if (types[i] == 0) return;
        if (x > 0) pixels[i - 1].b = IsOpen(x - 1, y) ? (byte)0 : (byte)255;
        if (x < W - 1) pixels[i + 1].b = IsOpen(x + 1, y) ? (byte)0 : (byte)255;
    }

    private void RewriteAllCells()
    {
        if (pixels == null) return;
        for (int i = 0; i < pixels.Length; i++) WriteCell(i);
    }

    // 용암이 있는 칸이 바뀌었는지 — 길찾기는 용암 칸을 못 가는 칸으로 치므로, 바뀌면 경로를 다시 잡게 한다
    private bool lavaCellsChanged;

    private void LateUpdate()
    {
        if (lavaCellsChanged)
        {
            lavaCellsChanged = false;
            gameMap?.InvalidateNavigation();
        }

        if (!textureDirty) return;
        textureDirty = false;
        fluidTexture.SetPixelData(pixels, 0);
        fluidTexture.Apply(false);
    }

    #endregion

    #region 직원 영향

    public float BreathSeconds => breathSeconds;
    public float DrownDamagePerSecond => drownDamagePerSecond;
    public float LavaDamagePerSecond => lavaDamagePerSecond;
    public float DampMinDepth => dampMinDepth;
    public float DampMental => dampMental;

    /// <summary>'축축함' 지속 시간(초) — 게임 시간 설정을 하루 길이로 환산</summary>
    public float DampSeconds
        => dampGameHours * (DayCycle.instance != null ? DayCycle.instance.DayLengthInSeconds : 1000f) / 24f;

    /// <summary>그 칸에 용암이 있는지 (매니저가 없어도 안전) — 길찾기가 용암 칸을 피하는 데 씁니다.</summary>
    public static bool IsLavaAt(int x, int y)
        => instance != null && InBounds(x, y) && instance.types[Index(x, y)] == (byte)FluidType.Lava;

    /// <summary>발이 (x, footY)에 있는 직원이 머리까지 물에 잠기는지 (직원은 두 칸 높이).</summary>
    public bool IsSubmerged(int x, int footY)
        => GetFluid(x, footY + 1) == FluidType.Water && GetAmount(x, footY + 1) >= submergeLevel;

    /// <summary>발이 그 칸에 있을 때의 이동 속도 배율 (액체가 없으면 1).</summary>
    public float GetSpeedMultiplierAt(int x, int y)
    {
        if (!InBounds(x, y)) return 1f;
        int i = Index(x, y);
        return types[i] == 0 ? 1f : Mathf.Lerp(1f, fluidSpeedMultiplier, amounts[i]);
    }

    /// <summary>
    /// 발이 그 칸에 있을 때 길찾기 비용에 곱할 값 (액체가 없으면 1).
    /// 느려지는 만큼 비싸고, 머리까지 잠기는 물은 더 비쌉니다 — 그래도 길이 그것뿐이면 지나갑니다.
    /// </summary>
    public float GetPathCostAt(int x, int y)
    {
        float cost = 1f / GetSpeedMultiplierAt(x, y);
        return IsSubmerged(x, y) ? cost * deepWaterPathCost : cost;
    }

    #endregion

    #region 조회·조작

    /// <summary>그 칸의 액체 종류 (없으면 None)</summary>
    public FluidType GetFluid(int x, int y) => InBounds(x, y) ? (FluidType)types[Index(x, y)] : FluidType.None;

    /// <summary>그 칸의 액체 양 (0~1)</summary>
    public float GetAmount(int x, int y) => InBounds(x, y) ? amounts[Index(x, y)] : 0f;

    /// <summary>
    /// 그 칸에 액체를 붓습니다. 칸이 막혀 있거나 다른 종류의 액체가 있으면 들어가지 않습니다.
    /// </summary>
    /// <param name="wake">false면 흐름 계산을 깨우지 않습니다 — 맵 생성 때 이미 고인 모양으로 채울 때만 쓰세요.</param>
    /// <returns>실제로 들어간 양</returns>
    public float AddFluid(int x, int y, FluidType type, float amount, bool wake = true)
    {
        if (type == FluidType.None || amount <= 0f || !InBounds(x, y) || !IsOpen(x, y)) return 0f;

        int i = Index(x, y);
        if (types[i] != 0 && types[i] != (byte)type) return 0f;

        float added = Mathf.Min(amount, FULL - amounts[i]);
        if (added <= 0f) return 0f;

        if (type == FluidType.Lava && types[i] == 0) lavaCellsChanged = true;
        types[i] = (byte)type;
        amounts[i] += added;
        Version++;
        WriteCell(i);
        if (wake) Wake(i);
        return added;
    }

    /// <summary>그 칸의 액체를 퍼냅니다.</summary>
    /// <returns>실제로 퍼낸 양</returns>
    public float RemoveFluid(int x, int y, float amount)
    {
        if (amount <= 0f || !InBounds(x, y)) return 0f;

        int i = Index(x, y);
        if (types[i] == 0) return 0f;

        float removed = Mathf.Min(amount, amounts[i]);
        amounts[i] -= removed;
        if (amounts[i] <= 0f) ClearCell(i);
        else WriteCell(i);
        Version++;
        WakeAround(x, y); // 줄어든 자리로 옆·위의 액체가 흘러든다
        return removed;
    }

    /// <summary>
    /// 그 칸의 물을 퍼내 아이템으로 바꿉니다 (물 퍼내기·물 흡수 시설 공용).
    /// 받는 자리의 침식이 기준 이상이면 '침식된 물', 아니면 '물'입니다.
    /// 한 칸에 못 미치는 양은 버리지 않고 모아 두었다가 아이템 하나가 될 때 내줍니다.
    /// </summary>
    /// <param name="fromSurroundings">
    /// 그 칸의 물이 모자라면 이어진 물에서 가까운 칸부터 마저 퍼냅니다.
    /// 물은 퍼내는 동안에도 옆으로 흘러 얇게 퍼지므로, 이게 없으면 한 번에 조금씩밖에 못 퍼냅니다.
    /// </param>
    /// <returns>나온 아이템 수 (물이 없거나 아직 하나가 안 되면 0)</returns>
    public int TakeWaterAsItems(int x, int y, float amount, out ItemData item, bool fromSurroundings = false)
    {
        item = null;
        if (GetFluid(x, y) != FluidType.Water) return 0;

        bool eroded = TerrainErosionManager.instance != null
                   && TerrainErosionManager.instance.GetRoomErosionAt(x, y) >= erodedWaterErosion;
        item = eroded ? erodedWaterItem : waterItem;

        float taken = RemoveFluid(x, y, amount);
        if (fromSurroundings && taken < amount) taken += DrainConnected(x, y, amount - taken);

        float items = taken * itemsPerCell + (eroded ? erodedCarry : waterCarry);
        int whole = Mathf.FloorToInt(items + 0.0001f);
        if (eroded) erodedCarry = items - whole; else waterCarry = items - whole;
        return item != null ? whole : 0;
    }

    private float waterCarry, erodedCarry; // 아이템 하나가 안 돼 남겨 둔 양

    /// <summary>(x, y)와 이어진 물에서 가까운 칸부터 amount만큼 퍼냅니다.</summary>
    private float DrainConnected(int x, int y, float amount)
    {
        const int MaxVisited = 64; // 한 번 퍼낼 때 뒤지는 칸 수 상한

        var visited = new HashSet<int> { Index(x, y) };
        var queue = new Queue<int>();
        queue.Enqueue(Index(x, y));
        float taken = 0f;

        while (queue.Count > 0 && taken < amount - 0.0001f && visited.Count <= MaxVisited)
        {
            int i = queue.Dequeue();
            int cx = i % W, cy = i / W;
            if (types[i] == (byte)FluidType.Water) taken += RemoveFluid(cx, cy, amount - taken);

            for (int k = 0; k < 4; k++)
            {
                int nx = cx + (k == 0 ? -1 : k == 1 ? 1 : 0), ny = cy + (k == 2 ? -1 : k == 3 ? 1 : 0);
                if (!InBounds(nx, ny)) continue;

                int n = Index(nx, ny);
                if (types[n] == (byte)FluidType.Water && visited.Add(n)) queue.Enqueue(n);
            }
        }
        return taken;
    }

    /// <summary>맵의 액체를 모두 없앱니다.</summary>
    public void Clear()
    {
        Array.Clear(types, 0, types.Length);
        Array.Clear(amounts, 0, amounts.Length);
        Array.Clear(queued, 0, queued.Length);
        current.Clear();
        next.Clear();
        Version++;
        lavaCellsChanged = true;
        RewriteAllCells();
    }

    private static int Index(int x, int y) => y * W + x;
    private static bool InBounds(int x, int y) => x >= 0 && x < W && y >= 0 && y < H;

    /// <summary>액체가 들어갈 수 있는 칸인지 — 공기가 통하는 칸과 같은 기준(지형·차단 건물·문이 막음).</summary>
    private bool IsOpen(int x, int y)
    {
        if (!InBounds(x, y)) return false;
        if (RoomManager.instance != null) return RoomManager.instance.IsAirCell(x, y);
        return gameMap != null && gameMap.IsPassableTile(x, y) && !gameMap.DoesTileBlockMovement(x, y);
    }

    private void ClearCell(int i)
    {
        if (types[i] == (byte)FluidType.Lava) lavaCellsChanged = true;
        types[i] = 0;
        amounts[i] = 0f;
        Version++;
        WriteCell(i);
    }

    private void Wake(int i)
    {
        if (queued[i]) return;
        queued[i] = true;
        next.Add(i);
    }

    private void WakeAround(int x, int y)
    {
        if (InBounds(x, y)) Wake(Index(x, y));
        if (InBounds(x - 1, y)) Wake(Index(x - 1, y));
        if (InBounds(x + 1, y)) Wake(Index(x + 1, y));
        if (InBounds(x, y + 1)) Wake(Index(x, y + 1));
        if (InBounds(x, y - 1)) Wake(Index(x, y - 1));
    }

    #endregion

    #region 흐름 계산

    private void Update()
    {
        if (gameMap == null)
        {
            EnsureGameMap();
            return;
        }
        ventTimer += Time.deltaTime;
        if (ventTimer >= ventInterval)
        {
            ventTimer = 0f;
            PulseVents();
        }

        if (next.Count == 0)
        {
            accumulator = 0f;
            return;
        }

        accumulator = Mathf.Min(accumulator + Time.deltaTime, tickInterval * maxTicksPerFrame);
        while (accumulator >= tickInterval)
        {
            accumulator -= tickInterval;
            Tick();
        }
    }

    private void Tick()
    {
        tickCount++;
        bool lavaMoves = tickCount % lavaTickDivisor == 0;
        bool leftFirst = (tickCount & 1) == 0; // 한쪽으로 쏠리지 않게 틱마다 순서를 바꾼다

        // 이번 틱에 살필 칸 = 지난 틱에 깨운 칸. 아래 칸부터 처리해야 한 틱에 한 칸씩 떨어진다.
        var cells = next;
        next = current;
        next.Clear();
        current = cells;
        cells.Sort();

        for (int k = 0; k < cells.Count; k++)
        {
            int i = cells[k];
            if (types[i] == (byte)FluidType.Lava && !lavaMoves)
            {
                next.Add(i); // 깨운 채로 다음 틱에 다시 (queued는 그대로 true)
                continue;
            }

            queued[i] = false;
            if (types[i] != 0) Step(i, leftFirst);
        }
    }

    private void Step(int i, bool leftFirst)
    {
        int x = i % W, y = i / W;

        // 물과 용암이 맞닿았으면 용암 쪽이 굳는다
        if (React(i, x, y - 1) || React(i, x - 1, y) || React(i, x + 1, y) || React(i, x, y + 1))
        {
            if (types[i] == 0) return; // 이 칸이 굳었다
        }

        bool moved = false;

        // 1) 아래로
        if (IsOpen(x, y - 1))
        {
            int below = i - W;
            if (types[below] == 0 || types[below] == types[i])
            {
                float flow = Mathf.Min(amounts[i], FULL - amounts[below]);
                if (flow > 0f)
                {
                    Transfer(i, below, flow);
                    moved = true;
                }
            }
        }

        // 2) 더 못 내려간 양은 좌우와 높이를 맞춘다
        if (types[i] != 0)
        {
            moved |= Level(i, x + (leftFirst ? -1 : 1), y);
            if (types[i] != 0) moved |= Level(i, x + (leftFirst ? 1 : -1), y);
        }

        if (moved)
        {
            WakeAround(x, y);
        }
        else if (types[i] != 0 && amounts[i] < minAmount)
        {
            ClearCell(i); // 더 흐를 곳 없는 얇은 막
            WakeAround(x, y);
        }
    }

    /// <summary>옆 칸이 더 낮으면 차이의 절반을 넘겨 높이를 맞춥니다.</summary>
    private bool Level(int i, int nx, int ny)
    {
        if (!IsOpen(nx, ny)) return false;

        int n = Index(nx, ny);
        if (types[n] != 0 && types[n] != types[i]) return false;
        if (types[n] == 0 && amounts[i] < puddleDepth) return false; // 얕으면 마른 칸으로 번지지 않는다

        float flow = (amounts[i] - amounts[n]) * 0.5f;
        if (flow < minFlow) return false;

        Transfer(i, n, flow);
        return true;
    }

    private void Transfer(int from, int to, float flow)
    {
        // 용암이 새 칸에 들어가거나 칸을 완전히 비우면 갈 수 있는 곳이 바뀐다
        if (types[from] == (byte)FluidType.Lava && (types[to] == 0 || amounts[from] - flow <= 0f)) lavaCellsChanged = true;
        types[to] = types[from];
        amounts[to] += flow;
        amounts[from] -= flow;
        if (amounts[from] <= 0f)
        {
            types[from] = 0;
            amounts[from] = 0f;
        }
        Version++;
        WriteCell(from);
        WriteCell(to);
        Wake(to);
    }

    /// <summary>이 칸과 이웃이 물·용암 조합이면 용암 쪽을 굳힙니다.</summary>
    private bool React(int i, int nx, int ny)
    {
        if (!InBounds(nx, ny)) return false;

        int n = Index(nx, ny);
        byte a = types[i], b = types[n];
        if (a == 0 || b == 0 || a == b) return false;

        Solidify(a == (byte)FluidType.Lava ? i : n);
        return true;
    }

    private void Solidify(int lava)
    {
        int x = lava % W, y = lava / W;
        bool enough = amounts[lava] >= obsidianMinLava;
        ClearCell(lava);

        if (enough && gameMap != null)
        {
            gameMap.SetTile(x, y, (int)TileType.Obsidian); // → HandleCellChanged가 주변을 깨운다
            MapGenerator.instance?.MapRendererInstance?.UpdateTileVisual(x, y);
        }
        else WakeAround(x, y);
    }

    #endregion

    #region ISaveModule

    /// <summary>맵(10)·건물(30)·방(35) 다음 — 복원된 액체가 막힌 칸에 걸리지 않게</summary>
    public int SaveOrder => 36;

    public void Capture(SaveData data)
    {
        var cells = new List<int>();
        for (int i = 0; i < types.Length; i++)
            if (types[i] != 0) cells.Add(i);

        var save = new FluidSaveData
        {
            cells = cells.ToArray(),
            types = new byte[cells.Count],
            amounts = new float[cells.Count],
        };
        for (int k = 0; k < cells.Count; k++)
        {
            save.types[k] = types[cells[k]];
            save.amounts[k] = amounts[cells[k]];
        }
        data.fluid = save;
    }

    public void Restore(SaveData data)
    {
        Clear();

        FluidSaveData save = data.fluid;
        if (save == null || save.cells == null || save.types == null || save.amounts == null) return;

        int count = Mathf.Min(save.cells.Length, Mathf.Min(save.types.Length, save.amounts.Length));
        for (int k = 0; k < count; k++)
        {
            int i = save.cells[k];
            if (i < 0 || i >= types.Length || save.types[k] == 0 || save.amounts[k] <= 0f) continue;

            types[i] = save.types[k];
            amounts[i] = Mathf.Min(save.amounts[k], FULL);
            Wake(i); // 흐르던 중에 저장됐을 수 있다 — 고여 있던 칸은 첫 틱에 다시 잠든다
        }
        lavaCellsChanged = true;
        RewriteAllCells();
    }

    public void PostRestore(SaveData data) { }

    #endregion
}
