using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 상시 경고 배너 리스트(HUD). AlertsRefreshedMessage를 구독해
/// 매 폴링마다 배너를 재구성한다. 우측 스트립 상단(미니맵 아래)에 배치.
///
/// 계층:
///   AlertReadout [VerticalLayoutGroup, ContentSizeFitter]
///   └── BannerTemplate (AlertBannerItem, 비활성 상태로 둘 것)
/// </summary>
public class AlertReadoutUI : MonoBehaviour
{
    [Header("배너 템플릿")]
    [Tooltip("배너 1개 오브젝트. AlertBannerItem 부착, 비활성으로 두면 런타임에 복제·풀링된다.")]
    [SerializeField] private AlertBannerItem bannerTemplate;

    [Header("심각도별 배경색")]
    [SerializeField] private Color infoColor     = new Color(0.30f, 0.30f, 0.30f, 0.85f);
    [SerializeField] private Color cautionColor  = new Color(0.85f, 0.50f, 0.10f, 0.90f); // 주황
    [SerializeField] private Color criticalColor = new Color(0.80f, 0.15f, 0.15f, 0.92f); // 빨강

    private readonly List<AlertBannerItem> _pool = new List<AlertBannerItem>();

    /// <summary>경고 갱신 메시지 구독 핸들</summary>
    private IDisposable _alertsSubscription;

    private void OnEnable()
    {
        _alertsSubscription = GameMessageBus.Subscribe<AlertsRefreshedMessage>(m => Rebuild(m.alerts));

        // 구독 시점의 현재 경고 즉시 반영
        var nm = NotificationManager.instance;
        if (nm != null) Rebuild(new List<AlertReport>(nm.ActiveAlerts));
    }

    private void OnDisable()
    {
        _alertsSubscription?.Dispose();
        _alertsSubscription = null;
    }

    private void Rebuild(List<AlertReport> alerts)
    {
        if (bannerTemplate == null) return;

        // 필요한 만큼 풀 확장
        while (_pool.Count < alerts.Count)
        {
            var item = Instantiate(bannerTemplate, bannerTemplate.transform.parent);
            _pool.Add(item);
        }

        for (int i = 0; i < _pool.Count; i++)
        {
            if (i < alerts.Count)
                _pool[i].Show(alerts[i], ColorFor(alerts[i].severity));
            else
                _pool[i].Hide();
        }
    }

    private Color ColorFor(AlertSeverity s)
    {
        switch (s)
        {
            case AlertSeverity.Critical: return criticalColor;
            case AlertSeverity.Caution:  return cautionColor;
            default:                     return infoColor;
        }
    }
}
