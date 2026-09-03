using System;
using MessagePipe;
using UnityEngine;

/// <summary>
/// MessagePipe 기반 전역 메시지 버스.
///
/// 매니저 간 · 매니저→UI 통신을 발행자/구독자가 서로를 모르는 pub-sub으로 처리합니다.
/// 기존의 "구독자가 상대 매니저 싱글톤을 찾아 += 로 매달리는" 방식이 갖던
/// 초기화 순서 의존(Awake 시점에 instance가 아직 null)과 해지 누락 문제를 없앱니다.
///
/// 사용법:
///   발행 — GameMessageBus.Publish(new ZoneCreatedMessage(zone));
///   구독 — _subscriptions = DisposableBag.Create(
///              GameMessageBus.Subscribe&lt;ZoneCreatedMessage&gt;(HandleZoneCreated));
///          해지 — _subscriptions?.Dispose();
///
/// 주의: MessagePipe의 내장 컨테이너는 오픈 제네릭을 지원하지 않으므로
///       모든 메시지 타입은 GameMessageBus.Registry.cs에 명시적으로 등록해야 합니다.
///       등록되지 않은 타입을 발행/구독하면 명확한 예외 메시지와 함께 실패합니다.
/// </summary>
public static partial class GameMessageBus
{
    #region 상태

    private static IServiceProvider _provider;

    /// <summary>
    /// 컨테이너 재구성 세대. 플레이 진입마다 증가하며,
    /// 도메인 리로드가 꺼진 환경에서 남아 있는 제네릭 캐시를 무효화합니다.
    /// </summary>
    private static int _generation;

    /// <summary>버스가 초기화되어 발행/구독이 가능한 상태인지</summary>
    public static bool IsReady => _provider != null;

    /// <summary>
    /// 타입별 발행자/구독자 캐시.
    /// 매 호출마다 컨테이너를 조회하지 않도록 제네릭 정적 필드에 보관합니다.
    /// </summary>
    private static class Endpoint<TMessage>
    {
        public static int generation = -1;
        public static IPublisher<TMessage> publisher;
        public static ISubscriber<TMessage> subscriber;
    }

    #endregion

    #region 초기화

    /// <summary>
    /// 플레이 진입 시 컨테이너를 새로 구성합니다.
    /// 씬 로드보다 먼저 실행되므로 어떤 Awake보다 앞서 버스가 준비됩니다.
    /// 매번 새 컨테이너를 만들기 때문에 이전 플레이 세션의 구독은 함께 버려집니다.
    ///
    /// 에디터에서 ContextMenu 등으로 플레이 밖에서 발행하는 경로도 있으므로,
    /// 준비되지 않은 상태로 처음 쓰이면 그 시점에 한 번 더 구성합니다.
    /// </summary>
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void Initialize()
    {
        var builder = new BuiltinContainerBuilder();
        builder.AddMessagePipe();
        RegisterMessages(builder);

        _provider = builder.BuildServiceProvider();
        GlobalMessagePipe.SetProvider(_provider);
        _generation++;
    }

    #endregion

    #region 발행 / 구독

    /// <summary>
    /// 메시지를 모든 구독자에게 즉시(동기) 전달합니다.
    /// 구독자가 없으면 아무 일도 일어나지 않습니다.
    /// </summary>
    public static void Publish<TMessage>(TMessage message)
    {
        var publisher = GetPublisher<TMessage>();
        if (publisher == null) return;

        publisher.Publish(message);
    }

    /// <summary>
    /// 메시지를 구독합니다.
    /// 반환된 IDisposable을 Dispose하면 구독이 해지됩니다 — 반드시 보관하세요.
    /// </summary>
    /// <returns>구독 해지용 핸들 (버스 미초기화 시 아무 동작도 하지 않는 핸들)</returns>
    public static IDisposable Subscribe<TMessage>(Action<TMessage> handler)
    {
        if (handler == null) return DisposableBag.Empty;

        var subscriber = GetSubscriber<TMessage>();
        if (subscriber == null) return DisposableBag.Empty;

        return subscriber.Subscribe(handler);
    }

    #endregion

    #region 내부 조회

    private static IPublisher<TMessage> GetPublisher<TMessage>()
    {
        if (!EnsureReady<TMessage>()) return null;

        if (Endpoint<TMessage>.generation != _generation)
        {
            Endpoint<TMessage>.publisher = GlobalMessagePipe.GetPublisher<TMessage>();
            Endpoint<TMessage>.subscriber = GlobalMessagePipe.GetSubscriber<TMessage>();
            Endpoint<TMessage>.generation = _generation;
        }

        return Endpoint<TMessage>.publisher;
    }

    private static ISubscriber<TMessage> GetSubscriber<TMessage>()
    {
        if (GetPublisher<TMessage>() == null) return null;
        return Endpoint<TMessage>.subscriber;
    }

    /// <summary>
    /// 버스 준비 상태와 메시지 타입 등록 여부를 확인합니다.
    /// 미등록 타입은 조용히 무시되지 않고 어디를 고쳐야 하는지 로그로 알립니다.
    /// </summary>
    private static bool EnsureReady<TMessage>()
    {
        if (!IsReady) Initialize();

        if (Endpoint<TMessage>.generation == _generation) return true;

        if (_provider.GetService(typeof(IPublisher<TMessage>)) == null)
        {
            Debug.LogError(
                $"[GameMessageBus] '{typeof(TMessage).Name}' 메시지가 등록되지 않았습니다. " +
                $"Core/Messaging/GameMessageBus.Registry.cs에 AddMessageBroker<{typeof(TMessage).Name}>()를 추가하세요.");
            return false;
        }

        return true;
    }

    #endregion
}
