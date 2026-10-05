using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 건물 이름 바 — 건물을 클릭하면 그 건물의 정보 창 위에 붙어 이름을 보여 줍니다.
///
/// 이름 오른쪽 '수정' 버튼을 눌러야 이름을 고칠 수 있습니다. 정보 창이 따로 없는 건물(발전기 등)은 이 바만 건물 위에 뜹니다.
/// 바가 떠 있는 동안에는 화면 전체를 덮는 차단막이 깔려 <b>다른 상호작용이 막힙니다</b> — 창 밖을 누르면 창이 닫힙니다.
///
/// 건물마다 창이 제각각이라 창에 이름 칸을 넣는 대신, Building.OnMouseDown이 여기에 요청하고
/// 그 프레임에 열린 창(BasePanel.LastOpened)을 LateUpdate에서 찾아 붙습니다.
/// </summary>
public class BuildingNameBar : MonoBehaviour
{
    [SerializeField] private RectTransform bar;
    [Tooltip("화면 전체를 덮는 차단막 (Canvas 직속 — 창 바로 뒤로 순서를 옮깁니다)")]
    [SerializeField] private Button blocker;
    [SerializeField] private TMP_Text nameText;
    [SerializeField] private Button editButton;
    [SerializeField] private TMP_InputField nameInput;
    [Tooltip("정보 창 없이 바만 떴을 때의 닫기 버튼")]
    [SerializeField] private Button closeButton;

    private static BuildingNameBar instance;
    private readonly Vector3[] corners = new Vector3[4];

    private Building pending;
    private int pendingFrame;
    private Building target;
    private BasePanel panel;

    /// <summary>이름 바(=차단막)가 떠 있는지. 이때 열리는 창은 차단막 위로 올라와야 합니다.</summary>
    public static bool IsOpen => instance != null && instance.bar.gameObject.activeSelf;

    /// <summary>이 건물의 이름 바를 띄워 달라고 요청합니다 (그 프레임 끝에 열립니다).</summary>
    public static void Request(Building building)
    {
        if (instance == null) return;
        instance.pending = building;
        instance.pendingFrame = Time.frameCount;
    }

    private void Awake()
    {
        instance = this;
        blocker.onClick.AddListener(Close);
        closeButton.onClick.AddListener(Close);
        editButton.onClick.AddListener(() => SetEditing(true));
        nameInput.onEndEdit.AddListener(OnNameEdited);
        bar.gameObject.SetActive(false);
        blocker.gameObject.SetActive(false);
    }

    // 클릭(OnMouseDown·InteractionManager.Update)이 창을 다 연 뒤에 붙는다.
    // 차단막을 더 일찍 켜면 같은 클릭을 받는 건물 기능 쪽이 'UI 위 클릭'으로 보고 창을 안 연다.
    private void LateUpdate()
    {
        if (pending != null)
        {
            Open(pending);
            pending = null;
            return;
        }
        if (!bar.gameObject.activeSelf) return;

        // 건물이 철거됐거나, 붙어 있던 창이 닫혔거나, 바만 뜬 상태에서 ESC
        bool gone = target == null
                 || (panel != null ? !panel.gameObject.activeInHierarchy : Input.GetKeyDown(KeyCode.Escape));
        if (gone) Close(); else Place();
    }

    private void Open(Building building)
    {
        target = building;
        panel = BasePanel.LastOpenedFrame >= pendingFrame ? BasePanel.LastOpened : null; // 같은 클릭으로 열린 창
        if (panel != null && !panel.gameObject.activeInHierarchy) panel = null;

        // 그리는 순서: 차단막 → 정보 창 → 이름 바
        blocker.transform.SetAsLastSibling();
        if (panel != null) panel.transform.SetAsLastSibling();
        transform.SetAsLastSibling();

        blocker.gameObject.SetActive(true);
        bar.gameObject.SetActive(true);
        closeButton.gameObject.SetActive(panel == null);
        SetEditing(false);
        Place();
    }

    private void Close()
    {
        if (!bar.gameObject.activeSelf) return;
        SetEditing(false);
        bar.gameObject.SetActive(false);
        blocker.gameObject.SetActive(false);
        if (panel != null && panel.gameObject.activeSelf) panel.OnClose();
        panel = null;
        target = null;
    }

    private void SetEditing(bool editing)
    {
        nameText.gameObject.SetActive(!editing);
        editButton.gameObject.SetActive(!editing);
        nameInput.gameObject.SetActive(editing);
        if (target == null) return;

        if (editing)
        {
            nameInput.SetTextWithoutNotify(target.DisplayName);
            nameInput.Select();
            nameInput.ActivateInputField();
        }
        else nameText.text = target.DisplayName;
    }

    private void OnNameEdited(string text)
    {
        if (!nameInput.gameObject.activeSelf) return;
        if (target != null && !nameInput.wasCanceled) target.DisplayName = text; // 비우면 기본 이름으로 돌아간다
        SetEditing(false);
    }

    /// <summary>정보 창의 왼쪽 위 모서리 바로 위에, 창이 없으면 건물 위에 놓습니다 (화면 밖으로 나가지 않게).</summary>
    private void Place()
    {
        Vector2 size = bar.rect.size * bar.lossyScale.x;
        Vector3 pos;
        if (panel != null)
        {
            ((RectTransform)panel.transform).GetWorldCorners(corners);
            pos = corners[1] + Vector3.up * 4f;
        }
        else
        {
            Bounds bounds = target.GetComponent<Collider2D>().bounds;
            pos = Camera.main.WorldToScreenPoint(new Vector3(bounds.center.x, bounds.max.y, 0f));
            pos.x -= size.x * 0.5f;
            pos.y += 8f;
        }

        pos.x = Mathf.Clamp(pos.x, 0f, Mathf.Max(0f, Screen.width - size.x));
        pos.y = Mathf.Clamp(pos.y, 0f, Mathf.Max(0f, Screen.height - size.y));
        pos.z = 0f;
        bar.position = pos;
    }
}
