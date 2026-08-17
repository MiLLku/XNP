/// <summary>
/// 직원 침식 단계 열거형.
/// erosionLevel 수치에 따라 0~4단계로 분류됩니다.
/// </summary>
public enum ErosionStage
{
    /// <summary>정상 (0 ~ 49) — 이상 없음</summary>
    Normal = 0,

    /// <summary>오염 초기 (50 ~ 99) — 작업/이동 속도 소폭 감소</summary>
    EarlyContamination = 1,

    /// <summary>오염 심화 (100 ~ 149) — 속도 감소 + 이상 행동 확률 발생</summary>
    Advanced = 2,

    /// <summary>오염 위기 (150 ~ 199) — 강한 속도 감소 + 높은 이상 행동 확률 + 침식 전파 오라</summary>
    Critical = 3,

    /// <summary>완전 침식 (200) — 직원 → 제놉스 변이</summary>
    FullErosion = 4
}

/// <summary>
/// 침식 이상 행동 타입 열거형.
/// IAbnormalBehavior 구현체를 AbnormalBehaviorRegistry에 등록할 때 사용합니다.
/// 새 행동을 추가하려면 여기에 값을 추가하고 IAbnormalBehavior 구현체를 만드세요.
/// </summary>
public enum AbnormalBehaviorType
{
    /// <summary>없음 (기본값)</summary>
    None = 0,

    // ── 3단계 이상 행동 ─────────────────────────────────

    /// <summary>명령 무시 — 다음 작업 명령 1회 거부</summary>
    IgnoreCommand = 1,

    /// <summary>무작위 이동 — 인접 타일 1~3칸 무작위 이동</summary>
    RandomMove = 2,

    /// <summary>작업 중단 — 진행 중인 작업 즉시 중단 후 5초 대기</summary>
    WorkStop = 3,

    /// <summary>우호 공격 — 인접 아군 1명에게 약한 물리 공격</summary>
    FriendlyAttack = 4,

    // ── 4단계 강화 이상 행동 ─────────────────────────────

    /// <summary>명령 무시 (강화) — 다음 명령 3회 연속 거부</summary>
    IgnoreCommandEnhanced = 5,

    /// <summary>적 방향 이동 — 가장 가까운 제놉스 방향으로 이동</summary>
    MoveTowardEnemy = 6,

    /// <summary>아군 공격 (강화) — 인접 아군 공격 + 침식 전파</summary>
    FriendlyAttackEnhanced = 7,

    /// <summary>도주 — 맵 가장자리 방향으로 달려감</summary>
    Flee = 8,

    /// <summary>침식 흔적 폭발 — 이동 흔적 타일에서 오라 침식 순간 발생</summary>
    ErosionTrailExplosion = 9,

    // ── 지속형 이상 행동 (Tick 구동) ─────────────────────
    // 아래 3종은 Execute 한 번으로 끝나지 않고 지속 시간 동안 매 프레임 Tick으로 동작한다.
    // 세이브에 int로 직렬화되므로 기존 값을 바꾸지 말고 항상 뒤에 추가할 것.

    /// <summary>침식 폭주 — 발생 위치에 멈춰 서서 주변에 침식을 흩뿌린다 (소집·조작 불가)</summary>
    ErosionOutburst = 10,

    /// <summary>건물 파괴 충동 — 주변의 내구도 있는 건설물을 공격한다 (벽·바닥 등 기반시설 제외)</summary>
    AttackBuilding = 11
}
