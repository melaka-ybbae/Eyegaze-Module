using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 시선 추적 디버그 환경을 자동으로 설정합니다.
/// 이 컴포넌트를 빈 GameObject에 추가하면 필요한 모든 UI를 자동 생성합니다.
/// </summary>
public class GazeDebugSetup : MonoBehaviour
{
    [Header("Auto Setup")]
    [SerializeField] private bool _autoSetupOnStart = true;
    [SerializeField] private bool _showCameraPreview = true;
    [SerializeField] private Vector2 _previewSize = new Vector2(320, 240);

    private Canvas _mainCanvas;
    private RawImage _cameraPreview;
    private Text _debugText;

    private void Start()
    {
        if (_autoSetupOnStart)
        {
            SetupDebugEnvironment();
        }
    }

    private void Update()
    {
        // WebCamTexture는 매 프레임 자동 업데이트되지만,
        // RawImage가 제대로 반영하지 못하는 경우 강제 갱신
        if (_cameraConnected && _cameraPreview != null && GazeTracker.Instance != null)
        {
            var webCam = GazeTracker.Instance.WebCamTexture;
            if (webCam != null && webCam.isPlaying && webCam.didUpdateThisFrame)
            {
                // 텍스처가 업데이트되었을 때 RawImage 강제 갱신
                _cameraPreview.texture = webCam;
            }
        }
    }

    [ContextMenu("Setup Debug Environment")]
    public void SetupDebugEnvironment()
    {
        CreateMainCanvas();
        CreateCameraPreview();
        CreateDebugText();
        CreateGazeCursor();

        // 항상 코루틴 시작 (GazeTracker가 나중에 초기화될 수 있음)
        StartCoroutine(WaitAndConnectCamera());

        Debug.Log("[GazeDebugSetup] 디버그 환경 설정 완료");
    }

    private void CreateMainCanvas()
    {
        var canvasObj = new GameObject("DebugCanvas");
        _mainCanvas = canvasObj.AddComponent<Canvas>();
        _mainCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
        _mainCanvas.sortingOrder = 100;

        var scaler = canvasObj.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;

        canvasObj.AddComponent<GraphicRaycaster>();
    }

    private void CreateCameraPreview()
    {
        if (!_showCameraPreview) return;

        // 컨테이너 (테두리 역할)
        var containerObj = new GameObject("CameraPreviewContainer");
        containerObj.transform.SetParent(_mainCanvas.transform, false);
        var containerImage = containerObj.AddComponent<Image>();
        containerImage.color = Color.white;
        var containerRect = containerImage.rectTransform;
        containerRect.anchorMin = new Vector2(0, 1);
        containerRect.anchorMax = new Vector2(0, 1);
        containerRect.pivot = new Vector2(0, 1);
        containerRect.anchoredPosition = new Vector2(10, -10);
        containerRect.sizeDelta = _previewSize + new Vector2(4, 4); // 테두리 두께

        // 카메라 프리뷰 (RawImage)
        var previewObj = new GameObject("CameraPreview");
        previewObj.transform.SetParent(containerObj.transform, false);
        _cameraPreview = previewObj.AddComponent<RawImage>();
        _cameraPreview.color = Color.gray;
        var rect = _cameraPreview.rectTransform;
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = Vector2.zero;
        rect.sizeDelta = _previewSize;

        // 라벨 추가
        var labelObj = new GameObject("Label");
        labelObj.transform.SetParent(containerObj.transform, false);
        var labelText = labelObj.AddComponent<Text>();
        labelText.text = "Camera Preview";
        labelText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        labelText.fontSize = 14;
        labelText.color = Color.white;
        labelText.alignment = TextAnchor.UpperCenter;
        var labelRect = labelText.rectTransform;
        labelRect.anchorMin = new Vector2(0, 1);
        labelRect.anchorMax = new Vector2(1, 1);
        labelRect.pivot = new Vector2(0.5f, 0);
        labelRect.anchoredPosition = new Vector2(0, 5);
        labelRect.sizeDelta = new Vector2(0, 20);
    }

