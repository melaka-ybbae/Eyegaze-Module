using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// L2CS-Net 기반 시선 추적 디버그 UI 설정
/// 씬에 추가하면 자동으로 디버그 UI 생성
/// </summary>
public class L2CSDebugSetup : MonoBehaviour
{
    [Header("UI Settings")]
    [SerializeField] private bool _showCameraPreview = true;
    [SerializeField] private bool _showGazePoint = true;
    [SerializeField] private bool _showDebugInfo = true;
    [SerializeField] private bool _showFaceOverlay = true;
    [SerializeField] private float _gazePointSize = 30f;
    [SerializeField] private Color _gazePointColor = Color.green;

    [Header("Preview Settings")]
    [SerializeField] private float _previewWidth = 200f;
    [SerializeField] private float _previewHeight = 150f;

    // UI 요소
    private Canvas _canvas;
    private RawImage _cameraPreview;
    private RawImage _croppedFacePreview;  // 크롭된 얼굴 표시
    private Image _gazePoint;
    private Text _debugText;
    private RectTransform _canvasRect;

    // 시선 방향 화살표
    private RectTransform _gazeArrowContainer;
    private Image _gazeArrowLine;
    private Image _gazeArrowHead;

    // 얼굴 오버레이 요소
    private RectTransform _faceOverlayContainer;
    private Image _faceBoundingBox;
    private Image _leftEyeMarker;
    private Image _rightEyeMarker;
    private Image _noseMarker;
    private Image _leftMouthMarker;
    private Image _rightMouthMarker;

    // 오버레이 좌표 변환용 플래그
    private bool _flipOverlayX = false;
    private bool _flipOverlayY = false;

    // 참조
    private L2CSGazeTracker _gazeTracker;
    private FaceDetector _faceDetector;

    private void Start()
    {
        SetupUI();
        StartCoroutine(WaitForTracker());
    }

    private System.Collections.IEnumerator WaitForTracker()
    {
        while (L2CSGazeTracker.Instance == null || !L2CSGazeTracker.Instance.IsInitialized)
        {
            yield return new WaitForSeconds(0.1f);
        }

        _gazeTracker = L2CSGazeTracker.Instance;

        // 카메라 프리뷰 연결
        if (_showCameraPreview && _cameraPreview != null && _gazeTracker.WebCamTexture != null)
        {
            _cameraPreview.texture = _gazeTracker.WebCamTexture;
            ApplyCameraRotation();
        }

        // FaceDetector 찾기
        _faceDetector = FindObjectOfType<FaceDetector>();
        if (_faceDetector != null && _showFaceOverlay)
        {
            _faceDetector.OnFaceDetected += OnFaceDetected;
        }

        // 이벤트 구독
        _gazeTracker.OnGazeUpdated += OnGazeUpdated;
        _gazeTracker.OnTrackingLost += OnTrackingLost;
        _gazeTracker.OnTrackingStarted += OnTrackingStarted;

        Debug.Log("[L2CSDebugSetup] 초기화 완료");
    }

    private void Update()
    {
        // 크롭된 얼굴 이미지 업데이트
        if (_croppedFacePreview != null && _gazeTracker != null && _gazeTracker.DebugCroppedFace != null)
        {
            _croppedFacePreview.texture = _gazeTracker.DebugCroppedFace;
        }

        // 시선 화살표 업데이트
        UpdateGazeArrow();

        // S키로 크롭 이미지 저장
        if (Input.GetKeyDown(KeyCode.S) && _gazeTracker != null)
        {
            _gazeTracker.SaveNextCroppedFace();
        }

        // F키로 크롭 이미지 상하 반전 토글
        if (Input.GetKeyDown(KeyCode.F) && _gazeTracker != null)
        {
            _gazeTracker.ToggleFlipVertical();
        }
    }

