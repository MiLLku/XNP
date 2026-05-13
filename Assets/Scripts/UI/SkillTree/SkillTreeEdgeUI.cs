using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 스킬 트리 연결선 UI — 프리팹 불필요, 자급자족 방식.
///
/// Awake()에서 Image 컴포넌트를 자동 추가합니다.
/// SetLine()으로 두 로컬 좌표 사이에 선을 배치합니다.
///
/// 선행 스킬 해제 여부에 따라 골드(활성) / 회색(비활성) 색상으로 표시됩니다.
/// </summary>
[RequireComponent(typeof(RectTransform))]
public class SkillTreeEdgeUI : MonoBehaviour
{
    private static readonly Color ColActive   = new Color(1.00f, 0.82f, 0.22f, 0.90f);
    private static readonly Color ColInactive = new Color(0.38f, 0.38f, 0.40f, 0.70f);

    [SerializeField] private float lineWidth = 3f;

    private RectTransform _rect;
    private Image         _lineImage;

    private void Awake()
    {
        _rect      = GetComponent<RectTransform>();
        _lineImage = GetComponent<Image>() ?? gameObject.AddComponent<Image>();
        _lineImage.raycastTarget = false;
    }

    // ─────────────────────────────────────────────
    #region 퍼블릭 API

    /// <summary>
    /// 두 contentRoot 기준 로컬 좌표 사이에 선을 배치합니다.
    /// SkillTreePanel이 노드 배치 후 호출합니다.
    /// </summary>
    public void SetLine(Vector2 fromLocal, Vector2 toLocal, bool isActive)
    {
        if (_rect == null) _rect = GetComponent<RectTransform>();

        Vector2 dir    = toLocal - fromLocal;
        float   length = dir.magnitude;
        float   angle  = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;

        _rect.anchoredPosition = (fromLocal + toLocal) * 0.5f;
        _rect.sizeDelta        = new Vector2(length, lineWidth);
        _rect.localRotation    = Quaternion.Euler(0f, 0f, angle);

        if (_lineImage == null)
            _lineImage = GetComponent<Image>() ?? gameObject.AddComponent<Image>();

        _lineImage.color = isActive ? ColActive : ColInactive;
    }

    /// <summary>위치 변경 없이 활성/비활성 색상만 갱신합니다.</summary>
    public void SetActive(bool isActive)
    {
        if (_lineImage != null)
            _lineImage.color = isActive ? ColActive : ColInactive;
    }

    #endregion
}
