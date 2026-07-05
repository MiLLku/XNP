using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

/// <summary>
/// 이벤트 시스템 관리자
/// 이벤트 스케줄링, 조건 체크, 발동을 담당합니다.
/// </summary>
public class EventManager : DestroySingleton<EventManager>, ISaveModule
{
    [Header("이벤트 데이터")]
    [Tooltip("발생 가능한 모든 이벤트")]
    public List<EventData> allEvents;

    [Header("타이밍 설정")]
    [Tooltip("이벤트 발생 최소 간격 (초)")]
    public float minInterval = 300f;  // 5분

    [Tooltip("이벤트 발생 최대 간격 (초)")]
    public float maxInterval = 600f;  // 10분

    [Header("제어")]
    public bool enableRandomEvents = true;

    [Header("디버그")]
    public bool showDebugLogs = true;

    // 내부 상태
    private float _nextEventTime;
    private Dictionary<int, ActivePersistentEvent> _activePersistentEvents;
    private Dictionary<int, int> _eventTriggerCounts;       // 이벤트별 발생 횟수
    private Dictionary<int, float> _eventLastTriggerTime;   // 이벤트별 마지막 발생 시간
    private HashSet<int> _triggeredEventIds;                // 한번이라도 발생한 이벤트

    // 이벤트
    public event Action<EventData> OnEventTriggered;
    public event Action<EventData> OnPersistentEventStarted;
    public event Action<EventData> OnPersistentEventEnded;
    public event Action<EventData, EventChoice> OnEventChoiceMade;

    #region Unity 생명주기

    protected override void Awake()
    {
        base.Awake();

        _activePersistentEvents = new Dictionary<int, ActivePersistentEvent>();
        _eventTriggerCounts = new Dictionary<int, int>();
        _eventLastTriggerTime = new Dictionary<int, float>();
        _triggeredEventIds = new HashSet<int>();

        // Resources/Events/ 폴더에 있는 EventData 에셋을 자동 등록
        LoadEventsFromResources();

        ScheduleNextEvent();
    }

    /// <summary>
    /// Resources/Events/ 경로의 EventData 에셋을 allEvents에 자동 추가합니다.
    /// 인스펙터에서 수동 등록한 이벤트와 중복되지 않도록 eventId로 비교합니다.
    /// </summary>
    private void LoadEventsFromResources()
    {
        var loaded = Resources.LoadAll<EventData>("Events");
        if (loaded == null || loaded.Length == 0) return;

        if (allEvents == null) allEvents = new List<EventData>();

        foreach (var evt in loaded)
        {
            if (evt == null) continue;
            if (allEvents.Exists(e => e != null && e.eventId == evt.eventId)) continue;
            allEvents.Add(evt);
        }

        if (showDebugLogs && loaded.Length > 0)
            Debug.Log($"[EventManager] Resources/Events 에서 {loaded.Length}개 이벤트 자동 로드");
    }

    private void Update()
    {
        // 랜덤 이벤트 스케줄링
        if (enableRandomEvents && Time.time >= _nextEventTime)
        {
            TriggerRandomEvent();
            ScheduleNextEvent();
        }

        // 지속형 이벤트 업데이트
        UpdatePersistentEvents();
    }

    #endregion

    #region 공개 API

    /// <summary>
    /// 즉시 랜덤 이벤트 발생
    /// </summary>
    public void TriggerRandomEvent()
    {
        EventData selectedEvent = SelectRandomEvent();

        if (selectedEvent != null)
        {
            TriggerEvent(selectedEvent);
        }
        else if (showDebugLogs)
        {
            Debug.Log("[EventManager] 발생 가능한 이벤트가 없습니다.");
        }
    }

    /// <summary>
    /// 특정 이벤트 즉시 발생 (ID로)
    /// </summary>
    public void TriggerEvent(int eventId)
    {
        // null 가드: 인스펙터 리스트에 빈 슬롯이 있어도 NRE 없이 건너뛴다
        EventData eventData = allEvents.FirstOrDefault(e => e != null && e.eventId == eventId);

        if (eventData != null)
        {
            TriggerEvent(eventData);
        }
        else
        {
            Debug.LogWarning($"[EventManager] 이벤트를 찾을 수 없음: ID {eventId}");
        }
    }