    private void UpdateGazeArrow()
    {
        if (_gazeArrowContainer == null || _gazeTracker == null) return;

        // Offset 적용된 값 사용 (캘리브레이션 후 0 중심)
        float pitchDeg = _gazeTracker.RawModelPitch - _gazeTracker.PitchOffset;
        float yawDeg = _gazeTracker.RawModelYaw - _gazeTracker.YawOffset;

        // 2D 화살표로 시선 방향 표시
        // 화면 좌표계: X 양수 = 오른쪽, Y 양수 = 위쪽
        // 화살표가 시선 방향을 가리킴

        // 화살표 방향 계산
        // yaw 양수 = 오른쪽 보기 = 화살표 오른쪽
        // pitch 양수 = 아래 보기 = 화살표 아래 (일반 L2CS 규칙)
        float arrowX = yawDeg;
        float arrowY = -pitchDeg;  // pitch 양수면 아래쪽

        // 각도 계산 (atan2: x, y 순서 주의)
        float arrowAngle = Mathf.Atan2(arrowX, arrowY) * Mathf.Rad2Deg;

        // 길이 계산 (최소 15, 최대 45)
        float magnitude = Mathf.Sqrt(arrowX * arrowX + arrowY * arrowY);
        float arrowLength = Mathf.Clamp(15f + magnitude * 1.0f, 15f, 45f);

        _gazeArrowContainer.localEulerAngles = new Vector3(0, 0, -arrowAngle);

        if (_gazeArrowLine != null)
        {
            var lineRect = _gazeArrowLine.rectTransform;
            lineRect.sizeDelta = new Vector2(4f, arrowLength);

            // 캘리브레이션 전: 노란색, 후: 초록~빨강
            Color arrowColor;
            if (!_gazeTracker.HasCalibrated)
            {
                arrowColor = Color.yellow;
            }
            else
            {
                float t = Mathf.Clamp01(magnitude / 20f);
                arrowColor = Color.Lerp(Color.green, Color.red, t);
            }

            _gazeArrowLine.color = arrowColor;
            if (_gazeArrowHead != null)
                _gazeArrowHead.color = arrowColor;
        }
    }

    private void OnDestroy()
    {
        if (_gazeTracker != null)
        {
            _gazeTracker.OnGazeUpdated -= OnGazeUpdated;
            _gazeTracker.OnTrackingLost -= OnTrackingLost;
            _gazeTracker.OnTrackingStarted -= OnTrackingStarted;
        }

        if (_faceDetector != null)
        {
            _faceDetector.OnFaceDetected -= OnFaceDetected;
        }
    }

    private void SetupUI()
    {
        // Canvas 생성
        var canvasObj = new GameObject("L2CSDebugCanvas");
        _canvas = canvasObj.AddComponent<Canvas>();
        _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        _canvas.sortingOrder = 100;

        var scaler = canvasObj.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;  // 픽셀 기반으로 변경

        canvasObj.AddComponent<GraphicRaycaster>();
        _canvasRect = canvasObj.GetComponent<RectTransform>();

        // 카메라 프리뷰
        if (_showCameraPreview)
        {
            CreateCameraPreview();
            CreateCroppedFacePreview();
        }

        // 시선 포인트
        if (_showGazePoint)
        {
            CreateGazePoint();
        }

        // 디버그 텍스트
        if (_showDebugInfo)
        {
            CreateDebugText();
        }

        // 얼굴 오버레이
        if (_showFaceOverlay)
        {
            CreateFaceOverlay();
        }

        // 캘리브레이션 버튼
        CreateCalibrationButton();
    }

    private void CreateCameraPreview()
    {
        var previewObj = new GameObject("CameraPreview");
        previewObj.transform.SetParent(_canvas.transform, false);

        _cameraPreview = previewObj.AddComponent<RawImage>();
        _cameraPreview.color = Color.white;

        var rect = _cameraPreview.rectTransform;
        // 왼쪽 상단 기준, 중앙 pivot으로 설정
        rect.anchorMin = new Vector2(0, 1);
        rect.anchorMax = new Vector2(0, 1);
        rect.pivot = new Vector2(0.5f, 0.5f);  // 중앙 pivot (회전 시 위치 유지)
        // 프리뷰 크기의 절반 + 여백만큼 안쪽으로
        rect.anchoredPosition = new Vector2(_previewWidth / 2f + 20f, -_previewHeight / 2f - 60f);
        rect.sizeDelta = new Vector2(_previewWidth, _previewHeight);
    }

