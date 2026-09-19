using System;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// 구역 관리 창의 구역 한 줄 — 이름과 이름 변경/확장/축소/삭제 버튼.
/// 버튼이 여럿이라 자식 순서로 찾으면 흔들리므로 인스펙터에 직접 물립니다.
///
/// 이름 변경은 줄 안에서 처리합니다 — 이름칸이 입력칸으로 바뀌고, 엔터를 치거나
/// 다른 곳을 누르면 확정됩니다. 별도 팝업을 띄우지 않습니다.
///
/// 씬에 직렬화되는 컴포넌트라 반드시 파일 하나에 클래스 하나여야 합니다
/// (ZoneModeBarUI.cs 안에 같이 두면 참조가 저장되지 않습니다).
/// </summary>
public class ZoneManageRow : MonoBehaviour
{
    [SerializeField] private TMP_Text label;

    [Tooltip("이름 변경 중에만 보이는 입력칸 (평소 비활성)")]
    [SerializeField] private TMP_InputField nameInput;

    [SerializeField] private Button renameButton;
    [SerializeField] private Button expandButton;
    [SerializeField] private Button shrinkButton;
    [SerializeField] private Button deleteButton;

    private string zoneName;
    private Action<string> onRename;

    /// <param name="display">줄에 보일 문구 (이름 + 칸 수)</param>
    /// <param name="rawName">입력칸에 채울 구역 이름 (칸 수 없이)</param>
    public void Bind(string display, string rawName, Action<string> onRename,
                     Action onExpand, Action onShrink, Action onDelete)
    {
        zoneName = rawName;
        this.onRename = onRename;

        if (label != null) label.text = display;

        Wire(renameButton, BeginRename);
        Wire(expandButton, onExpand);
        Wire(shrinkButton, onShrink);
        Wire(deleteButton, onDelete);

        if (nameInput != null)
        {
            nameInput.onEndEdit.RemoveAllListeners();
            nameInput.onEndEdit.AddListener(EndRename);
            nameInput.gameObject.SetActive(false);
        }
        if (label != null) label.gameObject.SetActive(true);
    }

    /// <summary>지금 이 구역을 편집 중이면 해당 버튼을 밝게 칠합니다.</summary>
    public void SetEditing(bool expanding, bool shrinking)
    {
        Paint(expandButton, expanding);
        Paint(shrinkButton, shrinking);
    }

    #region 이름 변경

    private void BeginRename()
    {
        if (nameInput == null) return;

        if (label != null) label.gameObject.SetActive(false);
        nameInput.gameObject.SetActive(true);
        nameInput.text = zoneName;
        nameInput.Select();
        nameInput.ActivateInputField();
    }

    /// <summary>
    /// 엔터 또는 포커스 아웃. 비우고 나가면 원래 이름을 지킵니다 —
    /// 실수로 지웠을 때 이름 없는 구역이 생기면 목록에서 구분할 수 없습니다.
    /// </summary>
    private void EndRename(string value)
    {
        if (nameInput != null) nameInput.gameObject.SetActive(false);
        if (label != null) label.gameObject.SetActive(true);

        string trimmed = (value ?? string.Empty).Trim();
        if (trimmed.Length == 0 || trimmed == zoneName) return;

        zoneName = trimmed;
        if (onRename != null) onRename(trimmed);
    }

    #endregion

    private static void Wire(Button btn, Action action)
    {
        if (btn == null) return;
        btn.onClick.RemoveAllListeners();
        if (action != null) btn.onClick.AddListener(() => action());
    }

    private static void Paint(Button btn, bool active)
    {
        if (btn == null) return;
        var img = btn.GetComponent<Image>();
        if (img != null) img.color = active ? ZoneModeBarUI.ColActive : ZoneModeBarUI.ColInactive;
    }
}
