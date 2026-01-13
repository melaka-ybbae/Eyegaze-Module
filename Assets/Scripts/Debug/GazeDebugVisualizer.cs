using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 시선 추적 디버그 시각화
/// 화면에 현재 시선 위치와 상태를 표시합니다.
/// </summary>
public class GazeDebugVisualizer : MonoBehaviour
{
    [Header("Gaze Cursor")]
    [SerializeField] private RectTransform _gazeCursor;
    [SerializeField] private Image _gazeCursorImage;
    [SerializeField] private float _cursorSize = 30f;
    [SerializeField] private Color _trackingColor = Color.green;
    [SerializeField] private Color _lostColor = Color.red;

    [Header("Smoothing")]
    [SerializeField] private bool _useSmoothing = true;
    [SerializeField] [Range(0.01f, 1f)] private float _smoothingFactor = 0.1f;

    [Header("Debug Info")]
    [SerializeField] private Text _debugText;
    [SerializeField] private bool _showDebugInfo = true;

    [Header("Iris Visualization")]
    [SerializeField] private bool _showIrisMarkers = true;
    [SerializeField] private RectTransform _leftIrisMarker;
    [SerializeField] private RectTransform _rightIrisMarker;

    private Canvas _canvas;
    private RectTransform _canvasRect;
    private Vector2 _smoothedPosition;
    private bool _isInitialized = false;

    private void Start()
    {
        SetupCanvas();
        SetupGazeCursor();
        SetupIrisMarkers();

        if (GazeTracker.Instance != null)
        {
            GazeTracker.Instance.OnGazeUpdated += OnGazeUpdated;
            GazeTracker.Instance.OnTrackingLost += OnTrackingLost;
            GazeTracker.Instance.OnTrackingStarted += OnTrackingStarted;
        }

        _isInitialized = true;
    }

    private void OnDestroy()
    {
        if (GazeTracker.Instance != null)
        {
            GazeTracker.Instance.OnGazeUpdated -= OnGazeUpdated;
            GazeTracker.Instance.OnTrackingLost -= OnTrackingLost;
            GazeTracker.Instance.OnTrackingStarted -= OnTrackingStarted;
        }
    }

    private void SetupCanvas()
    {
        _canvas = GetComponentInParent<Canvas>();
        if (_canvas == null)
        {
            // Canvas가 없으면 새로 생성
            var canvasObj = new GameObject("GazeDebugCanvas");
            _canvas = canvasObj.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = 1000; // 최상위에 표시
            canvasObj.AddComponent<CanvasScaler>();
            canvasObj.AddComponent<GraphicRaycaster>();
            transform.SetParent(canvasObj.transform);
        }
        _canvasRect = _canvas.GetComponent<RectTransform>();
    }

    private void SetupGazeCursor()
    {
        if (_gazeCursor == null)
        {
            var cursorObj = new GameObject("GazeCursor");
            cursorObj.transform.SetParent(transform);
            _gazeCursor = cursorObj.AddComponent<RectTransform>();
            _gazeCursorImage = cursorObj.AddComponent<Image>();

            // 원형 커서 생성 (기본 스프라이트 사용)
            _gazeCursorImage.color = _trackingColor;
        }

        _gazeCursor.sizeDelta = new Vector2(_cursorSize, _cursorSize);
        _gazeCursor.gameObject.SetActive(false);
    }

    private void SetupIrisMarkers()
    {
        if (!_showIrisMarkers) return;

        if (_leftIrisMarker == null)
        {
            _leftIrisMarker = CreateIrisMarker("LeftIrisMarker", Color.cyan);
        }

        if (_rightIrisMarker == null)
        {
            _rightIrisMarker = CreateIrisMarker("RightIrisMarker", Color.magenta);
        }
    }

    private RectTransform CreateIrisMarker(string name, Color color)
    {
        var markerObj = new GameObject(name);
        markerObj.transform.SetParent(transform);
        var rect = markerObj.AddComponent<RectTransform>();
        var image = markerObj.AddComponent<Image>();
        image.color = color;
        rect.sizeDelta = new Vector2(15f, 15f);
        markerObj.SetActive(false);
        return rect;
    }

    private void OnGazeUpdated(GazeData data)
    {
        if (!_isInitialized) return;

        // 시선 커서 업데이트
        UpdateGazeCursor(data);

        // 홍채 마커 업데이트
        UpdateIrisMarkers(data);

        // 디버그 정보 업데이트
        UpdateDebugInfo(data);
    }