    private void CreateCroppedFacePreview()
    {
        var previewObj = new GameObject("CroppedFacePreview");
        previewObj.transform.SetParent(_canvas.transform, false);

        _croppedFacePreview = previewObj.AddComponent<RawImage>();
        _croppedFacePreview.color = Color.white;

        var rect = _croppedFacePreview.rectTransform;
        rect.anchorMin = new Vector2(0, 1);
        rect.anchorMax = new Vector2(0, 1);
        rect.pivot = new Vector2(0.5f, 0.5f);
        // 카메라 프리뷰 아래에 배치
        rect.anchoredPosition = new Vector2(70f, -_previewHeight - 130f);
        rect.sizeDelta = new Vector2(100f, 100f);

        // 라벨 추가
        var labelObj = new GameObject("Label");
        labelObj.transform.SetParent(previewObj.transform, false);
        var label = labelObj.AddComponent<Text>();
        label.text = "Crop (S:Save)";
        label.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        label.fontSize = 12;
        label.color = Color.white;
        label.alignment = TextAnchor.UpperCenter;
        var labelRect = label.rectTransform;
        labelRect.anchorMin = new Vector2(0.5f, 1);
        labelRect.anchorMax = new Vector2(0.5f, 1);
        labelRect.pivot = new Vector2(0.5f, 0);
        labelRect.anchoredPosition = new Vector2(0, 5);
        labelRect.sizeDelta = new Vector2(100, 20);

        // 시선 방향 화살표 생성
        CreateGazeArrow(previewObj.transform);
    }

    private void CreateGazeArrow(Transform parent)
    {
        // 화살표 컨테이너 (중앙에서 회전)
        var containerObj = new GameObject("GazeArrowContainer");
        containerObj.transform.SetParent(parent, false);
        _gazeArrowContainer = containerObj.AddComponent<RectTransform>();
        _gazeArrowContainer.anchorMin = new Vector2(0.5f, 0.5f);
        _gazeArrowContainer.anchorMax = new Vector2(0.5f, 0.5f);
        _gazeArrowContainer.pivot = new Vector2(0.5f, 0.5f);
        _gazeArrowContainer.anchoredPosition = Vector2.zero;
        _gazeArrowContainer.sizeDelta = new Vector2(100, 100);

        // 화살표 선 (중앙에서 시작)
        var lineObj = new GameObject("ArrowLine");
        lineObj.transform.SetParent(containerObj.transform, false);
        _gazeArrowLine = lineObj.AddComponent<Image>();
        _gazeArrowLine.color = Color.red;
        var lineRect = _gazeArrowLine.rectTransform;
        lineRect.anchorMin = new Vector2(0.5f, 0.5f);
        lineRect.anchorMax = new Vector2(0.5f, 0.5f);
        lineRect.pivot = new Vector2(0.5f, 0f);  // 아래쪽 중앙이 피벗 (위로 뻗어나감)
        lineRect.anchoredPosition = Vector2.zero;
        lineRect.sizeDelta = new Vector2(4f, 40f);  // 굵기 4, 길이 40

        // 화살표 머리 (삼각형 대신 작은 사각형)
        var headObj = new GameObject("ArrowHead");
        headObj.transform.SetParent(lineObj.transform, false);
        _gazeArrowHead = headObj.AddComponent<Image>();
        _gazeArrowHead.color = Color.red;
        var headRect = _gazeArrowHead.rectTransform;
        headRect.anchorMin = new Vector2(0.5f, 1f);
        headRect.anchorMax = new Vector2(0.5f, 1f);
        headRect.pivot = new Vector2(0.5f, 0f);
        headRect.anchoredPosition = Vector2.zero;
        headRect.sizeDelta = new Vector2(12f, 12f);
        // 45도 회전하여 다이아몬드 모양
        headRect.localEulerAngles = new Vector3(0, 0, 45);
    }