    /// <summary>
    /// 특정 이벤트 즉시 발생 (EventData로)
    /// </summary>
    public void TriggerEvent(EventData eventData)
    {
        if (eventData == null) return;

        if (showDebugLogs)
        {
            Debug.Log($"[EventManager] 이벤트 발생: {eventData.title} (ID: {eventData.eventId})");
        }

        // 발생 기록
        RecordEventTrigger(eventData.eventId);

        // 이벤트 콜백
        OnEventTriggered?.Invoke(eventData);

        // 게임 속도 조절: 위험 이벤트 → 강제 정지, 일반 이벤트 → 1배속
        if (TimeManager.instance != null)
        {
            if (TimeManager.instance.IsDangerousCategory(eventData.category))
                TimeManager.instance.ForcePause();
            else
                TimeManager.instance.SetSpeedToNormal();
        }

        // 선택지가 있으면 UI 표시 (TODO: UI 연동)
        if (eventData.HasChoices)
        {
            ShowEventChoiceUI(eventData);
            return;
        }

        // 즉시 효과 적용
        EventEffectApplier.ApplyEffects(eventData.effects);

        // 지속형 이벤트면 활성화
        if (eventData.isPersistent)
        {
            StartPersistentEvent(eventData);
        }
    }

    /// <summary>
    /// 이벤트 선택지 선택
    /// </summary>
    public void MakeChoice(EventData eventData, int choiceIndex)
    {
        if (eventData == null || eventData.choices == null) return;
        if (choiceIndex < 0 || choiceIndex >= eventData.choices.Count) return;

        EventChoice choice = eventData.choices[choiceIndex];

        if (showDebugLogs)
        {
            Debug.Log($"[EventManager] 선택: {choice.choiceText}");
        }

        // 선택 효과 적용
        EventEffectApplier.ApplyEffects(choice.effects);

        // 콜백
        OnEventChoiceMade?.Invoke(eventData, choice);

        // 지속형이면 활성화
        if (eventData.isPersistent)
        {
            StartPersistentEvent(eventData);
        }
    }

    /// <summary>
    /// 모든 대기 중인 이벤트 및 스케줄 초기화
    /// </summary>
    public void ClearAllPendingEvents()
    {
        ScheduleNextEvent();

        if (showDebugLogs)
        {
            Debug.Log("[EventManager] 대기 이벤트 초기화");
        }
    }

    /// <summary>
    /// 현재 진행 중인 지속형 이벤트 모두 종료
    /// </summary>
    public void ClearAllActiveEvents()
    {
        var activeIds = _activePersistentEvents.Keys.ToList();

        foreach (int eventId in activeIds)
        {
            EndPersistentEvent(eventId);
        }

        if (showDebugLogs)
        {
            Debug.Log($"[EventManager] 활성 이벤트 {activeIds.Count}개 종료");
        }
    }

    /// <summary>
    /// 특정 지속형 이벤트 종료
    /// </summary>
    public void EndPersistentEvent(int eventId)
    {
        if (!_activePersistentEvents.TryGetValue(eventId, out var activeEvent))
            return;

        if (showDebugLogs)
        {
            Debug.Log($"[EventManager] 지속형 이벤트 종료: {activeEvent.eventData.title}");
        }

        // 종료 효과 적용
        EventEffectApplier.ApplyEffects(activeEvent.eventData.endEffects);

        // 콜백
        OnPersistentEventEnded?.Invoke(activeEvent.eventData);

        _activePersistentEvents.Remove(eventId);
    }

    /// <summary>
    /// 랜덤 이벤트 일시정지
    /// </summary>
    public void PauseRandomEvents()
    {
        enableRandomEvents = false;

        if (showDebugLogs)
        {
            Debug.Log("[EventManager] 랜덤 이벤트 일시정지");
        }
    }

    /// <summary>
    /// 랜덤 이벤트 재개
    /// </summary>
    public void ResumeRandomEvents()
    {
        enableRandomEvents = true;
        ScheduleNextEvent();

        if (showDebugLogs)
        {
            Debug.Log("[EventManager] 랜덤 이벤트 재개");
        }
    }

