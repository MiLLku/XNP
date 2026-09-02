using System.Collections.Generic;

/// <summary>
/// 길찾기 옵션. TilePathfinder.FindPath에 전달하여 구역 제한을 적용합니다.
///
/// 구역 정책:
///   - allowedZoneIds 설정 시 해당 구역 밖 타일(다른 구역 타일)은 완전 차단
///   - 구역 미지정(zoneId == -1) 타일(중립 타일)은 항상 통과 허용
///
/// 이 설계로 직원은 중립 타일은 자유롭게 통과하되,
/// 자기에게 배정되지 않은 남의 구역으로는 진입할 수 없습니다.
///
/// 구역을 배정받지 않은 직원(= '일반')은 <see cref="Default"/>를 쓰며 맵 전체를 자유롭게 다닙니다.
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

    // ── 프리셋 ──

    /// <summary>
    /// 기본 이동 정책 — 제한 없음 (맵 전체).
    /// 구역을 배정받지 않은 직원이 쓰는 정책입니다.
    ///
    /// 공유 인스턴스입니다. <b>필드를 변경하지 마세요</b> — 모든 직원의 이동에 영향을 줍니다.
    /// 변형이 필요하면 new PathOptions { ... } 로 새로 만드세요.
    /// </summary>
    public static readonly PathOptions Default = new PathOptions();

    /// <summary>
    /// 특정 구역 안에서만 이동하는 옵션.
    /// 해당 구역 타일 + 중립 타일만 통과 허용.
    /// </summary>
    public static PathOptions ForZone(int zoneId)
    {
        return new PathOptions
        {
            allowedZoneIds = new HashSet<int> { zoneId }
        };
    }

    /// <summary>여러 구역 안에서만 이동하는 옵션.</summary>
    public static PathOptions ForZones(HashSet<int> zoneIds)
    {
        return new PathOptions
        {
            allowedZoneIds = zoneIds
        };
    }
}