    private void CreateGazePoint()
    {
        var pointObj = new GameObject("GazePoint");
        pointObj.transform.SetParent(_canvas.transform, false);

        _gazePoint = pointObj.AddComponent<Image>();
        _gazePoint.color = _gazePointColor;

        var rect = _gazePoint.rectTransform;
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = new Vector2(_gazePointSize, _gazePointSize);
    }

    private void CreateDebugText()
    {
        // 배경
        var bgObj = new GameObject("DebugBackground");
        bgObj.transform.SetParent(_canvas.transform, false);

        var bg = bgObj.AddComponent<Image>();
        bg.color = new Color(0, 0, 0, 0.7f);

        var bgRect = bg.rectTransform;
        bgRect.anchorMin = new Vector2(1, 1);
        bgRect.anchorMax = new Vector2(1, 1);
        bgRect.pivot = new Vector2(1, 1);
        bgRect.anchoredPosition = new Vector2(-10, -10);
        bgRect.sizeDelta = new Vector2(300, 200);

        // 텍스트
        var textObj = new GameObject("DebugText");
        textObj.transform.SetParent(bgObj.transform, false);

        _debugText = textObj.AddComponent<Text>();
        _debugText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        _debugText.fontSize = 14;
        _debugText.color = Color.white;
        _debugText.alignment = TextAnchor.UpperLeft;

        var textRect = _debugText.rectTransform;
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = new Vector2(10, 10);
        textRect.offsetMax = new Vector2(-10, -10);
    }

    private void CreateCalibrationButton()
    {
        var buttonObj = new GameObject("CalibButton");
        buttonObj.transform.SetParent(_canvas.transform, false);

        var buttonImage = buttonObj.AddComponent<Image>();
        buttonImage.color = new Color(0.2f, 0.6f, 1f, 1f);

        var buttonRect = buttonImage.rectTransform;
        buttonRect.anchorMin = new Vector2(0, 0);
        buttonRect.anchorMax = new Vector2(0, 0);
        buttonRect.pivot = new Vector2(0, 0);
        buttonRect.anchoredPosition = new Vector2(10, 10);
        buttonRect.sizeDelta = new Vector2(150, 50);

        var button = buttonObj.AddComponent<Button>();
        button.targetGraphic = buttonImage;
        button.onClick.AddListener(OnCalibrationButtonClick);

        // 텍스트
        var textObj = new GameObject("Text");
        textObj.transform.SetParent(buttonObj.transform, false);

        var text = textObj.AddComponent<Text>();
        text.text = "Quick Calibrate";
        text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        text.fontSize = 16;
        text.color = Color.white;
        text.alignment = TextAnchor.MiddleCenter;

        var textRect = text.rectTransform;
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = Vector2.zero;
        textRect.offsetMax = Vector2.zero;
    }

    private void ApplyCameraRotation()
    {
        if (_cameraPreview == null || _gazeTracker?.WebCamTexture == null) return;

        var webCam = _gazeTracker.WebCamTexture;
        int rotationAngle = webCam.videoRotationAngle;
        bool isMirrored = webCam.videoVerticallyMirrored;

        _cameraPreview.rectTransform.localEulerAngles = new Vector3(0, 0, -rotationAngle);

        // 전면 카메라 미러링
        Vector3 scale = Vector3.one;
        bool isFrontFacing = false;
        foreach (var device in WebCamTexture.devices)
        {
            if (device.name == webCam.deviceName && device.isFrontFacing)
            {
                scale.x = -1;
                isFrontFacing = true;
                break;
            }
        }

        if (isMirrored)
        {
            scale.y = -1;
        }

        _cameraPreview.rectTransform.localScale = scale;

        // 오버레이 좌표 변환 플래그 설정
        // X축: 프리뷰가 X축 미러링되면 오버레이도 미러링 필요
        _flipOverlayX = (scale.x < 0);
        
        // Y축: 프리뷰가 Y축 반전되면 오버레이도 반전 필요
        // 기본적으로 false (반전 불필요), 프리뷰가 Y축 반전되면 true
        _flipOverlayY = (scale.y < 0);
    }

