using UnityEngine;

/// <summary>소리가 나가는 믹서 그룹 — 그룹별 볼륨을 따로 조절합니다.</summary>
public enum SoundGroup
{
    Bgm,
    Sfx,
    UI,
    Ambient,
}

/// <summary>
/// 소리 식별자 (enum ↔ SoundData.id). 코드는 클립이 아니라 이 id만 압니다.
/// 값은 에셋에 int로 저장되므로 <b>바꾸지 말고 뒤에만 추가</b>하세요.
///   1~99 UI / 100~199 배경음 / 200~ 효과음
/// </summary>
public enum SoundId
{
    None = 0,

    // UI
    UIClick = 1,

    // 배경음
    BgmCalm   = 100,
    BgmCombat = 101,

    // 효과음
    CraftStart    = 200,
    CraftComplete = 201,
    SawWorking    = 202,
}

/// <summary>
/// 소리 한 종류의 정의 — 클립·볼륨·피치 흔들림·그룹·동시 재생 상한.
/// SoundLibrary에 등록하면 SoundManager.Play(SoundId)로 재생됩니다.
/// 클립을 여러 개 넣으면 재생할 때마다 무작위로 하나를 고릅니다.
/// </summary>
[CreateAssetMenu(fileName = "Sound_New", menuName = "XNP/Sound/Sound Data")]
public class SoundData : ScriptableObject
{
    [Tooltip("코드에서 부르는 식별자")]
    public SoundId id = SoundId.None;

    [Tooltip("재생할 클립 (여러 개면 무작위). 비어 있으면 아무 소리도 나지 않습니다.")]
    public AudioClip[] clips = new AudioClip[0];

    [Tooltip("나가는 그룹 (볼륨 설정 단위)")]
    public SoundGroup group = SoundGroup.Sfx;

    [Range(0f, 1f)] public float volume = 1f;

    [Tooltip("재생할 때마다 피치를 이 범위만큼 흔듭니다 (0.05 = ±5%). 같은 소리가 반복돼도 덜 거슬립니다.")]
    [Range(0f, 0.5f)] public float pitchJitter = 0.05f;

    [Tooltip("같은 소리가 동시에 날 수 있는 최대 개수 (습격 때 타격음이 수십 개 겹치는 것 방지)")]
    [Min(1)] public int maxConcurrent = 4;

    [Tooltip("같은 소리를 다시 낼 수 있는 최소 간격 (초, 실제 시간)")]
    [Min(0f)] public float minInterval = 0.05f;

    /// <summary>무작위 클립 (없으면 null)</summary>
    public AudioClip PickClip()
    {
        if (clips == null || clips.Length == 0) return null;
        return clips[Random.Range(0, clips.Length)];
    }
}
