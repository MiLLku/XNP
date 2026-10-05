using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;

/// <summary>
/// 맵 렌더러.
/// GameMap 데이터를 Unity Tilemap에 시각화하고, 개체(나무, 광물 등)를 인스턴스화합니다.
/// </summary>
public class MapRenderer : MonoBehaviour
{
    #region 필드

    [Header("필수 연결")]
    [SerializeField] private Tilemap tilemap;
    [SerializeField] private Tilemap wallTilemap;
    [SerializeField] public Transform entityParent;

    [Header("지형 블렌드 (둥근 외곽·재료 섞임)")]
    [Tooltip("XNP/TerrainBlend 머티리얼. 연결하면 메인 타일맵은 판정용으로만 남고 그림은 이 셰이더가 그립니다.\n비우면 예전처럼 칸 모양 그대로 그립니다.")]
    [SerializeField] private Material terrainBlendMaterial;

    [Tooltip("흙 윗면에 덮이는 잔디 띠 텍스처. 비우면 잔디를 그리지 않습니다.")]
    [SerializeField] private Texture2D grassTexture;
    [Tooltip("침식된 곳의 잔디 텍스처. 비우면 일반 잔디를 씁니다.")]
    [SerializeField] private Texture2D erodedGrassTexture;
    [Tooltip("지표 기준선에서 이 칸 수보다 깊은 곳에는 지표 잔디가 자라지 않습니다 (침식 동굴 환경은 예외).")]
    [SerializeField] private int grassMaxDepth = 105;

    private GameMap _gameMap;
    private ResourceManager _resourceManager;

    // 칸마다 2바이트 (타일 id, 잔디 플래그) — 셰이더가 이웃 칸을 읽어 외곽을 깎고 잔디를 덮는다
    private Texture2D _idTexture;
    private byte[] _idBuffer;
    private bool _idDirty;
    private float _nextGrassRefresh;

    // 건설된 바닥 타일이 차지한 칸. 지형이 이 칸 쪽으로는 모서리를 깎지 않고 직각으로 맞닿는다.
    // 값은 셰이더가 읽는 id(StructureBase + 바닥 타일 종류). 0이면 건설 타일 없음.
    private readonly byte[] _structureCells = new byte[GameMap.MAP_WIDTH * GameMap.MAP_HEIGHT];

    // 타일 id별 그림 평균색 (미니맵용). 알파 0 = 그림 없음
    private readonly Color32[] _tileColors = new Color32[MaxBlendTileId];

    /// <summary>메인 타일맵 (EventEffectApplier 등 외부에서 좌표 변환용으로 참조)</summary>
    public Tilemap MainTilemap => tilemap;

    #endregion

    #region 초기화

    /// <summary>
    /// 맵 렌더러를 초기화합니다.
    /// </summary>
    /// <param name="gameMap">렌더링할 게임 맵</param>
    /// <param name="resourceManager">타일/프리팹 에셋 매니저</param>
    public void Initialize(GameMap gameMap, ResourceManager resourceManager)
    {
        _gameMap = gameMap;
        _resourceManager = resourceManager;
    }

    #endregion

    #region 렌더링

    /// <summary>
    /// 전체 맵을 렌더링합니다.
    /// </summary>
    /// <param name="map">렌더링할 GameMap</param>
    public void RenderMap(GameMap map)
    {
        if (_gameMap == null || _resourceManager == null)
        {
            Debug.LogError("[MapRenderer] GameMap 또는 ResourceManager가 없습니다!");
            return;
        }

        _gameMap = map;

        Debug.Log("--- 맵 렌더링 시작 ---");
        RenderTiles();
        RenderEntities();
        Debug.Log("--- 맵 렌더링 완료 ---");
    }