    private void OnGazeUpdated(L2CSGazeData data)
    {
        // 시선 포인트 업데이트
        if (_gazePoint != null)
        {
            // 화면 크기 기반 위치 계산
            Vector2 gazePos = new Vector2(
                (data.ScreenPosition.x - 0.5f) * Screen.width,
                (data.ScreenPosition.y - 0.5f) * Screen.height
            );

            _gazePoint.rectTransform.anchoredPosition = gazePos;
            _gazePoint.color = _gazePointColor;
        }

        // 디버그 텍스트 업데이트
        if (_debugText != null && _gazeTracker != null)
        {
            string calibStatus = _gazeTracker.HasCalibrated ? "OK" : "...";
            float correctedPitch = _gazeTracker.RawModelPitch - _gazeTracker.PitchOffset;
            float correctedYaw = _gazeTracker.RawModelYaw - _gazeTracker.YawOffset;

            string flipStatus = _gazeTracker.FlipCropVertical ? "ON" : "OFF";
            _debugText.text = $"=== L2CS Debug ===\n" +
                             $"Raw: P={_gazeTracker.RawModelPitch:F1}° Y={_gazeTracker.RawModelYaw:F1}°\n" +
                             $"Offset: P={_gazeTracker.PitchOffset:F1}° Y={_gazeTracker.YawOffset:F1}°\n" +
                             $"Corrected: P={correctedPitch:F1}° Y={correctedYaw:F1}°\n" +
                             $"Screen: ({data.ScreenPosition.x:F2}, {data.ScreenPosition.y:F2})\n" +
                             $"Calib: {calibStatus} Flip: {flipStatus}\n" +
                             $"[S]: Save [F]: Flip({flipStatus})";
        }
    }

    private void OnTrackingLost()
    {
        if (_gazePoint != null)
        {
            _gazePoint.color = new Color(_gazePointColor.r, _gazePointColor.g, _gazePointColor.b, 0.3f);
        }

        if (_debugText != null)
        {
            _debugText.text = "=== L2CS Gaze Debug ===\nTracking: LOST\n\n(Look at the camera)";
        }
    }

    private void OnTrackingStarted()
    {
        if (_gazePoint != null)
        {
            _gazePoint.color = _gazePointColor;
        }
    }

    private void OnCalibrationButtonClick()
    {
        if (_gazeTracker != null && _gazeTracker.IsTracking)
        {
            // 화면 중앙으로 빠른 캘리브레이션
            _gazeTracker.QuickCalibrate(new Vector2(0.5f, 0.5f));
            Debug.Log("[L2CSDebugSetup] 빠른 캘리브레이션 완료");
        }
    }

    private void CreateFaceOverlay()
    {
        // 얼굴 오버레이 컨테이너 (카메라 프리뷰 위에 표시)
        var containerObj = new GameObject("FaceOverlayContainer");
        containerObj.transform.SetParent(_cameraPreview.transform, false);
        _faceOverlayContainer = containerObj.AddComponent<RectTransform>();
        _faceOverlayContainer.anchorMin = Vector2.zero;
        _faceOverlayContainer.anchorMax = Vector2.one;
        _faceOverlayContainer.offsetMin = Vector2.zero;
        _faceOverlayContainer.offsetMax = Vector2.zero;

        // 바운딩 박스 (테두리만)
        _faceBoundingBox = CreateOverlayRect("BoundingBox", Color.green, 3f);

        // 랜드마크 마커들
        _leftEyeMarker = CreateOverlayMarker("LeftEye", Color.cyan, 8f);
        _rightEyeMarker = CreateOverlayMarker("RightEye", Color.cyan, 8f);
        _noseMarker = CreateOverlayMarker("Nose", Color.yellow, 8f);
        _leftMouthMarker = CreateOverlayMarker("LeftMouth", Color.magenta, 6f);
        _rightMouthMarker = CreateOverlayMarker("RightMouth", Color.magenta, 6f);

        // 초기에는 숨김
        SetFaceOverlayVisible(false);
    }

