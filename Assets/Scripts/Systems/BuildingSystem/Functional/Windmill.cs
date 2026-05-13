using UnityEngine;

/// <summary>
/// 풍차 - 곡물을 가공하여 밀가루를 제작하는 생산 건물.
/// ProductionBuilding을 상속받아 기본 생산 기능을 사용합니다.
/// 날개가 회전하는 애니메이션이 있습니다.
/// </summary>
public class Windmill : ProductionBuilding
{
    #region 필드

    [Header("풍차 전용")]
    [SerializeField] private Transform windmillBlades;
    [SerializeField] private float rotationSpeed = 30f;
    [SerializeField] private Animator windmillAnimator;

    /// <summary>날개 회전 중인지 여부</summary>
    private bool _isRotating = false;

    #endregion

    #region ProductionBuilding 오버라이드

    /// <summary>건물 이름을 반환합니다</summary>
    protected override string GetBuildingName()
    {
        return "풍차";
    }

    /// <summary>
    /// 생산이 시작될 때 호출됩니다.
    /// 날개 회전과 애니메이터를 활성화합니다.
    /// </summary>
    protected override void OnProductionStarted()
    {
        base.OnProductionStarted();

        _isRotating = true;

        if (windmillAnimator != null)
        {
            windmillAnimator.SetBool("IsWorking", true);
        }

        Debug.Log("[풍차] 풍차가 돌기 시작합니다!");
    }

    /// <summary>
    /// 생산이 완료될 때 호출됩니다.
    /// 풍차 효과를 정지합니다.
    /// </summary>
    /// <param name="recipe">완료된 레시피</param>
    /// <param name="amount">제작 수량</param>
    protected override void OnProductionCompleted(CraftingRecipe recipe, int amount)
    {
        base.OnProductionCompleted(recipe, amount);
        StopWindmillEffects();
        Debug.Log("[풍차] 밀가루 제분 완료!");
    }

    /// <summary>
    /// 생산이 취소될 때 호출됩니다.
    /// 풍차 효과를 정지합니다.
    /// </summary>
    protected override void OnProductionCancelled()
    {
        base.OnProductionCancelled();
        StopWindmillEffects();
    }

    #endregion

    #region Update

    void Update()
    {
        // 작업 중일 때만 날개 회전
        if (_isRotating && windmillBlades != null)
        {
            windmillBlades.Rotate(Vector3.forward, rotationSpeed * Time.deltaTime);
        }
    }

    #endregion

    #region 내부 헬퍼

    /// <summary>
    /// 풍차 효과 (날개 회전, 애니메이터)를 정지합니다.
    /// </summary>
    private void StopWindmillEffects()
    {
        _isRotating = false;

        if (windmillAnimator != null)
        {
            windmillAnimator.SetBool("IsWorking", false);
        }
    }

    #endregion
}
