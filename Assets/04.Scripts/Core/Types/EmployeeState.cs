public enum EmployeeState
{
    Idle, //대기 중, 할당된 작업 없음
    Moving, //목적지로 이동 중
    Working, //작업 중
    Resting, //휴식 중
    Eating, //식사 중
    Drafted, //소집 중(플레이어 직접 조작)
    MentalBreak, //정신 이상 상태
    Dead //사망
}
