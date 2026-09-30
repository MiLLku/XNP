using UnityEngine;

/// <summary>
/// 아이템 기본 데이터 ScriptableObject.
/// 인벤토리, 제작, 건설 등에서 아이템을 식별하는 데 사용됩니다.
/// </summary>
[CreateAssetMenu(fileName = "NewItemData", menuName = "StampSystem/Item Data")]
public class ItemData : ScriptableObject
{
    [Header("아이템 기본 정보")]

    [Tooltip("아이템 종류 — itemID는 이 enum의 정수값에서 자동 계산됩니다.")]
    public ItemType itemType = ItemType.None;

    /// <summary>아이템 고유 정수 ID (itemType의 정수값, 저장·통신용).</summary>
    public int itemID => (int)itemType;

    /// <summary>아이템 표시 이름</summary>
    public string itemName;

    /// <summary>아이템 아이콘 스프라이트 (인벤토리 UI·DroppedItem 폴백 비주얼)</summary>
    public Sprite itemIcon;

    [Tooltip("아이템 정보 창에 표시할 설명")]
    [TextArea(2, 5)]
    public string description;

    [Header("파종")]
    [Tooltip("지정하면 파종 가능한 아이템(묘목)이 됩니다. 심으면 이 개체가 성장도 0으로 생깁니다.")]
    public EntityDefinition plantEntity;

    /// <summary>파종 가능한 아이템인지</summary>
    public bool IsSowable => plantEntity != null;

    [Header("드롭 비주얼")]
    [Tooltip("바닥에 떨어졌을 때 사용할 prefab. 비어있으면 DroppedItemManager의 공용 prefab을 사용합니다.")]
    public GameObject dropPrefab;

    [Header("음식")]
    [Tooltip("직원이 섭취할 수 있는 음식인지 여부.")]
    public bool isFood = false;

    [Tooltip("섭취 시 회복되는 배고픔 수치 (0~100). isFood가 true일 때만 의미가 있습니다.\n요리는 조리 때 재료 합으로 정해지므로 이 값은 재고가 없을 때의 대체값입니다.")]
    public int nutrition = 0;

    [Tooltip("요리(조리해서 만든 음식)인지, 조리 재료인지")]
    public FoodKind foodKind = FoodKind.Ingredient;

    [Tooltip("조리 재료 분류 (재료 필터: 채소/육류)")]
    public IngredientCategory ingredientCategory = IngredientCategory.Vegetable;

    [Tooltip("날로 먹으면 생식 디버프가 붙는 재료인지 (쌀·감자 O, 베리 X)")]
    public bool rawPenalty = false;

    [Tooltip("생식 디버프 기분 변화")]
    public float rawMood = -3f;

    [Tooltip("생식 디버프 지속 시간 (게임 시간, 시)")]
    [Min(0f)] public float rawMoodHours = 3f;

    /// <summary>요리인지</summary>
    public bool IsMeal => isFood && foodKind == FoodKind.Meal;

    /// <summary>침식 음식인지 (먹으면 침식이 오름)</summary>
    public bool IsErosionFood => erosionOnEat > 0f;

    [Tooltip("섭취 시 직원이 받는 침식량 (침식 작물 등). 0이면 없음.")]
    [Min(0f)] public float erosionOnEat = 0f;

    [Tooltip("섭취 시 기분 변화 (음수 = 나빠짐). 0이면 없음. 같은 음식을 또 먹으면 겹치지 않고 지속 시간만 갱신됩니다.")]
    public float moodOnEat = 0f;

    [Tooltip("기분 변화 지속 시간 (게임 시간, 시).")]
    [Min(0f)] public float moodOnEatHours = 3f;

    [Tooltip("기분 변화 표시 이름 (예: 침식된 음식을 먹음)")]
    public string moodOnEatLabel = "";

    [Header("약물 (오락 소모품)")]
    [Tooltip("직원이 복용해 재미를 회복하는 약물인지 여부.")]
    public bool isDrug = false;

    [Tooltip("복용 시 회복되는 재미 수치 (0~100). isDrug가 true일 때만 의미가 있습니다.")]
    public int funValue = 0;
}

/// <summary>음식 종류 — 요리 / 조리 재료</summary>
public enum FoodKind { Ingredient, Meal }

/// <summary>조리 재료 분류</summary>
public enum IngredientCategory { Vegetable, Meat }

/// <summary>직원이 미리 챙겨 다닐 음식 기준 (배고파서 바로 먹을 때는 적용 안 됨)</summary>
public enum FoodCarryPolicy
{
    /// <summary>요리만 챙김</summary>
    CookedOnly,
    /// <summary>아무 음식이나 챙김 (요리 → 생식 패널티 없는 재료 → 나머지 순)</summary>
    Any
}