    /// <summary>
    /// 다음 이벤트까지 남은 시간
    /// </summary>
    public float GetTimeUntilNextEvent()
    {
        return Mathf.Max(0, _nextEventTime - Time.time);
    }

    /// <summary>
    /// 이벤트 발생 간격 변경
    /// </summary>
    public void SetEventInterval(float min, float max)
    {
        minInterval = min;
        maxInterval = max;

        if (showDebugLogs)
        {
            Debug.Log($"[EventManager] 이벤트 간격 변경: {min}~{max}초");
        }
    }

    /// <summary>
    /// 특정 이벤트가 활성 상태인지 확인
    /// </summary>
    public bool IsEventActive(int eventId)
    {
        return _activePersistentEvents.ContainsKey(eventId);
    }

    /// <summary>
    /// 특정 이벤트가 이전에 발생했는지 확인
    /// </summary>
    public bool WasEventTriggered(int eventId)
    {
        return _triggeredEventIds.Contains(eventId);
    }

    /// <summary>
    /// 현재 활성 지속형 이벤트 목록
    /// </summary>
    public List<EventData> GetActivePersistentEvents()
    {
        return _activePersistentEvents.Values.Select(a => a.eventData).ToList();
    }

    #endregion

    #region 내부 로직

    private void ScheduleNextEvent()
    {
        float interval = UnityEngine.Random.Range(minInterval, maxInterval);
        _nextEventTime = Time.time + interval;

        if (showDebugLogs)
        {
            Debug.Log($"[EventManager] 다음 이벤트: {interval:F0}초 후");
        }
    }

    /// <summary>
    /// 조건을 만족하는 이벤트들 중 가중치 기반으로 선택
    /// </summary>
    private EventData SelectRandomEvent()
    {
        // 1. 발동 가능한 이벤트 필터링 (null 슬롯 방어 — 인스펙터 빈 칸이 NRE를 유발할 수 있는 경로)
        var validEvents = allEvents.Where(e => e != null && CanTriggerEvent(e)).ToList();

        if (validEvents.Count == 0)
            return null;

        // 2. 가중치 기반 랜덤 선택
        int totalWeight = validEvents.Sum(e => e.weight);
        int randomValue = UnityEngine.Random.Range(0, totalWeight);

        int cumulative = 0;
        foreach (var evt in validEvents)
        {
            cumulative += evt.weight;
            if (randomValue < cumulative)
                return evt;
        }

        return validEvents.Last();
    }

    /// <summary>
    /// 이벤트 발동 가능 여부 확인
    /// </summary>
    private bool CanTriggerEvent(EventData eventData)
    {
        // 1. 조건 체크
        if (!EventConditionEvaluator.CheckAllConditions(eventData.conditions))
            return false;

        // 2. 최대 발생 횟수 체크
        if (eventData.maxOccurrences > 0)
        {
            int count = _eventTriggerCounts.GetValueOrDefault(eventData.eventId, 0);
            if (count >= eventData.maxOccurrences)
                return false;
        }

        // 3. 쿨다운 체크
        if (eventData.cooldown > 0)
        {
            float lastTime = _eventLastTriggerTime.GetValueOrDefault(eventData.eventId, -9999f);
            if (Time.time - lastTime < eventData.cooldown)
                return false;
        }

        // 4. 지속형 이벤트가 이미 활성화 중인지 체크
        if (eventData.isPersistent && _activePersistentEvents.ContainsKey(eventData.eventId))
            return false;

        return true;
    }

    private void RecordEventTrigger(int eventId)
    {
        // 발생 횟수 증가
        _eventTriggerCounts[eventId] = _eventTriggerCounts.GetValueOrDefault(eventId, 0) + 1;

        // 마지막 발생 시간 기록
        _eventLastTriggerTime[eventId] = Time.time;

        // 발생 기록
        _triggeredEventIds.Add(eventId);
    }

