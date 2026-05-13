using UnityEngine;

/// <summary>
/// 메인 카메라 컨트롤러.
/// WASD 이동, 마우스 휠 줌, 맵 경계 제한을 담당합니다.
/// </summary>
public class CameraController : MonoBehaviour
{
    #region 상수

    private const float DEFAULT_START_X = 100f;
    private const float DEFAULT_START_Y = 150f;

    #endregion

    #region 필드

    [Header("이동 설정")]
    [Tooltip("카메라의 이동 속도")]
    [SerializeField] private float moveSpeed = 20f;

    [Header("줌 설정")]
    [Tooltip("마우스 휠 줌 속도")]
    [SerializeField] private float zoomSpeed = 10f;

    [Tooltip("최대 확대 (값이 작을수록 가까움)")]
    [SerializeField] private float minOrthographicSize = 3f;

    [Tooltip("최대 축소 (값이 클수록 멀어짐)")]
    [SerializeField] private float maxOrthographicSize = 25f;

    private Camera _cam;
    private float _minX, _maxX, _minY, _maxY;

    #endregion

    #region 생명주기

    void Start()
    {
        _cam = Camera.main;
        if (_cam == null)
        {
            Debug.LogError("[CameraController] Camera.main이 null입니다! MainCamera 태그를 확인하세요.");
            enabled = false;
            return;
        }

        // TODO [맵시스템]: MapGenerator의 baseGroundLevel 참조로 시작 위치 개선
        Vector3 startPos = new Vector3(DEFAULT_START_X, DEFAULT_START_Y, transform.position.z);

        CalculateBounds();
        ClampPosition(ref startPos);
        transform.position = startPos;
    }

    void Update()
    {
        if (_cam == null) return;

        // WASD 이동
        float horizontalInput = Input.GetAxisRaw("Horizontal");
        float verticalInput = Input.GetAxisRaw("Vertical");
        Vector3 moveDirection = (Vector3.right * horizontalInput) + (Vector3.up * verticalInput);
        transform.Translate(moveDirection.normalized * moveSpeed * Time.deltaTime);

        // 마우스 휠 줌
        float scrollInput = Input.GetAxis("Mouse ScrollWheel");
        if (scrollInput != 0f)
        {
            float currentZoom = _cam.orthographicSize;
            currentZoom -= scrollInput * zoomSpeed;
            currentZoom = Mathf.Clamp(currentZoom, minOrthographicSize, maxOrthographicSize);
            _cam.orthographicSize = currentZoom;
        }
    }

    void LateUpdate()
    {
        if (_cam == null) return;

        CalculateBounds();
        Vector3 pos = transform.position;
        ClampPosition(ref pos);
        transform.position = pos;
    }

    #endregion

    #region 경계 제한

    /// <summary>
    /// 현재 줌 레벨에 맞는 카메라 이동 범위를 계산합니다.
    /// </summary>
    private void CalculateBounds()
    {
        float camHeight = _cam.orthographicSize;
        float camWidth = _cam.orthographicSize * _cam.aspect;

        _minX = camWidth;
        _maxX = GameMap.MAP_WIDTH - camWidth;
        _minY = camHeight;
        _maxY = GameMap.MAP_HEIGHT - camHeight;
    }

    private void ClampPosition(ref Vector3 pos)
    {
        pos.x = Mathf.Clamp(pos.x, _minX, _maxX);
        pos.y = Mathf.Clamp(pos.y, _minY, _maxY);
    }

    #endregion

    #region 공개 API

    /// <summary>
    /// 현재 마우스 위치를 월드 좌표로 반환합니다.
    /// </summary>
    public Vector3 GetMouseWorldPosition()
    {
        Vector3 mousePos = Input.mousePosition;
        mousePos.z = -_cam.transform.position.z;
        return _cam.ScreenToWorldPoint(mousePos);
    }

    #endregion
}