    private void UpdateGazeCursor(GazeData data)
    {
        if (_gazeCursor == null || _canvasRect == null) return;

        _gazeCursor.gameObject.SetActive(true);
        _gazeCursorImage.color = _trackingColor;

        // 정규화된 좌표를 화면 좌표로 변환
        Vector2 screenPos = new Vector2(
            data.ScreenPosition.x * _canvasRect.rect.width,
            (1f - data.ScreenPosition.y) * _canvasRect.rect.height // Y축 반전
        );

        // 스무딩 적용
        if (_useSmoothing)
        {
            _smoothedPosition = Vector2.Lerp(_smoothedPosition, screenPos, _smoothingFactor);
            _gazeCursor.anchoredPosition = _smoothedPosition - _canvasRect.rect.size / 2f;
        }
        else
        {
            _gazeCursor.anchoredPosition = screenPos - _canvasRect.rect.size / 2f;
        }

        // 신뢰도에 따라 커서 크기 조절
        float sizeMultiplier = 0.5f + data.Confidence * 0.5f;
        _gazeCursor.sizeDelta = new Vector2(_cursorSize * sizeMultiplier, _cursorSize * sizeMultiplier);
    }

    private void UpdateIrisMarkers(GazeData data)
    {
        if (!_showIrisMarkers || _canvasRect == null) return;

        if (_leftIrisMarker != null)
        {
            _leftIrisMarker.gameObject.SetActive(true);
            Vector2 leftPos = new Vector2(
                data.LeftIrisCenter.x * _canvasRect.rect.width,
                (1f - data.LeftIrisCenter.y) * _canvasRect.rect.height
            );
            _leftIrisMarker.anchoredPosition = leftPos - _canvasRect.rect.size / 2f;
        }

        if (_rightIrisMarker != null)
        {
            _rightIrisMarker.gameObject.SetActive(true);
            Vector2 rightPos = new Vector2(
                data.RightIrisCenter.x * _canvasRect.rect.width,
                (1f - data.RightIrisCenter.y) * _canvasRect.rect.height
            );
            _rightIrisMarker.anchoredPosition = rightPos - _canvasRect.rect.size / 2f;
        }
    }

    private void UpdateDebugInfo(GazeData data)
    {
        if (_debugText == null || !_showDebugInfo) return;

        _debugText.text = $"Gaze Tracking Debug\n" +
                          $"==================\n" +
                          $"Screen Pos: ({data.ScreenPosition.x:F3}, {data.ScreenPosition.y:F3})\n" +
                          $"Left Iris: ({data.LeftIrisCenter.x:F3}, {data.LeftIrisCenter.y:F3})\n" +
                          $"Right Iris: ({data.RightIrisCenter.x:F3}, {data.RightIrisCenter.y:F3})\n" +
                          $"Confidence: {data.Confidence:P0}\n" +
                          $"Tracking: {data.IsValid}\n" +
                          $"Calibrated: {GazeTracker.Instance?.Calibrator?.IsCalibrated ?? false}";
    }

    private void OnTrackingLost()
    {
        if (_gazeCursor != null)
        {
            _gazeCursorImage.color = _lostColor;
        }

        if (_leftIrisMarker != null) _leftIrisMarker.gameObject.SetActive(false);
        if (_rightIrisMarker != null) _rightIrisMarker.gameObject.SetActive(false);

        if (_debugText != null && _showDebugInfo)
        {
            _debugText.text = "Gaze Tracking Debug\n" +
                              "==================\n" +
                              "Status: TRACKING LOST\n" +
                              "Face not detected";
        }
    }

    private void OnTrackingStarted()
    {
        if (_gazeCursorImage != null)
        {
            _gazeCursorImage.color = _trackingColor;
        }
    }

    /// <summary>
    /// 디버그 시각화 토글
    /// </summary>
    public void ToggleDebugVisualization()
    {
        _showDebugInfo = !_showDebugInfo;
        if (_debugText != null)
        {
            _debugText.gameObject.SetActive(_showDebugInfo);
        }
    }

    /// <summary>
    /// 홍채 마커 토글
    /// </summary>
    public void ToggleIrisMarkers()
    {
        _showIrisMarkers = !_showIrisMarkers;
        if (_leftIrisMarker != null) _leftIrisMarker.gameObject.SetActive(_showIrisMarkers);
        if (_rightIrisMarker != null) _rightIrisMarker.gameObject.SetActive(_showIrisMarkers);
    }
}