    private void StartPersistentEvent(EventData eventData)
    {
        float duration = eventData.GetRandomDuration();

        var activeEvent = new ActivePersistentEvent
        {
            eventData = eventData,
            startTime = Time.time,
            endTime = Time.time + duration
        };

        _activePersistentEvents[eventData.eventId] = activeEvent;

        if (showDebugLogs)
        {
            Debug.Log($"[EventManager] 지속형 이벤트 시작: {eventData.title} ({duration:F0}초간)");
        }

        // 지속 효과 적용
        EventEffectApplier.ApplyEffects(eventData.persistentEffects);

        // 콜백
        OnPersistentEventStarted?.Invoke(eventData);
    }

    private void UpdatePersistentEvents()
    {
        var expiredEvents = new List<int>();

        foreach (var kvp in _activePersistentEvents)
        {
            if (Time.time >= kvp.Value.endTime)
            {
                expiredEvents.Add(kvp.Key);
            }
        }

        foreach (int eventId in expiredEvents)
        {
            EndPersistentEvent(eventId);
        }
    }

    private void ShowEventChoiceUI(EventData eventData)
    {
        // 이벤트는 완전 무작위 발생 설계 — 선택지가 있어도 첫 번째를 자동 적용
        if (eventData.choices != null && eventData.choices.Count > 0)
            MakeChoice(eventData, 0);
    }

    #endregion

    #region ISaveModule 구현

    public int SaveOrder => 80;

    public void Capture(SaveData data)
    {
        data.eventSystem = CaptureSaveData();
    }

    public void Restore(SaveData data)
    {
        if (data.eventSystem != null)
        {
            RestoreSaveData(data.eventSystem);
        }
    }

    public void PostRestore(SaveData data) { }

    #endregion

    #region 저장/로드 지원 (레거시)

    /// <summary>
    /// 저장용 데이터 캡처
    /// </summary>
    public EventSystemSaveData CaptureSaveData()
    {
        return new EventSystemSaveData
        {
            nextEventTime = _nextEventTime - Time.time,
            activePersistentEvents = _activePersistentEvents.Values
                .Select(a => new PersistentEventSaveData
                {
                    eventId = a.eventData.eventId,
                    remainingTime = a.endTime - Time.time
                }).ToList(),
            eventTriggerCounts = _eventTriggerCounts.ToList()
                .Select(kvp => new EventTriggerCountData { eventId = kvp.Key, count = kvp.Value })
                .ToList(),
            triggeredEventIds = _triggeredEventIds.ToList()
        };
    }

    /// <summary>
    /// 저장된 데이터로 복원
    /// </summary>
    public void RestoreSaveData(EventSystemSaveData data)
    {
        if (data == null) return;

        // 다음 이벤트 시간 복원
        _nextEventTime = Time.time + data.nextEventTime;

        // 활성 지속형 이벤트 복원
        _activePersistentEvents.Clear();
        foreach (var persistentData in data.activePersistentEvents)
        {
            EventData eventData = allEvents.FirstOrDefault(e => e != null && e.eventId == persistentData.eventId);
            if (eventData != null)
            {
                _activePersistentEvents[persistentData.eventId] = new ActivePersistentEvent
                {
                    eventData = eventData,
                    startTime = Time.time,
                    endTime = Time.time + persistentData.remainingTime
                };
            }
        }

        // 발생 횟수 복원
        _eventTriggerCounts.Clear();
        foreach (var countData in data.eventTriggerCounts)
        {
            _eventTriggerCounts[countData.eventId] = countData.count;
        }

        // 발생 기록 복원
        _triggeredEventIds = new HashSet<int>(data.triggeredEventIds);

        if (showDebugLogs)
        {
            Debug.Log($"[EventManager] 상태 복원 완료. 활성 이벤트: {_activePersistentEvents.Count}개");
        }
    }

    #endregion

    #region 에디터 테스트

    /// <summary>
    /// XenopsAppearance 카테고리 이벤트를 조건·쿨다운 무시하고 즉시 발동합니다.
    /// BottomBar 강제 발동 버튼 전용. 해당 카테고리 이벤트가 없으면 랜덤 폴백.
    /// </summary>
    public void ForceSpawnXenopsEvent()
    {
        var xenopsEvents = allEvents?
            .Where(e => e != null && e.category == EventCategory.XenopsAppearance)
            .ToList();

        if (xenopsEvents != null && xenopsEvents.Count > 0)
        {
            int totalWeight = xenopsEvents.Sum(e => e.weight);
            int rand = UnityEngine.Random.Range(0, totalWeight);
            int cumulative = 0;
            foreach (var evt in xenopsEvents)
            {
                cumulative += evt.weight;
                if (rand < cumulative)
                {
                    TriggerEvent(evt);
                    return;
                }
            }
            TriggerEvent(xenopsEvents.Last());
        }
        else
        {
            SpawnRandomXenopsFallback();
        }
    }

