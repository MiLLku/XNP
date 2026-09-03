using Cysharp.Threading.Tasks;

/// <summary>
/// UniTask 대기 시점 상수.
///
/// 어느 PlayerLoop 단계에서 깨어날지는 "Update()와의 순서"를 바꾸기 때문에
/// 값을 호출부마다 고르지 않고 여기서 한 번만 정합니다.
/// </summary>
public static class GameLoop
{
    /// <summary>
    /// 매 프레임 대기에 쓰는 타이밍.
    ///
    /// <see cref="PlayerLoopTiming.Update"/>는 Update 단계의 <b>맨 앞</b>에 주입되어
    /// MonoBehaviour.Update()보다 먼저 실행됩니다. 반면 코루틴의 <c>yield return null</c>은
    /// Update() <b>뒤</b>(ScriptRunDelayedDynamicFrameRate)에서 재개됩니다.
    ///
    /// 이 프로젝트의 작업·이동 루프는 Update()에서 내려진 AI 결정을 이어받아 도는 구조라
    /// "Update 이후"라는 순서가 유지돼야 합니다. 그래서 Update 단계 끝에 주입되는
    /// LastUpdate를 씁니다 — 코루틴에서 옮겨온 코드가 같은 프레임 순서를 갖습니다.
    /// </summary>
    public const PlayerLoopTiming Frame = PlayerLoopTiming.LastUpdate;
}