    private void CreateDebugText()
    {
        var textObj = new GameObject("DebugText");
        textObj.transform.SetParent(_mainCanvas.transform, false);

        // 배경 패널
        var bgImage = textObj.AddComponent<Image>();
        bgImage.color = new Color(0, 0, 0, 0.7f);

        var bgRect = bgImage.rectTransform;
        bgRect.anchorMin = new Vector2(1, 1);
        bgRect.anchorMax = new Vector2(1, 1);
        bgRect.pivot = new Vector2(1, 1);
        bgRect.anchoredPosition = new Vector2(-10, -10);
        bgRect.sizeDelta = new Vector2(300, 200);

        // 텍스트
        var textChildObj = new GameObject("Text");
        textChildObj.transform.SetParent(textObj.transform, false);
        _debugText = textChildObj.AddComponent<Text>();
        _debugText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        _debugText.fontSize = 16;
        _debugText.color = Color.white;
        _debugText.alignment = TextAnchor.UpperLeft;

        var textRect = _debugText.rectTransform;
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = new Vector2(10, 10);
        textRect.offsetMax = new Vector2(-10, -10);

        _debugText.text = "Gaze Tracking Debug\n==================\nWaiting for GazeTracker...";
    }

    private RectTransform _gazeCursor;
    private Image _gazeCursorImage;
    private RectTransform _leftIrisMarker;
    private RectTransform _rightIrisMarker;
    private Vector2 _smoothedPosition;
    private bool _cameraConnected = false;

    private void CreateGazeCursor()
    {
        // 시선 커서
        var cursorObj = new GameObject("GazeCursor");
        cursorObj.transform.SetParent(_mainCanvas.transform, false);
        _gazeCursor = cursorObj.AddComponent<RectTransform>();
        _gazeCursorImage = cursorObj.AddComponent<Image>();
        _gazeCursorImage.color = Color.green;
        _gazeCursor.sizeDelta = new Vector2(30, 30);
        _gazeCursor.gameObject.SetActive(false);

        // 왼쪽 홍채 마커
        var leftObj = new GameObject("LeftIris");
        leftObj.transform.SetParent(_mainCanvas.transform, false);
        _leftIrisMarker = leftObj.AddComponent<RectTransform>();
        var leftImg = leftObj.AddComponent<Image>();
        leftImg.color = Color.cyan;
        _leftIrisMarker.sizeDelta = new Vector2(15, 15);
        _leftIrisMarker.gameObject.SetActive(false);

        // 오른쪽 홍채 마커
        var rightObj = new GameObject("RightIris");
        rightObj.transform.SetParent(_mainCanvas.transform, false);
        _rightIrisMarker = rightObj.AddComponent<RectTransform>();
        var rightImg = rightObj.AddComponent<Image>();
        rightImg.color = Color.magenta;
        _rightIrisMarker.sizeDelta = new Vector2(15, 15);
        _rightIrisMarker.gameObject.SetActive(false);
    }