    private void RenderTiles()
    {
        Vector3Int[] positions = new Vector3Int[GameMap.MAP_WIDTH * GameMap.MAP_HEIGHT];
        TileBase[] tiles = new TileBase[GameMap.MAP_WIDTH * GameMap.MAP_HEIGHT];
        TileBase[] wallTiles = new TileBase[GameMap.MAP_WIDTH * GameMap.MAP_HEIGHT];

        int index = 0;
        for (int y = 0; y < GameMap.MAP_HEIGHT; y++)
        {
            for (int x = 0; x < GameMap.MAP_WIDTH; x++)
            {
                positions[index] = new Vector3Int(x, y, 0);
                tiles[index] = _resourceManager.GetTileAsset(_gameMap.TileGrid[x, y]);
                wallTiles[index] = _resourceManager.GetTileAsset(_gameMap.WallGrid[x, y]);
                index++;
            }
        }

        SetupTerrainBlend();
        if (_idBuffer != null)
        {
            for (int y = 0; y < GameMap.MAP_HEIGHT; y++)
                for (int x = 0; x < GameMap.MAP_WIDTH; x++)
                    WriteId(x, y);
        }

        if (tilemap == null) Debug.LogError("[MapRenderer] tilemap이 연결되지 않았습니다!");
        else tilemap.SetTiles(positions, tiles);

        if (wallTilemap == null) Debug.LogError("[MapRenderer] wallTilemap이 연결되지 않았습니다!");
        else wallTilemap.SetTiles(positions, wallTiles);
    }

    private void RenderEntities()
    {
        if (entityParent == null) return;

        Debug.Log($"[MapRenderer] {_gameMap.Entities.Count}개의 개체 렌더링");

        foreach (var entity in _gameMap.Entities)
        {
            GameObject prefab = _resourceManager.GetEntityPrefab(entity.id);
            if (prefab != null)
            {
                Vector3 worldPos = new Vector3(entity.position.x, entity.position.y, 0);
                var spawned = Instantiate(prefab, worldPos, Quaternion.identity, entityParent);

                // 세이브 캡처가 종류별 컴포넌트를 뒤지지 않아도 되도록 ID를 새겨 둔다
                MapEntityIdentity.Attach(spawned, entity.id);
            }
            else
            {
                Debug.LogWarning($"[MapRenderer] 개체 ID '{entity.id}'({entity.type})의 프리팹이 없습니다.");
            }
        }
    }

    #endregion

    #region 공개 API

    /// <summary>
    /// 특정 타일의 시각을 현재 GameMap 데이터로 갱신합니다.
    /// </summary>
    /// <param name="x">타일 X 좌표</param>
    /// <param name="y">타일 Y 좌표</param>
    public void UpdateTileVisual(int x, int y)
    {
        if (_gameMap == null || _resourceManager == null) return;

        int newTileId = _gameMap.TileGrid[x, y];
        TileBase tileAsset = _resourceManager.GetTileAsset(newTileId);
        tilemap.SetTile(new Vector3Int(x, y, 0), tileAsset);
        if (_idBuffer != null) WriteId(x, y);
    }

    /// <summary>
    /// 전체 타일맵을 현재 GameMap 데이터로 새로고침합니다.
    /// 세이브 파일 로드 시 사용됩니다.
    /// </summary>
    public void RefreshAllTiles()
    {
        if (_gameMap == null || _resourceManager == null)
        {
            Debug.LogWarning("[MapRenderer] RefreshAllTiles: GameMap 또는 ResourceManager가 없습니다.");
            return;
        }

        RenderTiles();
        Debug.Log("[MapRenderer] 전체 타일 새로고침 완료");
    }

    /// <summary>
    /// 현재 GameMap.Entities 목록을 기반으로 개체(나무, 식물 등)를 인스턴스화합니다.
    /// 세이브 복원 후 자연물 재생성 시 사용됩니다.
    /// </summary>
    public void RenderRestoredEntities()
    {
        if (_gameMap == null || _resourceManager == null)
        {
            Debug.LogWarning("[MapRenderer] RenderRestoredEntities: GameMap 또는 ResourceManager가 없습니다.");
            return;
        }

        RenderEntities();
        Debug.Log($"[MapRenderer] 복원된 개체 {_gameMap.Entities.Count}개 렌더링 완료");
    }

    /// <summary>
    /// 리소스 매니저를 반환합니다.
    /// </summary>
    public ResourceManager GetResourceManager()
    {
        return _resourceManager;
    }

    #endregion

    #region 지형 블렌드

    private const int MaxBlendTileId = 128; // 지형 타일 id 상한
    private const int BlendInfoSize = 256;  // 셰이더 _TileInfo 배열 크기와 같아야 한다
    private const int StructureBase = 200;  // 셰이더와 같아야 한다: 이 id 이상은 건설된 바닥 타일(StructureBase + 종류)
    private const int BlendLayerSize = 1024;