    private Image CreateOverlayRect(string name, Color color, float thickness)
    {
        var obj = new GameObject(name);
        obj.transform.SetParent(_faceOverlayContainer, false);

        var image = obj.AddComponent<Image>();
        image.color = color;

        // Outline 효과를 위해 Sliced 이미지 대신 빈 중앙의 사각형 사용
        // Unity 기본 UI에서는 Outline 컴포넌트 사용
        var outline = obj.AddComponent<Outline>();
        outline.effectColor = color;
        outline.effectDistance = new Vector2(thickness, thickness);

        // 내부를 투명하게
        image.color = new Color(color.r, color.g, color.b, 0.1f);

        var rect = image.rectTransform;
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);

        return image;
    }

    private Image CreateOverlayMarker(string name, Color color, float size)
    {
        var obj = new GameObject(name);
        obj.transform.SetParent(_faceOverlayContainer, false);

        var image = obj.AddComponent<Image>();
        image.color = color;

        var rect = image.rectTransform;
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = new Vector2(size, size);

        return image;
    }

    private void SetFaceOverlayVisible(bool visible)
    {
        if (_faceBoundingBox != null) _faceBoundingBox.gameObject.SetActive(visible);
        if (_leftEyeMarker != null) _leftEyeMarker.gameObject.SetActive(visible);
        if (_rightEyeMarker != null) _rightEyeMarker.gameObject.SetActive(visible);
        if (_noseMarker != null) _noseMarker.gameObject.SetActive(visible);
        if (_leftMouthMarker != null) _leftMouthMarker.gameObject.SetActive(visible);
        if (_rightMouthMarker != null) _rightMouthMarker.gameObject.SetActive(visible);
    }

    private void OnFaceDetected(FaceDetector.FaceDetectionResult result)
    {
        if (!_showFaceOverlay || _faceOverlayContainer == null || _cameraPreview == null)
            return;

        SetFaceOverlayVisible(true);

        // 프리뷰 크기
        float previewW = _previewWidth;
        float previewH = _previewHeight;

        // 바운딩 박스 업데이트
        if (_faceBoundingBox != null)
        {
            var box = result.BoundingBox;

            // 정규화 좌표 (0~1)
            float normX = box.x + box.width / 2f;
            float normY = box.y + box.height / 2f;

            // 프리뷰 좌표로 변환
            // X축: 프리뷰가 미러링되면 반전
            float boxX = (_flipOverlayX ? (1f - normX) : normX) - 0.5f;
            
            // Y축: BlazeFace Y=0은 상단, Unity UI Y=0.5 중앙 기준
            // 기본적으로 그대로 사용, 프리뷰가 Y반전되면 반전
            float boxY = (_flipOverlayY ? (1f - normY) : normY) - 0.5f;

            boxX *= previewW;
            boxY *= previewH;

            float boxW = box.width * previewW;
            float boxH = box.height * previewH;

            _faceBoundingBox.rectTransform.anchoredPosition = new Vector2(boxX, boxY);
            _faceBoundingBox.rectTransform.sizeDelta = new Vector2(boxW, boxH);
        }

        // 랜드마크 업데이트
        UpdateLandmarkMarker(_leftEyeMarker, result.LeftEye, previewW, previewH);
        UpdateLandmarkMarker(_rightEyeMarker, result.RightEye, previewW, previewH);
        UpdateLandmarkMarker(_noseMarker, result.Nose, previewW, previewH);
        UpdateLandmarkMarker(_leftMouthMarker, result.LeftMouth, previewW, previewH);
        UpdateLandmarkMarker(_rightMouthMarker, result.RightMouth, previewW, previewH);
    }

    private void UpdateLandmarkMarker(Image marker, Vector2 normalizedPos, float previewW, float previewH)
    {
        if (marker == null) return;

        // 프리뷰 좌표로 변환 (미러링 고려)
        float x = (_flipOverlayX ? (1f - normalizedPos.x) : normalizedPos.x) - 0.5f;
        // Y축: _flipOverlayY가 true면 반전, false면 그대로
        float y = (_flipOverlayY ? (1f - normalizedPos.y) : normalizedPos.y) - 0.5f;

        x *= previewW;
        y *= previewH;

        marker.rectTransform.anchoredPosition = new Vector2(x, y);
    }
}
