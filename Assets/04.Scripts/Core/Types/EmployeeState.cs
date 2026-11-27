public enum EmployeeState
{
    Idle, //대기 중, 할당된 작업 없음
    Moving, //목적지로 이동 중
    Working, //작업 중
    Resting, //휴식 중
    Eating, //식사 중
    Drafted, //소집 중(플레이어 직접 조작)
    MentalBreak, //정신 이상 상태
    Dead, //사망
    Downed //쓰러짐 — 체력 0. 움직이지 못하고, 치료 담당이 침대로 옮겨 소생시켜야 깨어난다 (EmployeeDowned)
}
