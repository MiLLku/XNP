using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// 건설장 - 대형 건축물 부품을 제작
/// 복수의 직원이 협력하여 작업 (최대 3명)
/// </summary>
public class ConstructionYard : ProductionBuilding
{
    [Header("건설장 전용")]
    [SerializeField] private int maxWorkers = 3; // 최대 작업 인원
    [SerializeField] private List<Transform> workStations; // 작업 위치들
    [SerializeField] private ParticleSystem dustParticles; // 먼지 파티클

    private List<Employee> _assignedWorkers = new List<Employee>();

    protected override string GetBuildingName()
    {
        return "건설장";
    }

    protected override void OpenWorkAssignmentPanel()
    {
        // Pending Order가 있으면 그것 사용, 아니면 현재 Order 사용
        CraftingRecipe targetRecipe = _pendingRecipe ?? _currentOrder?.recipe;
        int targetAmount = _pendingRecipe != null ? _pendingAmount : (_currentOrder?.craftAmount ?? 1);

        if (targetRecipe == null)
        {
            Debug.LogWarning($"[{GetBuildingName()}] 할당할 Order가 없습니다.");
            return;
        }

        // 임시 WorkOrder 생성 (작업자 선택용)
        WorkOrder tempOrder = new WorkOrder
        {
            orderName = $"{targetRecipe.outputItem.itemName} 제작",
            workType = WorkType.Crafting,
            maxAssignedWorkers = maxWorkers // 복수 작업자 허용
        };

        // WorkAssignmentPanel 가져오기
        var assignmentPanel = UIManager.instance.GetPanel<WorkAssignmentPanel>(UIPanelType.WorkAssignment);
        if (assignmentPanel != null)
        {
            assignmentPanel.Setup(
                tempOrder,
                OnWorkerAssigned,
                OnWorkerAssignmentClosed,
                OnWorkerAssignmentCancelled
            );

            UIManager.instance.ShowPanel(UIPanelType.WorkAssignment);
        }
        else
        {
            Debug.LogError($"[{GetBuildingName()}] WorkAssignmentPanel을 찾을 수 없습니다!");
        }
    }

    protected override void OnWorkerAssigned(Employee worker)
    {
        if (worker == null) return;

        // 직원 리스트에 추가
        if (!_assignedWorkers.Contains(worker))
        {
            _assignedWorkers.Add(worker);
        }

        // 최소 1명만 있어도 시작 가능
        if (_assignedWorkers.Count >= 1)
        {
            UIManager.instance.HidePanel(UIPanelType.WorkAssignment);

            if (_pendingRecipe != null)
            {
                StartProduction(worker);
            }
        }
    }

    protected override void StartProduction(Employee worker)
    {
        if (_pendingRecipe == null) return;

        // CraftingOrder 생성
        _currentOrder = new CraftingOrder(_pendingRecipe, _pendingAmount, this, worker);

        // 모든 할당된 직원에게 작업 지시
        for (int i = 0; i < _assignedWorkers.Count; i++)
        {
            Employee emp = _assignedWorkers[i];
            Vector3 workPos = (i < workStations.Count) ? workStations[i].position : WorkPosition;
            emp.AssignCraftingWork(_currentOrder, workPos);
        }

        Debug.Log($"[건설장] {_assignedWorkers.Count}명의 직원이 {_pendingRecipe.outputItem.itemName} x{_pendingAmount} 제작을 시작합니다.");

        // Pending 상태 초기화
        _pendingRecipe = null;
        _pendingAmount = 0;

        OnProductionStarted();
    }

    protected override void OnProductionStarted()
    {
        base.OnProductionStarted();

        if (dustParticles != null)
            dustParticles.Play();

        Debug.Log($"[건설장] {_assignedWorkers.Count}명이 협력 작업 시작!");
    }

    protected override void OnProductionCompleted(CraftingRecipe recipe, int amount)
    {
        base.OnProductionCompleted(recipe, amount);

        if (dustParticles != null)
            dustParticles.Stop();

        // 작업자 리스트 초기화
        _assignedWorkers.Clear();

        Debug.Log("[건설장] 대형 부품 제작 완료!");
    }

    public override void CancelProduction()
    {
        base.CancelProduction();

        // 작업자 리스트 초기화
        _assignedWorkers.Clear();

        if (dustParticles != null)
            dustParticles.Stop();
    }
}
