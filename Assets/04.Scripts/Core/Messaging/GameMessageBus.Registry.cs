using MessagePipe;

/// <summary>
/// 메시지 타입 등록 목록.
///
/// MessagePipe의 내장 DI 컨테이너(BuiltinContainerBuilder)는 오픈 제네릭을 지원하지 않아
/// 사용할 메시지 타입을 미리 등록해야 합니다. 리플렉션 자동 등록 대신 명시적으로 나열하는 이유:
///   - IL2CPP 빌드에서 제네릭 코드가 스트리핑되지 않도록 보장
///   - "이 게임이 주고받는 메시지 전체"가 한 화면에 드러남
///
/// GameMessages.cs에 메시지를 추가하면 이 목록에도 같은 이름을 추가하세요.
/// 빠뜨리면 첫 발행/구독 시점에 어느 타입이 누락됐는지 에러 로그로 알려줍니다.
/// </summary>
public static partial class GameMessageBus
{
    private static void RegisterMessages(BuiltinContainerBuilder builder)
    {
        // 시간
        builder.AddMessageBroker<HourChangedMessage>();
        builder.AddMessageBroker<NewDayMessage>();
        builder.AddMessageBroker<GameSpeedChangedMessage>();
        builder.AddMessageBroker<GamePauseStateChangedMessage>();

        // 직원
        builder.AddMessageBroker<EmployeeSpawnedMessage>();
        builder.AddMessageBroker<EmployeeRemovedMessage>();
        builder.AddMessageBroker<EmployeeMutatedMessage>();

        // 상호작용
        builder.AddMessageBroker<InteractionModeChangedMessage>();
        builder.AddMessageBroker<EmployeeSelectionChangedMessage>();
        builder.AddMessageBroker<EditingZoneChangedMessage>();

        // 인벤토리
        builder.AddMessageBroker<InventoryChangedMessage>();
        builder.AddMessageBroker<EquipmentPoolChangedMessage>();

        // 건설
        builder.AddMessageBroker<BuildingPlacementModeChangedMessage>();
        builder.AddMessageBroker<ConstructionSiteCreatedMessage>();
        builder.AddMessageBroker<ConstructionSiteCompletedMessage>();
        builder.AddMessageBroker<ConstructionSiteCancelledMessage>();

        // 방·구역
        builder.AddMessageBroker<RoomsRebuiltMessage>();
        builder.AddMessageBroker<ZoneCreatedMessage>();
        builder.AddMessageBroker<ZoneDeletedMessage>();
        builder.AddMessageBroker<ZoneTilesChangedMessage>();

        // 이벤트
        builder.AddMessageBroker<GameEventTriggeredMessage>();
        builder.AddMessageBroker<PersistentEventStartedMessage>();
        builder.AddMessageBroker<PersistentEventEndedMessage>();
        builder.AddMessageBroker<GameEventChoiceMadeMessage>();

        // 알림
        builder.AddMessageBroker<LetterAddedMessage>();
        builder.AddMessageBroker<LetterRemovedMessage>();
        builder.AddMessageBroker<AlertsRefreshedMessage>();

        // 습격
        builder.AddMessageBroker<RaidStartedMessage>();
        builder.AddMessageBroker<RaidWaveStartedMessage>();
        builder.AddMessageBroker<RaidCompletedMessage>();

        // 연구
        builder.AddMessageBroker<ResearchPointsChangedMessage>();
        builder.AddMessageBroker<ResearchStartedMessage>();
        builder.AddMessageBroker<ResearchCompletedMessage>();
        builder.AddMessageBroker<ResearchCancelledMessage>();
        builder.AddMessageBroker<ResearchProgressChangedMessage>();
        builder.AddMessageBroker<ResearchNodeStateChangedMessage>();
        builder.AddMessageBroker<BuildingUnlockedMessage>();
        builder.AddMessageBroker<RecipeUnlockedMessage>();

        // 스킬
        builder.AddMessageBroker<SkillPointCapIncreasedMessage>();

        // 디버그
        builder.AddMessageBroker<DebugFlagsChangedMessage>();
    }
}
