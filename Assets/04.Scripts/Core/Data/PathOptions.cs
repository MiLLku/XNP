using System.Collections.Generic;

/// <summary>
/// 길찾기 옵션. TilePathfinder.FindPath에 전달하여 구역 제한을 적용합니다.
///
/// 구역 정책 (결정된 사항):
///   - allowedZoneIds 설정 시 해당 구역 밖 타일(다른 구역 타일)은 완전 차단
///   - 구역 미지정(zoneId == -1) 타일(중립 타일)은 항상 통과 허용
///   - Restricted 구역은 allowedZoneIds 무관하게 항상 차단
///
/// 이 설계로 직원은 중립 타일은 자유롭게 통과하되,
/// 다른 구역으로는 진입할 수 없습니다.
/// </summary>
public class PathOptions
{
    /// <summary>
    /// 허용된 구역 ID 집합.
    /// null이면 전체 탐색 허용.
    /// 설정 시 해당 구역 밖의 '다른 구역 타일'은 완전 차단.
    /// zoneId == -1 (중립 타일)은 항상 허용.
    /// </summary>
    public HashSet<int> allowedZoneIds;

    /// <summary>
    /// 제한 구역(ZoneType.Restricted) 완전 차단 여부.
    /// true면 Restricted 구역 타일의 이동 비용을 float.MaxValue로 설정합니다.
    /// </summary>
    public bool blockRestrictedZones = true;

    // ── 프리셋 ──

    /// <summary>제한 없는 전역 탐색 (기본값)</summary>
    public static PathOptions Default => new PathOptions();

    /// <summary>
    /// 특정 구역 내에서만 이동하는 옵션.
    /// 해당 구역 타일 + 중립 타일만 통과 허용.
    /// </summary>
    public static PathOptions ForZone(int zoneId)
    {
        return new PathOptions
        {
            allowedZoneIds       = new HashSet<int> { zoneId },
            blockRestrictedZones = true
        };
    }

    /// <summary>
    /// 특정 Work 구역 내에서만 이동하는 옵션 (기존 ForWorkZone 대체).
    /// </summary>
    public static PathOptions ForWorkZone(int zoneId) => ForZone(zoneId);

    /// <summary>여러 구역 내에서만 이동하는 옵션.</summary>
    public static PathOptions ForZones(HashSet<int> zoneIds)
    {
        return new PathOptions
        {
            allowedZoneIds       = zoneIds,
            blockRestrictedZones = true
        };
    }
}