    private System.Collections.IEnumerator WaitAndConnectCamera()
    {
        _debugText.text = "Gaze Tracking Debug\n==================\nWaiting for GazeTracker...";

        // GazeTracker 인스턴스 대기
        float waitTime = 0f;
        while (GazeTracker.Instance == null)
        {
            waitTime += 0.5f;
            _debugText.text = $"Gaze Tracking Debug\n==================\nWaiting for GazeTracker... ({waitTime:F1}s)";
            yield return new WaitForSeconds(0.5f);

            if (waitTime > 30f)
            {
                _debugText.text = "Gaze Tracking Debug\n==================\nGazeTracker not found!\nCheck if GazeTracker is in scene.";
                Debug.LogError("[GazeDebugSetup] GazeTracker 인스턴스 타임아웃");
                yield break;
            }
        }

        Debug.Log("[GazeDebugSetup] GazeTracker 인스턴스 발견");
        _debugText.text = "Gaze Tracking Debug\n==================\nGazeTracker found!\nWaiting for initialization...";

        // GazeTracker 초기화 대기 (최대 60초)
        float initWaitTime = 0f;
        while (!GazeTracker.Instance.IsInitialized)
        {
            initWaitTime += 0.5f;
            waitTime += 0.5f;

            string status = "Initializing";
            if (initWaitTime > 5f) status = "Loading model...";
            if (initWaitTime > 15f) status = "Creating FaceLandmarker...";
            if (initWaitTime > 30f) status = "Still loading (slow device?)...";

            _debugText.text = $"Gaze Tracking Debug\n==================\n{status} ({initWaitTime:F1}s)\n\nCheck logcat for details";
            yield return new WaitForSeconds(0.5f);

            if (initWaitTime > 60f)
            {
                _debugText.text = "Gaze Tracking Debug\n==================\nInitialization TIMEOUT!\n\nPossible causes:\n- Model file missing\n- Camera permission denied\n- MediaPipe error";
                Debug.LogError("[GazeDebugSetup] GazeTracker 초기화 타임아웃 (60초)");
                yield break;
            }
        }

        Debug.Log("[GazeDebugSetup] GazeTracker 초기화 완료 감지");

        // 웹캠 텍스처가 준비될 때까지 추가 대기
        while (GazeTracker.Instance.WebCamTexture == null || !GazeTracker.Instance.WebCamTexture.isPlaying)
        {
            waitTime += 0.1f;
            _debugText.text = $"Gaze Tracking Debug\n==================\nWaiting for camera... ({waitTime:F1}s)";
            yield return new WaitForSeconds(0.1f);

            if (waitTime > 10f)
            {
                _debugText.text = "Gaze Tracking Debug\n==================\nCamera timeout!\nCheck camera connection.";
                Debug.LogError("[GazeDebugSetup] 카메라 연결 타임아웃");
                yield break;
            }
        }

        Debug.Log($"[GazeDebugSetup] 웹캠 텍스처 준비됨: {GazeTracker.Instance.WebCamTexture.width}x{GazeTracker.Instance.WebCamTexture.height}");

        // 웹캠 텍스처 연결
        if (_cameraPreview != null)
        {
            var webCam = GazeTracker.Instance.WebCamTexture;
            _cameraPreview.texture = webCam;
            _cameraPreview.color = Color.white;

            // 카메라 회전 및 미러링 보정
            int rotationAngle = webCam.videoRotationAngle;
            bool isMirrored = webCam.videoVerticallyMirrored;

            Debug.Log($"[GazeDebugSetup] 카메라 회전: {rotationAngle}도, 수직 미러: {isMirrored}");

            // RectTransform 회전 적용
            _cameraPreview.rectTransform.localEulerAngles = new Vector3(0, 0, -rotationAngle);

            // 전면 카메라 미러링 (좌우 반전)
            if (webCam.deviceName != null)
            {
                foreach (var device in WebCamTexture.devices)
                {
                    if (device.name == webCam.deviceName && device.isFrontFacing)
                    {
                        // 전면 카메라는 거울처럼 보이도록 좌우 반전
                        _cameraPreview.rectTransform.localScale = new Vector3(-1, 1, 1);
                        Debug.Log("[GazeDebugSetup] 전면 카메라 미러링 적용");
                        break;
                    }
                }
            }

            // 수직 미러링 보정
            if (isMirrored)
            {
                var scale = _cameraPreview.rectTransform.localScale;
                _cameraPreview.rectTransform.localScale = new Vector3(scale.x, -scale.y, scale.z);
            }

            _cameraPreview.uvRect = new Rect(0, 0, 1, 1);
            _cameraConnected = true;

            Debug.Log("[GazeDebugSetup] 카메라 프리뷰 연결 완료");
        }
        else
        {
            Debug.LogError("[GazeDebugSetup] _cameraPreview가 null입니다");
        }

        // 이벤트 구독
        GazeTracker.Instance.OnGazeUpdated += OnGazeUpdated;
        GazeTracker.Instance.OnTrackingLost += OnTrackingLost;

        _debugText.text = "Gaze Tracking Debug\n==================\nReady! Looking for face...";
    }

