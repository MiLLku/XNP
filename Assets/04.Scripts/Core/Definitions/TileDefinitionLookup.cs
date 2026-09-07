using UnityEngine;

/// <summary>
/// 타일 정의를 찾아 주는 얇은 창구.
///
/// TileHardness·TileConductivity·TileHeatOutput·MiningSkillGate처럼
/// 인스펙터 참조를 받을 수 없는 정적 클래스들이 공유합니다.
/// 정의 데이터베이스가 없을 때 매 프레임 로그가 쏟아지지 않도록 경고는 한 번만 냅니다.
/// </summary>
public static class TileDefinitionLookup
{
    private static bool _warned;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetOnLoad() => _warned = false;

    /// <summary>타일 ID로 정의를 찾습니다. 없으면 null.</summary>
    public static TileDefinition Find(int tileId)
    {
        var db = DefinitionDatabase.Instance;
        if (db == null)
        {
            WarnMissingDatabase();
            return null;
        }
        return db.GetTile(tileId);
    }

    /// <summary>타일 종류로 정의를 찾습니다. 없으면 null.</summary>
    public static TileDefinition Find(TileType type) => Find((int)type);

    private static void WarnMissingDatabase()
    {
        if (_warned) return;
        _warned = true;

        Debug.LogError(
            $"[TileDefinitionLookup] Resources/{DefinitionDatabase.ResourcePath}.asset 을 찾지 못했습니다. " +
            "경도·전도율·발열·채광 스킬 게이트가 모두 기본값으로 동작합니다.\n" +
            "메뉴 [XNP/정의/현재 코드에서 정의 에셋 생성]을 실행하세요.");
    }
}