    /// <summary>
    /// 맵을 덮는 쿼드와 id 텍스처를 한 번 만든다. 메인 타일맵은 콜라이더·좌표 변환용으로 남기고 렌더러만 끈다.
    /// </summary>
    private void SetupTerrainBlend()
    {
        if (terrainBlendMaterial == null || _idTexture != null) return;

        int w = GameMap.MAP_WIDTH, h = GameMap.MAP_HEIGHT;
        _idTexture = new Texture2D(w, h, TextureFormat.RG16, false, true)
        {
            filterMode = FilterMode.Point,
            wrapMode = TextureWrapMode.Clamp,
        };
        _idBuffer = new byte[w * h * 2];

        // 타일마다 그림 출처를 고른다: 이음매 없는 blendTexture가 우선, 없으면 tileAsset 스프라이트(칸마다 반복).
        // 같은 그림을 쓰는 타일끼리는 레이어를 공유한다.
        var info = new Vector4[BlendInfoSize];        // x = 레이어+1 (0이면 그림 없음), y = 1이면 이음매 없는 텍스처
        var priorities = new float[MaxBlendTileId];
        var sources = new List<(Texture tex, Rect uv)>();
        for (int id = 1; id < MaxBlendTileId; id++)
        {
            var def = TileDefinitionLookup.Find((TileType)id);
            (Texture tex, Rect uv) src;
            bool seamless = def != null && def.blendTexture != null;
            if (seamless) src = (def.blendTexture, new Rect(0, 0, 1, 1));
            else
            {
                var sprite = (_resourceManager.GetTileAsset(id) as Tile)?.sprite;
                if (sprite == null) continue;
                Rect r = sprite.textureRect;
                Texture t = sprite.texture;
                src = (t, new Rect(r.x / t.width, r.y / t.height, r.width / t.width, r.height / t.height));
            }
            int layer = sources.IndexOf(src);
            if (layer < 0) { layer = sources.Count; sources.Add(src); }
            bool grassHost = id == (int)TileType.Dirt || id == (int)TileType.GrassDirt;
            info[id] = new Vector4(layer + 1, seamless ? 1 : 0, grassHost ? 1 : 0, 0);
            priorities[id] = def != null ? def.blendPriority : 0;
        }

        // 건설된 바닥 타일: 텍스처가 있는 종류만 셰이더가 그린다 (없으면 프리팹 스프라이트가 그대로 보인다)
        var floors = DefinitionDatabase.Instance != null ? DefinitionDatabase.Instance.floorTiles : null;
        if (floors != null)
        {
            foreach (var floor in floors)
            {
                if (floor == null || floor.blendTexture == null) continue;
                int id = StructureBase + floor.id;
                if (floor.id < 0 || id >= BlendInfoSize) continue;
                var src = ((Texture)floor.blendTexture, new Rect(0, 0, 1, 1));
                int layer = sources.IndexOf(src);
                if (layer < 0) { layer = sources.Count; sources.Add(src); }
                info[id] = new Vector4(layer + 1, 1, 0, 0);
            }
        }
        _structureInfo = info;

        // 잔디는 재료가 아니라 흙 위에 덮는 띠라 타일 id 없이 레이어만 차지한다
        var grassLayers = Vector4.zero; // (일반, 침식, 사용 여부)
        if (grassTexture != null)
        {
            var full = new Rect(0, 0, 1, 1);
            sources.Add((grassTexture, full));
            int normal = sources.Count - 1;
            int eroded = normal;
            if (erodedGrassTexture != null) { sources.Add((erodedGrassTexture, full)); eroded = sources.Count - 1; }
            grassLayers = new Vector4(normal, eroded, 1, 0);
        }

        // ponytail: 레이어당 1024² RGBA 비압축(밉 포함 약 5.6MB). 레이어가 수십 장이 되면 에디터에서 압축 Texture2DArray로 굽기.
        var tileArray = new RenderTexture(new RenderTextureDescriptor(BlendLayerSize, BlendLayerSize, RenderTextureFormat.ARGB32, 0)
        {
            dimension = UnityEngine.Rendering.TextureDimension.Tex2DArray,
            volumeDepth = Mathf.Max(1, sources.Count),
            useMipMap = true,
            autoGenerateMips = false,
        })
        {
            name = "TerrainBlendTiles",
            wrapMode = TextureWrapMode.Repeat,
            filterMode = FilterMode.Trilinear,
            anisoLevel = 4,
        };
        for (int i = 0; i < sources.Count; i++)
            Graphics.Blit(sources[i].tex, tileArray, sources[i].uv.size, sources[i].uv.position, 0, i);
        tileArray.GenerateMips();

        // 가장 작은 밉(1×1) = 그 그림의 평균색. 미니맵이 타일 색으로 쓴다.
        var onePixel = RenderTexture.GetTemporary(1, 1, 0, RenderTextureFormat.ARGB32);
        var reader = new Texture2D(1, 1, TextureFormat.RGBA32, false);
        var previousActive = RenderTexture.active;
        var layerColors = new Color32[sources.Count];
        for (int i = 0; i < sources.Count; i++)
        {
            Graphics.CopyTexture(tileArray, i, tileArray.mipmapCount - 1, onePixel, 0, 0);
            RenderTexture.active = onePixel;
            reader.ReadPixels(new Rect(0, 0, 1, 1), 0, 0);
            // 리니어 색 공간에서는 읽어 온 값이 리니어라, UI에 쓰려면 감마로 되돌려야 한다 (안 하면 거의 검게 나온다)
            Color pixel = reader.GetPixel(0, 0);
            if (QualitySettings.activeColorSpace == ColorSpace.Linear) pixel = pixel.gamma;
            pixel.a = 1f;
            layerColors[i] = pixel;
        }
        RenderTexture.active = previousActive;
        RenderTexture.ReleaseTemporary(onePixel);
        Destroy(reader);
        for (int id = 1; id < MaxBlendTileId; id++)
            if (info[id].x > 0) _tileColors[id] = layerColors[(int)info[id].x - 1];

        // 셰이더 Properties에 없는 값(배열 등)은 머티리얼에 남지 않아 프로퍼티 블록으로 넘긴다
        var block = new MaterialPropertyBlock();
        block.SetTexture("_TileTex", tileArray);
        block.SetTexture("_MapTex", _idTexture);
        block.SetVector("_MapSize", new Vector4(w, h, 0, 0));
        block.SetVectorArray("_TileInfo", info);
        block.SetVector("_GrassLayers", grassLayers);
        block.SetFloatArray("_Priority", priorities);

        var mesh = new Mesh { name = "TerrainBlendQuad" };
        mesh.vertices = new[] { new Vector3(0, 0), new Vector3(w, 0), new Vector3(0, h), new Vector3(w, h) };
        mesh.triangles = new[] { 0, 2, 1, 2, 3, 1 };
        mesh.RecalculateBounds();

        var groundRenderer = tilemap.GetComponent<TilemapRenderer>();
        var go = new GameObject("TerrainBlend");
        go.transform.SetParent(tilemap.transform, false);
        go.transform.localPosition = new Vector3(0, 0, -0.01f); // 같은 정렬 순서인 벽 타일맵보다 앞에
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        var mr = go.AddComponent<MeshRenderer>();
        mr.sharedMaterial = terrainBlendMaterial;
        mr.SetPropertyBlock(block);
        mr.sortingLayerID = groundRenderer.sortingLayerID;
        mr.sortingOrder = groundRenderer.sortingOrder;
        groundRenderer.enabled = false;
    }

