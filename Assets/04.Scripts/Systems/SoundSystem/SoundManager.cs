using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using MessagePipe;
using UnityEngine;
using UnityEngine.Audio;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// 소리 재생 담당 — 메인화면·인게임 어디서나 쓰도록 씬을 넘어 유지됩니다(DontDestroySingleton).
///
///   • 배경음: 소스 2개를 번갈아 크로스페이드 (PlayBgm). 습격 시작 → 전투곡, 습격 끝 → 평시곡 (MessagePipe 구독).
///   • 효과음: 미리 만든 소스 풀을 돌려 씀 (Play). 소리마다 동시 재생 상한·최소 간격.
///     위치를 주면 화면 밖에서는 작아지다 꺼진다 — 맵이 넓어서 멀리 있는 채광·전투 소리를 거른다.
///   • UI 소리: 2D 소스 1개 (PlayUI). 버튼을 누르면 클릭음이 저절로 난다.
///
/// 게임 배속(Time.timeScale)은 소리 높낮이에 영향을 주지 않고(AudioSource는 timeScale과 무관),
/// 배경음·UI 소리는 일시정지 중에도 납니다(ignoreListenerPause + 실제 시간 페이드).
/// 그룹 볼륨은 PlayerPrefs에 저장합니다(게임 세이브와 별개).
/// 게임 로직은 소리를 직접 부르지 않고 메시지를 내기만 하면 됩니다 — 연결은 여기서 합니다.
/// </summary>
public class SoundManager : DontDestroySingleton<SoundManager>
{
    [Header("데이터")]
    [Tooltip("SoundId → SoundData 등록부")]
    [SerializeField] private SoundLibrary library;

    [Header("믹서 (선택) — 비우면 볼륨을 코드에서 곱합니다")]
    [SerializeField] private AudioMixer mixer;
    [Tooltip("SoundGroup 순서(Bgm, Sfx, UI, Ambient)대로 믹서 그룹")]
    [SerializeField] private AudioMixerGroup[] groupOutputs = new AudioMixerGroup[4];

    [Header("재생")]
    [Tooltip("효과음 소스 풀 크기 — 동시에 날 수 있는 효과음 총수")]
    [Min(1)] [SerializeField] private int sfxPoolSize = 12;
    [Tooltip("배경음 크로스페이드 시간 (초, 실제 시간)")]
    [Min(0f)] [SerializeField] private float bgmFadeSeconds = 1.5f;
    [Tooltip("화면 밖으로 이 비율(화면 크기 대비)만큼 벗어나면 효과음이 꺼집니다. 그 사이는 점점 작아짐.")]
    [Min(0.01f)] [SerializeField] private float offscreenFalloff = 0.25f;

    private const string PREF_KEY = "Sound.Volume.";
    private static readonly SoundGroup[] Groups = { SoundGroup.Bgm, SoundGroup.Sfx, SoundGroup.UI, SoundGroup.Ambient };

    private AudioSource bgmA, bgmB, uiSource;
    private readonly List<AudioSource> sfxPool = new List<AudioSource>();
    private readonly Dictionary<AudioSource, SoundData> playingData = new Dictionary<AudioSource, SoundData>();
    private readonly Dictionary<SoundData, float> lastPlayTime = new Dictionary<SoundData, float>();
    private readonly float[] groupVolume = { 1f, 1f, 1f, 1f };
    private float masterVolume = 1f;

    private SoundId currentBgm = SoundId.None;
    private float currentBgmVolume = 1f;
    private CancellationTokenSource bgmCts;
    private IDisposable subscriptions;

    private readonly List<RaycastResult> raycastResults = new List<RaycastResult>();

    #region 초기화

    protected override void Awake()
    {
        base.Awake();
        if (instance != this) return;

        bgmA = CreateSource("BGM_A", SoundGroup.Bgm, loop: true, ignorePause: true);
        bgmB = CreateSource("BGM_B", SoundGroup.Bgm, loop: true, ignorePause: true);
        uiSource = CreateSource("UI", SoundGroup.UI, loop: false, ignorePause: true);
        for (int i = 0; i < sfxPoolSize; i++)
            sfxPool.Add(CreateSource($"SFX_{i}", SoundGroup.Sfx, loop: false, ignorePause: false));

        masterVolume = PlayerPrefs.GetFloat(PREF_KEY + "Master", 1f);
        foreach (var g in Groups) groupVolume[(int)g] = PlayerPrefs.GetFloat(PREF_KEY + g, 1f);
    }

