using System;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// 현재 상호작용 모드를 화면에 표시하는 UI 패널
/// UIManager를 통해 관리됨 (alwaysActive = true)
/// </summary>
public class InteractionModePanel : BasePanel
{
    [Header("UI References")]
    [SerializeField] private TextMeshProUGUI modeNameText;
    [SerializeField] private TextMeshProUGUI keyHintsText;

    [Header("Mode Display Settings")]
    [SerializeField] private Color normalColor = new Color(1f, 1f, 1f);
    [SerializeField] private Color miningColor = new Color(1f, 0.9f, 0.3f);
    [SerializeField] private Color harvestColor = new Color(0.3f, 1f, 0.3f);
    [SerializeField] private Color buildColor = new Color(0.3f, 0.7f, 1f);
    [SerializeField] private Color demolishColor = new Color(1f, 0.3f, 0.3f);

    private InteractionManager interactionManager;

    /// <summary>상호작용 모드 메시지 구독 핸들</summary>
    private IDisposable modeSubscription;

    void Start()
    {
        interactionManager = InteractionManager.instance;

        if (interactionManager == null)
        {
            Debug.LogError("[InteractionModePanel] InteractionManager를 찾을 수 없습니다!");
            gameObject.SetActive(false);
            return;
        }

        // 모드 변경 구독
        modeSubscription = GameMessageBus.Subscribe<InteractionModeChangedMessage>(m => UpdateModeDisplay(m.mode));

        // 초기 모드 표시
        UpdateModeDisplay(interactionManager.GetCurrentMode());

        // 항상 키 힌트 표시
        UpdateKeyHints();
    }

    void OnEnable()
    {
        // 활성화될 때마다 현재 모드를 다시 반영
        if (interactionManager != null)
        {
            UpdateModeDisplay(interactionManager.GetCurrentMode());
        }
    }

    void OnDestroy()
    {
        modeSubscription?.Dispose();
        modeSubscription = null;
    }

    /// <summary>
    /// 현재 모드에 맞게 UI를 업데이트합니다.
    /// </summary>
    private void UpdateModeDisplay(InteractionManager.InteractMode mode)
    {
        if (modeNameText == null) return;

        string modeName = "";
        Color modeColor = Color.white;

        switch (mode)
        {
            case InteractionManager.InteractMode.Normal:
                modeName = "일반 모드";
                modeColor = normalColor;
                break;

            case InteractionManager.InteractMode.Mine:
                modeName = "채광 모드";
                modeColor = miningColor;
                break;

            case InteractionManager.InteractMode.Harvest:
                modeName = "수확 모드";
                modeColor = harvestColor;
                break;

            case InteractionManager.InteractMode.Build:
                modeName = "건설 모드";
                modeColor = buildColor;
                break;

            case InteractionManager.InteractMode.Demolish:
                modeName = "철거 모드";
                modeColor = demolishColor;
                break;
        }

        modeNameText.text = modeName;
        modeNameText.color = modeColor;
    }

    /// <summary>
    /// 키 힌트를 업데이트합니다.
    /// </summary>
    private void UpdateKeyHints()
    {
        if (keyHintsText == null) return;

        keyHintsText.text =
            "<b>조작 키</b>\n" +
            "<color=#FFFFFF>[TAB]</color> 일반\n" +
            "<color=#FFE680>[Q]</color> 채광\n" +
            "<color=#80FF80>[W]</color> 수확\n" +
            "<color=#80B3FF>[B]</color> 건설\n" +
            "<color=#FF8080>[R]</color> 철거\n" +
            "<color=#CCCCCC>[ESC]</color> 취소";
    }
}