    private Vector4[] _structureInfo; // SetupTerrainBlend가 채운 _TileInfo — 어떤 바닥 종류를 셰이더가 그리는지 묻는 데 쓴다

    private void WriteId(int x, int y)
    {
        int index = y * GameMap.MAP_WIDTH + x;
        int id = _gameMap.TileGrid[x, y];
        _idBuffer[index * 2] = _structureCells[index] != 0 ? _structureCells[index]
                         : (byte)(id >= 0 && id < MaxBlendTileId ? id : 0);
        _idDirty = true;
    }

    /// <summary>타일 그림의 평균색 (미니맵용). 그림이 없는 타일이면 알파 0.</summary>
    public Color32 GetTileColor(int id) => id >= 0 && id < MaxBlendTileId ? _tileColors[id] : default;

    /// <summary>건설된 바닥 타일이 채운 칸인지.</summary>
    public bool IsStructureCell(int x, int y) => _structureCells[y * GameMap.MAP_WIDTH + x] != 0;

    /// <summary>
    /// 건설된 바닥 타일이 이 칸을 채우고 있는지 알립니다.
    /// 지형 블렌드가 이 칸을 '이웃 고체'로 보고 둥근 모서리·테두리 없이 직각으로 맞닿습니다.
    /// </summary>
    /// <returns>
    /// 이 종류를 지형 셰이더가 직접 그리면 true — 호출한 쪽은 자기 스프라이트를 꺼야 합니다(겹쳐 그려지지 않게).
    /// </returns>
    public bool SetStructureCell(int x, int y, bool occupied, FloorTileType type)
    {
        if (x < 0 || y < 0 || x >= GameMap.MAP_WIDTH || y >= GameMap.MAP_HEIGHT) return false;

        int id = Mathf.Clamp(StructureBase + (int)type, StructureBase, BlendInfoSize - 1);
        _structureCells[y * GameMap.MAP_WIDTH + x] = occupied ? (byte)id : (byte)0;
        if (_idBuffer != null) WriteId(x, y);
        return occupied && _structureInfo != null && _structureInfo[id].x > 0;
    }

