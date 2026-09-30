using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>장비 목록 한 칸의 데이터 — 인스턴스와 지금 착용 중인 직원(없으면 보관소).</summary>
public class EquipmentEntry
{
    public EquipmentInstance instance;
    public EquipmentData data;

    /// <summary>착용 중인 직원 (보관소에 있으면 null)</summary>
    public Employee wearer;

    public bool IsWorn => wearer != null;
    public string Name => data != null && data.itemData != null ? data.itemData.itemName : "장비";
}

/// <summary>
/// 장비 목록 모으기·정렬.
///
/// <b>정렬</b>은 <see cref="Sort"/> 한 곳에서만 정합니다 — 나중에 정렬 방식(등급·내구도·착용 여부 등)을
/// 고르는 기능을 붙일 때 이 비교 함수만 바꿔 끼우면 됩니다.
/// </summary>
public static class EquipmentListing
{
    /// <summary>현재 정렬. 기본: 슬롯 종류 → 이름 → 인스턴스 번호</summary>
    public static Comparison<EquipmentEntry> Sort = DefaultSort;

    public static int DefaultSort(EquipmentEntry a, EquipmentEntry b)
    {
        int c = (a.data != null ? (int)a.data.slot : 0).CompareTo(b.data != null ? (int)b.data.slot : 0);
        if (c != 0) return c;
        c = string.CompareOrdinal(a.Name, b.Name);
        return c != 0 ? c : a.instance.instanceId.CompareTo(b.instance.instanceId);
    }

    /// <summary>보관소 장비 + 직원이 착용 중인 장비 전부를 정렬해 돌려줍니다.</summary>
    public static List<EquipmentEntry> CollectAll()
    {
        var list = new List<EquipmentEntry>();
        var db = GameDatabase.Instance;

        var storage = EquipmentStorageManager.instance;
        if (storage != null)
            foreach (var inst in storage.Pool)
                Add(list, inst, null, db);

        if (EmployeeManager.instance != null)
            foreach (var emp in EmployeeManager.instance.AllEmployees)
            {
                if (emp == null || !emp.TryGetComponent(out EmployeeEquipment eq)) continue;
                foreach (EquipmentSlot slot in Enum.GetValues(typeof(EquipmentSlot)))
                {
                    var inst = eq.GetInstanceInSlot(slot);
                    if (inst != null) Add(list, inst, emp, db);
                }
            }

        list.Sort(Sort);
        return list;
    }

    private static void Add(List<EquipmentEntry> list, EquipmentInstance inst, Employee wearer, GameDatabase db)
    {
        if (inst == null) return;
        list.Add(new EquipmentEntry
        {
            instance = inst,
            data = db != null ? db.GetEquipmentData(inst.equipmentId) : null,
            wearer = wearer
        });
    }
}

/// <summary>
/// 장비 그리드 한 칸 — 장비 그림 · 우상단 정보(i) 버튼 · 우하단 착용 표시.
///
/// 정보 창과 착용 전용 아이콘은 아직 없습니다. 자리와 연결점만 있습니다:
///   정보 버튼 → Bind에 넘긴 onInfo (정보 창을 만들면 여기로 연결)
///   착용 표시 → wornIcon에 스프라이트를 넣으면 아이콘으로, 없으면 wornText 글자로
/// </summary>
public class EquipmentCell : MonoBehaviour
{
    [SerializeField] private Button button;
    [SerializeField] private Image icon;
    [Tooltip("아이콘이 없을 때 대신 보여 줄 이름")]
    [SerializeField] private TMP_Text nameLabel;
    [SerializeField] private Button infoButton;

    [Header("착용 표시 (우하단)")]
    [SerializeField] private GameObject wornMark;
    [SerializeField] private Image wornIcon;
    [SerializeField] private TMP_Text wornText;

    private Action onClick;
    private Action onInfo;

    public EquipmentEntry Entry { get; private set; }

    private void Awake()
    {
        if (button != null) button.onClick.AddListener(() => onClick?.Invoke());
        if (infoButton != null) infoButton.onClick.AddListener(() => onInfo?.Invoke());
    }

    /// <summary>칸을 채웁니다. entry가 null이면 빈 칸.</summary>
    public void Bind(EquipmentEntry entry, bool interactable, Action onClick, Action onInfo)
    {
        Entry = entry;
        this.onClick = onClick;
        this.onInfo = onInfo;

        bool has = entry != null;
        if (button != null) button.interactable = has && interactable;
        if (infoButton != null) infoButton.gameObject.SetActive(has);

        Sprite sprite = has && entry.data != null && entry.data.itemData != null ? entry.data.itemData.itemIcon : null;
        if (icon != null)
        {
            icon.sprite = sprite;
            icon.enabled = sprite != null;
        }
        if (nameLabel != null)
        {
            nameLabel.text = has && sprite == null ? entry.Name : "";
            nameLabel.gameObject.SetActive(has && sprite == null);
        }

        SetWorn(has && entry.IsWorn);
    }

    /// <summary>착용 표시. 전용 아이콘(wornIcon.sprite)이 있으면 아이콘, 없으면 글자.</summary>
    public void SetWorn(bool worn)
    {
        if (wornMark != null) wornMark.SetActive(worn);
        bool hasIcon = wornIcon != null && wornIcon.sprite != null;
        if (wornIcon != null) wornIcon.enabled = worn && hasIcon;
        if (wornText != null) wornText.gameObject.SetActive(worn && !hasIcon);
    }
}
