using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// 스케줄 그리드의 시간 셀 1칸. 클릭하거나 마우스를 누른 채 지나가면(드래그) 브러시로 칠해진다.
/// </summary>
[RequireComponent(typeof(Image))]
public class ScheduleCellUI : MonoBehaviour, IPointerDownHandler, IPointerEnterHandler
{
    private int hour;
    private ScheduleRowUI row;
    private Image image;

    public void Init(int hourIndex, ScheduleRowUI ownerRow)
    {
        hour = hourIndex;
        row = ownerRow;
        if (image == null) image = GetComponent<Image>();
    }

    public void SetColor(Color color)
    {
        if (image == null) image = GetComponent<Image>();
        if (image != null) image.color = color;
    }

    public void OnPointerDown(PointerEventData eventData)
    {
        row?.Paint(hour);
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        // 마우스 왼쪽 버튼을 누른 채 지나가면 드래그 칠하기
        if (Input.GetMouseButton(0))
            row?.Paint(hour);
    }
}
