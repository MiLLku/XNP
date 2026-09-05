using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 알림 시스템 허브. 두 축을 관리한다:
///   - 레터(메시지 로그): 이벤트 등 1회성 알림. 클릭 확인 시 소멸.
///   - 경고(상시 배너): 직원 침식·식량 등 지속 상태. 조건 해제 시 자동 소멸.
///
/// 역할 경계: 기준값은 Config(SO)가, 평가·출력은 Evaluator(코드)가 담당한다.
/// UI는 OnLetterAdded/OnLetterRemoved/OnAlertsRefreshed를 구독해 갱신한다.
/// </summary>
public class NotificationManager : DestroySingleton<NotificationManager>
{
    [Header("경고 폴링")]
    [Tooltip("경고 평가 주기(초). 일시정지 중에도 unscaled로 갱신된다.")]
    [SerializeField] private float alertPollInterval = 0.5f;

    [Header("경고 기준 설정 (SO — 숫자만)")]
    [SerializeField] private ErosionAlertConfig erosionConfig;
    [SerializeField] private FoodShortageAlertConfig foodConfig;
    [SerializeField] private FunAlertConfig funAlertConfig;

    [Tooltip("세척 시설 부재 경고 — 침식을 완전히 제거할 수단이 없음을 알린다")]
    [SerializeField] private WashStationAlertConfig washStationConfig = new WashStationAlertConfig();
    [SerializeField] private WashStationFullAlertConfig washStationFullConfig = new WashStationFullAlertConfig();
    [SerializeField] private RoomErosionAlertConfig roomErosionConfig = new RoomErosionAlertConfig();

    [Header("레터")]
    [Tooltip("메시지 로그에 유지할 최대 레터 수(초과 시 가장 오래된 일반 레터부터 제거).")]
    [SerializeField] private int maxLetters = 8;

    private readonly List<IAlertEvaluator> _evaluators = new List<IAlertEvaluator>();
    private readonly List<Letter> _letters = new List<Letter>();
    private readonly List<AlertReport> _activeAlerts = new List<AlertReport>();

    private float _pollTimer;
    private int _nextLetterId = 1;

    /// <summary>게임 이벤트 발생 메시지 구독 핸들</summary>
    private IDisposable _eventSubscription;

    public IReadOnlyList<Letter> Letters => _letters;
    public IReadOnlyList<AlertReport> ActiveAlerts => _activeAlerts;

    #region 생명주기

    protected override void Awake()
    {
        base.Awake();
        BuildEvaluators();
    }

    private void Start()
    {
        _eventSubscription = GameMessageBus.Subscribe<GameEventTriggeredMessage>(m => HandleEvent(m.eventData));
    }

    private void OnDestroy()
    {
        _eventSubscription?.Dispose();
        _eventSubscription = null;
    }

    private void Update()
    {
        // 일시정지(timeScale=0) 중에도 경고가 갱신되도록 unscaled 사용
        _pollTimer -= Time.unscaledDeltaTime;
        if (_pollTimer > 0f) return;
        _pollTimer = alertPollInterval;

        RefreshAlerts();
    }

    #endregion

    #region 경고

    /// <summary>
    /// ★ 새 경고 종류 추가 지점.
    /// Config(SO)를 Evaluator(코드)에 주입해 등록한다.
    /// </summary>
    private void BuildEvaluators()
    {
        if (erosionConfig != null)  _evaluators.Add(new ErosionAlertEvaluator(erosionConfig));
        if (foodConfig != null)     _evaluators.Add(new FoodShortageAlertEvaluator(foodConfig));
        if (funAlertConfig != null) _evaluators.Add(new FunAlertEvaluator(funAlertConfig));
        if (washStationConfig != null) _evaluators.Add(new WashStationAlertEvaluator(washStationConfig));
        if (washStationFullConfig != null) _evaluators.Add(new WashStationFullAlertEvaluator(washStationFullConfig));
        if (roomErosionConfig != null) _evaluators.Add(new RoomErosionAlertEvaluator(roomErosionConfig));
    }

    private void RefreshAlerts()
    {
        _activeAlerts.Clear();

        foreach (var ev in _evaluators)
        {
            if (ev == null || !ev.Enabled) continue;
            var report = ev.Evaluate();
            if (report.active) _activeAlerts.Add(report);
        }

        // 심각도 높은 순 정렬(Critical이 위)
        _activeAlerts.Sort((a, b) => b.severity.CompareTo(a.severity));

        GameMessageBus.Publish(new AlertsRefreshedMessage(_activeAlerts));
    }

    #endregion

    #region 레터

    /// <summary>어디서든 호출 가능한 레터 송출 API.</summary>
    public Letter PushLetter(Letter letter)
    {
        if (letter == null) return null;

        letter.id = _nextLetterId++;
        letter.createdGameTime = Time.time;
        _letters.Add(letter);
        GameMessageBus.Publish(new LetterAddedMessage(letter));

        // 최대치 초과 시 가장 오래된 "일반" 레터부터 제거(정지 유지 레터는 남김)
        while (_letters.Count > maxLetters)
        {
            int idx = _letters.FindIndex(l => !l.pauseUntilRead);
            if (idx < 0) break;
            var old = _letters[idx];
            _letters.RemoveAt(idx);
            GameMessageBus.Publish(new LetterRemovedMessage(old));
        }

        return letter;
    }

    /// <summary>레터 확인/제거. 남은 정지 유지 레터가 없으면 게임을 재개한다.</summary>
    public void DismissLetter(Letter letter)
    {
        if (letter == null) return;
        if (!_letters.Remove(letter)) return;

        GameMessageBus.Publish(new LetterRemovedMessage(letter));

        if (letter.pauseUntilRead && TimeManager.instance != null && TimeManager.instance.IsPaused)
        {
            bool anyPending = _letters.Exists(l => l.pauseUntilRead);
            if (!anyPending) TimeManager.instance.Resume();
        }
    }

    /// <summary>
    /// 이벤트 발생 → 레터 변환.
    /// 정보형 1차: 효과 적용은 EventManager가 이미 수행(여기선 알림만).
    /// 위협(위험 카테고리) 이벤트는 EventManager가 ForcePause 했으므로 확인 시 풀어준다.
    /// </summary>
    private void HandleEvent(EventData e)
    {
        if (e == null) return;

        bool threat = TimeManager.instance != null && TimeManager.instance.IsDangerousCategory(e.category);

        PushLetter(new Letter
        {
            title = e.title,
            body = e.description,
            icon = e.icon,
            type = threat ? LetterType.Threat : LetterType.Neutral,
            sourceEvent = e,
            pauseUntilRead = threat
        });
    }

    #endregion
}
