using System;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 화면 오버레이 관리.
/// InteractionManager의 모드에 따라 화면 색상을 부드럽게 전환합니다.
/// </summary>
public class ScreenOverlay : MonoBehaviour
{
    #region 필드

    [Header("UI References")]
    [SerializeField] private Image overlayImage;

    [Header("Overlay Settings")]
    [SerializeField] private Color normalOverlayColor = new Color(0, 0, 0, 0);
    [SerializeField] private Color miningOverlayColor = new Color(0, 0, 0, 0.3f);
    [SerializeField] private Color harvestOverlayColor = new Color(0, 0.1f, 0, 0.2f);
    [SerializeField] private Color demolishOverlayColor = new Color(0.1f, 0, 0, 0.2f);
    [SerializeField] private float fadeSpeed = 5f;

    private Color targetColor;
    private InteractionManager interactionManager;

    /// <summary>상호작용 모드 메시지 구독 핸들</summary>
    private IDisposable modeSubscription;

    #endregion

    #region 생명주기

    void Start()
    {
        interactionManager = InteractionManager.instance;

        if (interactionManager == null)
        {
            Debug.LogError("[ScreenOverlay] InteractionManager를 찾을 수 없습니다!");
            gameObject.SetActive(false);
            return;
        }

        if (overlayImage == null)
        {
            overlayImage = GetComponent<Image>();
        }

        modeSubscription = GameMessageBus.Subscribe<InteractionModeChangedMessage>(m => OnModeChanged(m.mode));

        OnModeChanged(interactionManager.GetCurrentMode());
        overlayImage.color = targetColor;
    }

    void OnDestroy()
    {
        modeSubscription?.Dispose();
        modeSubscription = null;
    }

    void Update()
    {
        if (overlayImage.color != targetColor)
        {
            overlayImage.color = Color.Lerp(overlayImage.color, targetColor, fadeSpeed * Time.deltaTime);
        }
    }

    #endregion

    #region 모드 전환

    private void OnModeChanged(InteractionManager.InteractMode mode)
    {
        switch (mode)
        {
            case InteractionManager.InteractMode.Normal:
            case InteractionManager.InteractMode.Build:
                targetColor = normalOverlayColor;
                break;
            case InteractionManager.InteractMode.Mine:
                targetColor = miningOverlayColor;
                break;
            case InteractionManager.InteractMode.Harvest:
                targetColor = harvestOverlayColor;
                break;
            case InteractionManager.InteractMode.Demolish:
                targetColor = demolishOverlayColor;
                break;
        }
    }

    #endregion
}
