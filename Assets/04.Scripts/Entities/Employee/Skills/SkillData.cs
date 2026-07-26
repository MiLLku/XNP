using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 개별 스킬 노드 데이터 (ScriptableObject).
///
/// 스킬 트리에 배치될 하나의 노드를 정의합니다.
/// treePosition을 통해 중앙(0,0) 기준 정규화 좌표로 배치 위치를 지정합니다.
/// prerequisiteSkillIds에 등록된 모든 스킬이 해제되어야 이 스킬을 해제할 수 있습니다.
/// </summary>
[CreateAssetMenu(fileName = "NewSkill", menuName = "Employee/Skill Data")]
public class SkillData : ScriptableObject
{
    [Header("기본 정보")]
    [Tooltip("스킬 고유 ID (중복 불가)")]
    public int skillId;

    [Tooltip("스킬 이름 (UI 표시용)")]
    public string skillName;

    [Tooltip("스킬 설명 (툴팁 표시용)")]
    [TextArea(2, 4)]
    public string description;

    [Tooltip("스킬 아이콘 (없으면 기본 아이콘 표시)")]
    public Sprite icon;

    [Header("분류")]
    [Tooltip("스킬 카테고리. 해당 카테고리의 WorkType 결격 시 이 스킬 잠금")]
    public SkillCategory category = SkillCategory.General;

    [Header("트리 배치")]
    [Tooltip("스킬 트리 내 위치. (0,0) = 중앙. ±1 단위 = nodeSpacing px 거리")]
    public Vector2 treePosition;

    [Tooltip("이 스킬을 해제하기 위해 먼저 해제되어야 하는 스킬 ID 목록")]
    public List<int> prerequisiteSkillIds = new List<int>();

    [Header("해금 조건")]
    [Tooltip("필요한 작업 적성 레벨. category에 대응하는 작업을 그만큼 숙련해야 찍을 수 있습니다.\n" +
             "1이면 조건 없음. General 카테고리는 적성 검사를 하지 않습니다.")]
    [Range(1, WorkAptitude.MAX_LEVEL)]
    public int requiredAptitudeLevel = 1;

    [Tooltip("해제에 소모되는 스킬 포인트. 직원의 남은 포인트가 이 값 이상이어야 합니다.")]
    [Min(0)]
    public int pointCost = 1;

    [Tooltip("해금에 필요한 현재 스탯 조건. 모두 충족해야 합니다.\n" +
             "모든 직원이 같은 트리를 갖지만, 무작위 생성된 결격·스탯 차이로 찍을 수 있는 스킬이 갈립니다.")]
    public List<SkillStatRequirement> requiredStats = new List<SkillStatRequirement>();

    [Header("프리팹 기본값")]
    [Tooltip("true이면 직원 프리팹 생성/로드 시 자동으로 해제된 상태로 시작합니다")]
    public bool defaultUnlocked = false;

    [Header("부여 특성")]
    [Tooltip("이 스킬 해제 시 활성화되는 특성 효과. null이면 효과 없음.")]
    public EmployeeTrait grantedTrait;
}
