using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 메시지 로그(HUD). NotificationManager의 레터 추가/제거 이벤트를 구독해
/// 레터 버튼을 쌓는다. 우측 스트립 하단(명령 바 위)에 배치 — 새 레터가 아래로 쌓인다.
///
/// 계층:
///   MessageLog [VerticalLayoutGroup, ContentSizeFitter]
///   └── ItemTemplate (MessageLogItem, 비활성 상태로 둘 것)
/// </summary>
public class MessageLogUI : MonoBehaviour
{
    [Header("레터 템플릿")]
    [Tooltip("레터 1개 오브젝트. MessageLogItem 부착, 비활성으로 두면 런타임에 복제·풀링된다.")]
    [SerializeField] private MessageLogItem itemTemplate;

    [Header("LetterType별 강조색")]
    [SerializeField] private Color neutralColor  = new Color(0.60f, 0.60f, 0.60f, 1f);
    [SerializeField] private Color positiveColor = new Color(0.30f, 0.70f, 0.30f, 1f);
    [SerializeField] private Color threatColor   = new Color(0.80f, 0.20f, 0.20f, 1f);

    private readonly Dictionary<Letter, MessageLogItem> _items = new Dictionary<Letter, MessageLogItem>();
    private readonly Stack<MessageLogItem> _spare = new Stack<MessageLogItem>();

    private bool _subscribed;

    // OnEnable·Start 양쪽에서 시도 — NotificationManager.instance가 늦게 준비돼도(실행 순서 레이스) 보장.
    private void OnEnable() => TrySubscribe();
    private void Start() => TrySubscribe();

    private void OnDisable()
    {
        var nm = NotificationManager.instance;
        if (_subscribed && nm != null)
        {
            nm.OnLetterAdded -= AddItem;
            nm.OnLetterRemoved -= RemoveItem;
        }
        _subscribed = false;
    }

    private void TrySubscribe()
    {
        if (_subscribed) return;
        var nm = NotificationManager.instance;
        if (nm == null) return;

        nm.OnLetterAdded += AddItem;
        nm.OnLetterRemoved += RemoveItem;
        _subscribed = true;

        foreach (var l in nm.Letters) AddItem(l);   // 구독 전에 쌓인 레터 복원
    }

    private void AddItem(Letter letter)
    {
        if (itemTemplate == null || letter == null || _items.ContainsKey(letter)) return;

        MessageLogItem item = _spare.Count > 0
            ? _spare.Pop()
            : Instantiate(itemTemplate, itemTemplate.transform.parent);

        item.transform.SetAsLastSibling();   // 새 레터가 아래로
        item.Bind(letter, ColorFor(letter.type), OnLetterClicked);
        _items[letter] = item;
    }

    private void RemoveItem(Letter letter)
    {
        if (letter == null) return;
        if (_items.TryGetValue(letter, out var item))
        {
            item.gameObject.SetActive(false);
            _spare.Push(item);
            _items.Remove(letter);
        }
    }

    private void OnLetterClicked(Letter letter)
    {
        letter.onClick?.Invoke();   // 부가 동작(카메라 이동 등)

        var popup = UIManager.instance?.GetPanel<LetterDetailPopup>(UIPanelType.LetterDetail);
        if (popup != null) popup.Show(letter);
    }

    private Color ColorFor(LetterType t)
    {
        switch (t)
        {
            case LetterType.Threat:   return threatColor;
            case LetterType.Positive: return positiveColor;
            default:                  return neutralColor;
        }
    }
}
