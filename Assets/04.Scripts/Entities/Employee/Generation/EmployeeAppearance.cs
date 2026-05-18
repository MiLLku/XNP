using UnityEngine;

/// <summary>
/// 직원 외형 데이터. 헤어 스타일/색상을 저장하며 추후 신체·의상 슬롯으로 확장 가능합니다.
/// EmployeeData에 직렬화되어 인스펙터 및 무작위 생성 양쪽에서 사용됩니다.
/// </summary>
[System.Serializable]
public class EmployeeAppearance
{
    [Header("헤어")]
    [Tooltip("헤어 스타일 스프라이트. null이면 Hair 자식 렌더러를 비활성화합니다.")]
    public Sprite hairSprite;

    [Tooltip("헤어 색상 (Sprite에 multiply 적용)")]
    public Color hairColor = Color.black;

    // 추후 확장 예시 (주석 해제 후 사용):
    // public Sprite bodySprite;
    // public Sprite faceSprite;
    // public Color skinColor = new Color(1f, 0.87f, 0.73f);
}
