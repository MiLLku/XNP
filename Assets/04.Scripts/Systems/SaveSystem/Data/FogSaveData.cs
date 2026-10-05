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
    /// 한 번이라도 시야에 들어온(탐색한) 타일 비트마스크. 구 세이브에는 없을 수 있습니다(null).
    /// </summary>
    public byte[] revealedBitmask;

    /// <summary>
    /// 스탬프/구조물로 영구 공개된 타일 비트마스크.
    /// bit index = y * MAP_WIDTH + x
    /// 해당 bit가 1이면 영구 공개 (안개 미적용).
    /// </summary>
    public byte[] permanentRevealBitmask;
}