    private void OnDestroy()
    {
        if (GazeTracker.Instance != null)
        {
            GazeTracker.Instance.OnGazeUpdated -= OnGazeUpdated;
            GazeTracker.Instance.OnTrackingLost -= OnTrackingLost;
        }
    }

    private void OnGazeUpdated(GazeData data)
    {
        if (_mainCanvas == null) return;

        var canvasRect = _mainCanvas.GetComponent<RectTransform>();
        float canvasWidth = canvasRect.rect.width;
        float canvasHeight = canvasRect.rect.height;

        // 시선 커서 업데이트
        _gazeCursor.gameObject.SetActive(true);
        _gazeCursorImage.color = Color.green;

        Vector2 screenPos = new Vector2(
            data.ScreenPosition.x * canvasWidth,
            (1f - data.ScreenPosition.y) * canvasHeight
        );

        // 스무딩
        _smoothedPosition = Vector2.Lerp(_smoothedPosition, screenPos, 0.15f);
        _gazeCursor.anchoredPosition = _smoothedPosition - new Vector2(canvasWidth, canvasHeight) / 2f;

        // 신뢰도에 따라 크기 조절
        float size = 20f + data.Confidence * 20f;
        _gazeCursor.sizeDelta = new Vector2(size, size);

        // 홍채 마커 업데이트
        _leftIrisMarker.gameObject.SetActive(true);
        _rightIrisMarker.gameObject.SetActive(true);

        Vector2 leftPos = new Vector2(
            data.LeftIrisCenter.x * canvasWidth,
            (1f - data.LeftIrisCenter.y) * canvasHeight
        );
        _leftIrisMarker.anchoredPosition = leftPos - new Vector2(canvasWidth, canvasHeight) / 2f;

        Vector2 rightPos = new Vector2(
            data.RightIrisCenter.x * canvasWidth,
            (1f - data.RightIrisCenter.y) * canvasHeight
        );
        _rightIrisMarker.anchoredPosition = rightPos - new Vector2(canvasWidth, canvasHeight) / 2f;

        // 디버그 텍스트 업데이트
        _debugText.text = $"Gaze Tracking Debug\n" +
                          $"==================\n" +
                          $"Screen: ({data.ScreenPosition.x:F3}, {data.ScreenPosition.y:F3})\n" +
                          $"Left Iris: ({data.LeftIrisCenter.x:F3}, {data.LeftIrisCenter.y:F3})\n" +
                          $"Right Iris: ({data.RightIrisCenter.x:F3}, {data.RightIrisCenter.y:F3})\n" +
                          $"Confidence: {data.Confidence:P0}\n" +
                          $"Tracking: YES\n" +
                          $"Calibrated: {GazeTracker.Instance?.Calibrator?.IsCalibrated ?? false}";
    }

    private void OnTrackingLost()
    {
        if (_gazeCursorImage != null)
        {
            _gazeCursorImage.color = Color.red;
        }

        if (_leftIrisMarker != null) _leftIrisMarker.gameObject.SetActive(false);
        if (_rightIrisMarker != null) _rightIrisMarker.gameObject.SetActive(false);

        if (_debugText != null)
        {
            _debugText.text = "Gaze Tracking Debug\n" +
                              "==================\n" +
                              "Status: TRACKING LOST\n" +
                              "Face not detected\n\n" +
                              "Please look at the camera";
        }
    }

    /// <summary>
    /// 카메라 프리뷰에 웹캠 텍스처 설정
    /// </summary>
    public void SetCameraTexture(Texture texture)
    {
        if (_cameraPreview != null)
        {
            _cameraPreview.texture = texture;
        }
    }
}