    /// <summary>
    /// XenopsAppearance 카테고리 이벤트 중 하나를 랜덤으로 발생시킵니다.
    /// 해당 카테고리 이벤트가 없으면 직접 제노프스를 랜덤 스폰합니다.
    /// </summary>
    public void TriggerXenopsSpawnEvent()
    {
        var xenopsEvents = allEvents
            .Where(e => e.category == EventCategory.XenopsAppearance && CanTriggerEvent(e))
            .ToList();

        if (xenopsEvents.Count > 0)
        {
            int totalWeight = xenopsEvents.Sum(e => e.weight);
            int rand = UnityEngine.Random.Range(0, totalWeight);
            int cumulative = 0;
            foreach (var evt in xenopsEvents)
            {
                cumulative += evt.weight;
                if (rand < cumulative)
                {
                    TriggerEvent(evt);
                    return;
                }
            }
            TriggerEvent(xenopsEvents.Last());
        }
        else
        {
            // 등록된 Xenops 이벤트가 없으면 GameDatabase에서 랜덤 스폰
            SpawnRandomXenopsFallback();
        }
    }

    private void SpawnRandomXenopsFallback()
    {
        if (XenopsManager.instance == null || GameDatabase.Instance == null)
        {
            Debug.LogWarning("[EventManager] XenopsManager 또는 GameDatabase가 없어 제노프스를 스폰할 수 없습니다.");
            return;
        }

        var allXenopsData = GameDatabase.Instance.allXenopsData;
        if (allXenopsData == null || allXenopsData.Count == 0)
        {
            Debug.LogWarning("[EventManager] GameDatabase에 등록된 XenopsData가 없습니다.");
            return;
        }

        var xenopsData = allXenopsData[UnityEngine.Random.Range(0, allXenopsData.Count)];
        Vector3 spawnPos = Vector3.zero;
        if (Camera.main != null)
        {
            Vector3 cam = Camera.main.transform.position;
            spawnPos = new Vector3(cam.x + UnityEngine.Random.Range(-8f, 8f), cam.y + UnityEngine.Random.Range(-3f, 3f), 0f);
        }

        XenopsManager.instance.SpawnXenops(xenopsData, spawnPos);
        Debug.Log($"[EventManager] 제노프스 등장 (폴백): {xenopsData.xenopsName}");
    }

    [ContextMenu("Trigger Random Event")]
    private void TestTriggerRandomEvent()
    {
        TriggerRandomEvent();
    }

    [ContextMenu("Clear All Events")]
    private void TestClearAllEvents()
    {
        ClearAllActiveEvents();
        ClearAllPendingEvents();
    }

    #endregion
}

/// <summary>
/// 활성 지속형 이벤트 정보
/// </summary>
public class ActivePersistentEvent
{
    public EventData eventData;
    public float startTime;
    public float endTime;

    public float RemainingTime => endTime - Time.time;
    public float Progress => 1f - (RemainingTime / (endTime - startTime));
}

#region 저장 데이터 구조

[Serializable]
public class EventSystemSaveData
{
    public float nextEventTime;
    public List<PersistentEventSaveData> activePersistentEvents;
    public List<EventTriggerCountData> eventTriggerCounts;
    public List<int> triggeredEventIds;

    public EventSystemSaveData()
    {
        activePersistentEvents = new List<PersistentEventSaveData>();
        eventTriggerCounts = new List<EventTriggerCountData>();
        triggeredEventIds = new List<int>();
    }
}

[Serializable]
public class PersistentEventSaveData
{
    public int eventId;
    public float remainingTime;
}

[Serializable]
public class EventTriggerCountData
{
    public int eventId;
    public int count;
}

#endregion