    private void Start()
    {
        ApplyMixerVolumes();
        PlayBgm(SoundId.BgmCalm);
    }

    private void OnEnable()
    {
        subscriptions = DisposableBag.Create(
            GameMessageBus.Subscribe<RaidStartedMessage>(_ => PlayBgm(SoundId.BgmCombat)),
            GameMessageBus.Subscribe<RaidCompletedMessage>(_ => PlayBgm(SoundId.BgmCalm)));
    }

    private void OnDisable()
    {
        subscriptions?.Dispose();
        subscriptions = null;
    }

    private void OnDestroy()
    {
        bgmCts?.Cancel();
        bgmCts?.Dispose();
        bgmCts = null;
    }

    private AudioSource CreateSource(string name, SoundGroup group, bool loop, bool ignorePause)
    {
        var go = new GameObject(name);
        go.transform.SetParent(transform, false);
        var src = go.AddComponent<AudioSource>();
        src.playOnAwake = false;
        src.loop = loop;
        src.spatialBlend = 0f; // 2D — 거리는 화면 기준으로 직접 계산
        src.ignoreListenerPause = ignorePause;
        src.outputAudioMixerGroup = OutputFor(group);
        return src;
    }

    private AudioMixerGroup OutputFor(SoundGroup group)
    {
        int i = (int)group;
        return groupOutputs != null && i < groupOutputs.Length ? groupOutputs[i] : null;
    }

    #endregion

    #region 재생 API

    /// <summary>효과음 재생. 위치를 주면 화면에서 멀수록 작아지고, 많이 벗어나면 나지 않습니다.</summary>
    public void Play(SoundId id, Vector3? worldPosition = null) => Play(library != null ? library.Get(id) : null, worldPosition);

    /// <inheritdoc cref="Play(SoundId, Vector3?)"/>
    public void Play(SoundData data, Vector3? worldPosition = null)
    {
        if (data == null) return;
        AudioClip clip = data.PickClip();
        if (clip == null || !CanPlayNow(data)) return;

        float positional = worldPosition.HasValue ? ScreenFactor(worldPosition.Value) : 1f;
        if (positional <= 0f) return;

        AudioSource src = FreeSfxSource();
        if (src == null) return; // 풀이 다 차 있으면 이번 소리는 버린다

        src.outputAudioMixerGroup = OutputFor(data.group);
        src.clip = clip;
        src.volume = data.volume * positional * CodeVolume(data.group);
        src.pitch = 1f + UnityEngine.Random.Range(-data.pitchJitter, data.pitchJitter);
        src.Play();
        playingData[src] = data;
        lastPlayTime[data] = Time.unscaledTime;
    }

    /// <summary>UI 소리 — 2D, 일시정지 중에도 납니다.</summary>
    public void PlayUI(SoundId id)
    {
        SoundData data = library != null ? library.Get(id) : null;
        AudioClip clip = data != null ? data.PickClip() : null;
        if (clip == null || !CanPlayNow(data)) return;

        uiSource.pitch = 1f + UnityEngine.Random.Range(-data.pitchJitter, data.pitchJitter);
        uiSource.PlayOneShot(clip, data.volume * CodeVolume(SoundGroup.UI));
        lastPlayTime[data] = Time.unscaledTime;
    }

    /// <summary>배경음을 바꿉니다 (크로스페이드). 같은 곡이면 아무것도 하지 않습니다.</summary>
    public void PlayBgm(SoundId id)
    {
        if (id == currentBgm) return;
        SoundData data = library != null ? library.Get(id) : null;
        AudioClip clip = data != null ? data.PickClip() : null;
        currentBgm = id;
        currentBgmVolume = data != null ? data.volume : 1f;

        bgmCts?.Cancel();
        bgmCts?.Dispose();
        bgmCts = CancellationTokenSource.CreateLinkedTokenSource(this.GetCancellationTokenOnDestroy());
        CrossfadeAsync(clip, bgmCts.Token).Forget();
    }

