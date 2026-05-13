using System;

/// <summary>
/// 전쟁의 안개 저장 데이터.
/// 200×200 맵의 탐색 여부를 비트마스크(byte[])로 직렬화합니다.
/// 크기: 200×200 / 8 = 5,000 바이트 (≈ 5KB)
/// </summary>
[Serializable]
public class FogSaveData
{
    /// <summary>
    /// 탐색된 타일 비트마스크 (미사용 — 하위 호환 유지용).
    /// </summary>
    public byte[] revealedBitmask;

    /// <summary>
    /// 스탬프/구조물로 영구 공개된 타일 비트마스크.
    /// bit index = y * MAP_WIDTH + x
    /// 해당 bit가 1이면 영구 공개 (안개 미적용).
    /// </summary>
    public byte[] permanentRevealBitmask;
}