    private const byte GrassNone = 0, GrassNormal = 1, GrassEroded = 2; // 셰이더와 같아야 한다

    /// <summary>
    /// 공기 칸마다 "나와 맞닿은 흙 면에 잔디가 자라는가"를 다시 매긴다.
    /// - 하늘로 뚫린 지표면 공기: 자란다(일정 깊이 아래 제외). 그 자리 침식이 기준 이상이면 침식된 잔디.
    /// - 밀폐된 방: 침식 동굴 환경(RoomManager.IsErodedHabitat)일 때만 침식된 잔디. 그 밖의 동굴은 맨흙.
    /// 타일이 잔디흙인지 흙인지는 따지지 않는다 — 스탬프가 흙으로 다진 시작 지점도 같은 규칙으로 덮인다.
    /// 판정은 그림 전용이다 — 타일 종류는 바꾸지 않는다.
    /// </summary>
    private void RefreshGrassFlags()
    {
        var rooms = RoomManager.instance;
        var erosion = TerrainErosionManager.instance;
        int w = GameMap.MAP_WIDTH;
        float surfaceY = MapGenerator.instance != null ? MapGenerator.instance.SurfaceReferenceY : GameMap.MAP_HEIGHT;
        int minY = Mathf.CeilToInt(surfaceY - grassMaxDepth);

        for (int y = 0; y < GameMap.MAP_HEIGHT; y++)
        {
            for (int x = 0; x < w; x++)
            {
                byte flag = GrassNone;
                if (rooms != null && _gameMap.TileGrid[x, y] == (int)TileType.Air && TouchesGrassHost(x, y))
                {
                    if (rooms.IsOpenToSky(x, y))
                    {
                        if (y >= minY)
                            flag = erosion != null && erosion.GetRoomErosionAt(x, y) >= rooms.ErodedErosionThreshold ? GrassEroded : GrassNormal;
                    }
                    else if (rooms.IsErodedHabitat(x, y))
                    {
                        flag = GrassEroded;
                    }
                }

                int i = (y * w + x) * 2 + 1;
                if (_idBuffer[i] == flag) continue;
                _idBuffer[i] = flag;
                _idDirty = true;
            }
        }
    }

    // 대각선 포함 8방향 — 셰이더가 모서리를 돌아 감싸는 잔디도 이 칸에서 읽는다
    private bool TouchesGrassHost(int x, int y)
    {
        for (int dy = -1; dy <= 1; dy++)
        {
            for (int dx = -1; dx <= 1; dx++)
            {
                int nx = x + dx, ny = y + dy;
                if (nx < 0 || ny < 0 || nx >= GameMap.MAP_WIDTH || ny >= GameMap.MAP_HEIGHT) continue;
                int id = _gameMap.TileGrid[nx, ny];
                if (id == (int)TileType.Dirt || id == (int)TileType.GrassDirt) return true;
            }
        }
        return false;
    }

    // 채광 여러 칸이 한 프레임에 몰려도 업로드는 한 번
    private void LateUpdate()
    {
        if (_idBuffer == null) return;

        // ponytail: 1초마다 전체 훑기(9만 칸). 방 재계산·침식 변화를 이벤트 없이 따라간다. 느려지면 RoomsRebuiltMessage 구독으로.
        if (Time.unscaledTime >= _nextGrassRefresh)
        {
            _nextGrassRefresh = Time.unscaledTime + 1f;
            RefreshGrassFlags();
        }

        if (!_idDirty) return;
        _idDirty = false;
        _idTexture.SetPixelData(_idBuffer, 0);
        _idTexture.Apply(false);
    }

    #endregion
}