    private async UniTaskVoid CrossfadeAsync(AudioClip clip, CancellationToken ct)
    {
        // 지금 크게 나는 쪽이 out, 다른 쪽이 in
        AudioSource from = bgmA.volume >= bgmB.volume ? bgmA : bgmB;
        AudioSource to = from == bgmA ? bgmB : bgmA;
        float fromStart = from.volume;
        float target = currentBgmVolume * CodeVolume(SoundGroup.Bgm);

        if (clip != null)
        {
            to.clip = clip;
            to.volume = 0f;
            to.Play();
        }

        float t = 0f;
        while (t < bgmFadeSeconds)
        {
            await UniTask.Yield(GameLoop.Frame, ct);
            t += Time.unscaledDeltaTime; // 일시정지 중에도 페이드
            float k = Mathf.Clamp01(t / Mathf.Max(bgmFadeSeconds, 0.0001f));
            from.volume = Mathf.Lerp(fromStart, 0f, k);
            if (clip != null) to.volume = Mathf.Lerp(0f, target, k);
        }

        from.Stop();
        from.volume = 0f;
        if (clip != null) to.volume = target;
    }

    #endregion

    #region 볼륨

    /// <summary>그룹 볼륨 (0~1). PlayerPrefs에 저장됩니다.</summary>
    public float GetVolume(SoundGroup group) => groupVolume[(int)group];

    public void SetVolume(SoundGroup group, float value)
    {
        groupVolume[(int)group] = Mathf.Clamp01(value);
        PlayerPrefs.SetFloat(PREF_KEY + group, groupVolume[(int)group]);
        ApplyMixerVolumes();
        RefreshBgmVolume();
    }

    public float MasterVolume
    {
        get => masterVolume;
        set
        {
            masterVolume = Mathf.Clamp01(value);
            PlayerPrefs.SetFloat(PREF_KEY + "Master", masterVolume);
            ApplyMixerVolumes();
            RefreshBgmVolume();
        }
    }

    /// <summary>믹서가 있으면 노출 파라미터(MasterVolume·BgmVolume·SfxVolume·UIVolume·AmbientVolume, dB)로 보냅니다.</summary>
    private void ApplyMixerVolumes()
    {
        if (mixer == null) return;
        mixer.SetFloat("MasterVolume", ToDecibel(masterVolume));
        foreach (var g in Groups) mixer.SetFloat(g + "Volume", ToDecibel(groupVolume[(int)g]));
    }

    /// <summary>믹서가 없을 때만 소스 볼륨에 곱하는 값 (믹서가 있으면 믹서가 맡으므로 1)</summary>
    private float CodeVolume(SoundGroup group) => mixer != null ? 1f : masterVolume * groupVolume[(int)group];

    private void RefreshBgmVolume()
    {
        AudioSource active = bgmA.volume >= bgmB.volume ? bgmA : bgmB;
        if (active.isPlaying) active.volume = currentBgmVolume * CodeVolume(SoundGroup.Bgm);
    }

    private static float ToDecibel(float v) => v <= 0.0001f ? -80f : Mathf.Log10(v) * 20f;

    #endregion

    #region 내부

    private bool CanPlayNow(SoundData data)
    {
        if (lastPlayTime.TryGetValue(data, out float last) && Time.unscaledTime - last < data.minInterval) return false;

        int playing = 0;
        foreach (var pair in playingData)
            if (pair.Value == data && pair.Key.isPlaying) playing++;
        return playing < data.maxConcurrent;
    }

    private AudioSource FreeSfxSource()
    {
        foreach (var src in sfxPool)
            if (!src.isPlaying) return src;
        return null;
    }

    /// <summary>화면 안이면 1, 화면 밖으로 offscreenFalloff만큼 벗어날 때까지 1→0.</summary>
    private float ScreenFactor(Vector3 world)
    {
        Camera cam = Camera.main;
        if (cam == null) return 1f;
        Vector3 v = cam.WorldToViewportPoint(world);
        float outside = Mathf.Max(Mathf.Max(-v.x, v.x - 1f), Mathf.Max(-v.y, v.y - 1f));
        return outside <= 0f ? 1f : Mathf.Clamp01(1f - outside / offscreenFalloff);
    }

    /// <summary>버튼(Selectable)을 누르면 클릭음 — 버튼마다 소리를 달지 않아도 된다.</summary>
    private void Update()
    {
        if (!Input.GetMouseButtonDown(0) || EventSystem.current == null) return;

        var pointer = new PointerEventData(EventSystem.current) { position = Input.mousePosition };
        raycastResults.Clear();
        EventSystem.current.RaycastAll(pointer, raycastResults);
        if (raycastResults.Count == 0) return;

        var selectable = raycastResults[0].gameObject.GetComponentInParent<Selectable>();
        if (selectable != null && selectable.IsInteractable()) PlayUI(SoundId.UIClick);
    }

    #endregion
}
