using System;
using System.Collections;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// L2CS-Net 기반 시선 추적 컨트롤러
/// FaceDetector + GazeEstimator + GazeToScreen + GazeFilter를 통합 관리
/// </summary>
public class L2CSGazeTracker : MonoBehaviour
{
    public static L2CSGazeTracker Instance { get; private set; }

    [Header("Components")]
    [SerializeField] private FaceDetector _faceDetector;
    [SerializeField] private GazeEstimator _gazeEstimator;
    [SerializeField] private GazeToScreen _gazeToScreen;

    [Header("Camera Settings")]
    [SerializeField] private int _cameraWidth = 640;
    [SerializeField] private int _cameraHeight = 480;
    [SerializeField] private int _cameraFps = 30;
    [SerializeField] private RawImage _cameraPreview;
    [SerializeField] private bool _forceHorizontalFlip = true;  // 에디터 웹캠용 강제 좌우 반전

    [Header("Processing Settings")]
    [SerializeField] private int _processEveryNFrames = 1;
    [SerializeField] private bool _useFilter = true;
    [SerializeField] private float _filterMinCutoff = 0.3f;  // 낮을수록 더 스무딩 (기존 1.0)
    [SerializeField] private float _filterBeta = 0.001f;     // 낮을수록 빠른 움직임도 스무딩 (기존 0.007)
    [SerializeField] private int _movingAverageWindow = 5;   // 이동 평균 윈도우 크기

    [Header("Tracking Stability")]
    [SerializeField] private int _lostFrameThreshold = 10;

    // 카메라
    private WebCamTexture _webCamTexture;
    private Texture2D _frameTexture;
    private Color32[] _pixelBuffer;
    private bool _isInitialized = false;

    // 필터
    private GazeFilter _gazeFilter;
    private MovingAverageFilter _movingAvgFilter;

    // 상태
    private bool _isTracking = false;
    private int _consecutiveLostFrames = 0;
    private int _frameSkipCounter = 0;
    private bool _isFrontFacing = false;

    // 현재 데이터
    private L2CSGazeData _currentGazeData;

    // Raw 모델 출력 (디버그용)
    private float _rawModelPitch;
    private float _rawModelYaw;

    // 동적 오프셋 (캘리브레이션 시 설정)
    private float _pitchOffset = 0f;
    private float _yawOffset = 0f;
    private bool _hasCalibrated = false;
    private int _calibrationFrameCount = 0;
    private float _calibrationPitchSum = 0f;
    private float _calibrationYawSum = 0f;
    private const int CALIBRATION_FRAMES = 30;

    // 디버그용 크롭 이미지
    private Texture2D _debugCroppedFace;
    private bool _saveNextFrame = false;
    private bool _flipCropVertical = false;  // 크롭 이미지 상하 반전 테스트

    // Properties for debug access
    public float RawModelPitch => _rawModelPitch;
    public float RawModelYaw => _rawModelYaw;
    public float PitchOffset => _pitchOffset;
    public float YawOffset => _yawOffset;
    public bool HasCalibrated => _hasCalibrated;
    public Texture2D DebugCroppedFace => _debugCroppedFace;
    public bool FlipCropVertical => _flipCropVertical;

    // 이벤트
    public event Action<L2CSGazeData> OnGazeUpdated;
    public event Action OnTrackingLost;
    public event Action OnTrackingStarted;

    public bool IsInitialized => _isInitialized;
    public bool IsTracking => _isTracking;
    public L2CSGazeData CurrentGazeData => _currentGazeData;
    public WebCamTexture WebCamTexture => _webCamTexture;
    public GazeToScreen GazeToScreenComponent => _gazeToScreen;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        DontDestroyOnLoad(gameObject);

        // 컴포넌트 확인 및 생성
        EnsureComponents();
    }

    private IEnumerator Start()
    {
        Application.targetFrameRate = _cameraFps;

        _gazeFilter = new GazeFilter(_filterMinCutoff, _filterBeta);
        _movingAvgFilter = new MovingAverageFilter(_movingAverageWindow);

        yield return StartCoroutine(Initialize());
    }

    private void Update()
    {
        if (!_isInitialized || _webCamTexture == null || !_webCamTexture.isPlaying)
            return;

        if (!_webCamTexture.didUpdateThisFrame)
            return;

        _frameSkipCounter++;
        if (_frameSkipCounter < _processEveryNFrames)
            return;
        _frameSkipCounter = 0;

        ProcessFrame();
    }

    private void OnDestroy()
    {
        Shutdown();
        if (Instance == this)
        {
            Instance = null;
        }
    }

    /// <summary>
    /// 컴포넌트 확인 및 생성
    /// </summary>
    private void EnsureComponents()
    {
        if (_faceDetector == null)
        {
            _faceDetector = GetComponent<FaceDetector>();
            if (_faceDetector == null)
            {
                _faceDetector = gameObject.AddComponent<FaceDetector>();
            }
        }

        if (_gazeEstimator == null)
        {
            _gazeEstimator = GetComponent<GazeEstimator>();
            if (_gazeEstimator == null)
            {
                _gazeEstimator = gameObject.AddComponent<GazeEstimator>();
            }
        }

        if (_gazeToScreen == null)
        {
            _gazeToScreen = GetComponent<GazeToScreen>();
            if (_gazeToScreen == null)
            {
                _gazeToScreen = gameObject.AddComponent<GazeToScreen>();
            }
        }
    }

    /// <summary>
    /// 초기화
    /// </summary>
    private IEnumerator Initialize()
    {
        Debug.Log("[L2CSGazeTracker] 초기화 시작...");

        // Android 권한 요청
#if UNITY_ANDROID && !UNITY_EDITOR
        if (!UnityEngine.Android.Permission.HasUserAuthorizedPermission(UnityEngine.Android.Permission.Camera))
        {
            UnityEngine.Android.Permission.RequestUserPermission(UnityEngine.Android.Permission.Camera);
            yield return new WaitForSeconds(1f);

            float waitTime = 0f;
            while (!UnityEngine.Android.Permission.HasUserAuthorizedPermission(UnityEngine.Android.Permission.Camera) && waitTime < 15f)
            {
                waitTime += 1f;
                yield return new WaitForSeconds(1f);
            }

            if (!UnityEngine.Android.Permission.HasUserAuthorizedPermission(UnityEngine.Android.Permission.Camera))
            {
                Debug.LogError("[L2CSGazeTracker] 카메라 권한이 거부되었습니다.");
                yield break;
            }
        }
#endif

        yield return new WaitForSeconds(0.5f);

        // 카메라 초기화
        if (WebCamTexture.devices.Length == 0)
        {
            Debug.LogError("[L2CSGazeTracker] 카메라를 찾을 수 없습니다.");
            yield break;
        }

        // 전면 카메라 선택
        WebCamDevice? selectedDevice = null;
        foreach (var cam in WebCamTexture.devices)
        {
            Debug.Log($"[L2CSGazeTracker] 카메라: {cam.name} (Front: {cam.isFrontFacing})");
            if (cam.isFrontFacing)
            {
                selectedDevice = cam;
                break;
            }
        }

        if (selectedDevice == null)
        {
            selectedDevice = WebCamTexture.devices[0];
        }

        var device = selectedDevice.Value;
        _isFrontFacing = device.isFrontFacing;
        Debug.Log($"[L2CSGazeTracker] 선택된 카메라: {device.name} (전면: {_isFrontFacing})");

        try
        {
            _webCamTexture = new WebCamTexture(device.name, _cameraWidth, _cameraHeight, _cameraFps);
            _webCamTexture.Play();
        }
        catch (Exception e)
        {
            Debug.LogError($"[L2CSGazeTracker] 카메라 시작 실패: {e.Message}");
            yield break;
        }

        // 카메라 준비 대기
        float timeout = 5f;
        float elapsed = 0f;
        while (_webCamTexture.width <= 16 && elapsed < timeout)
        {
            elapsed += Time.deltaTime;
            yield return null;
        }

        if (_webCamTexture.width <= 16)
        {
            Debug.LogError("[L2CSGazeTracker] 카메라 초기화 타임아웃");
            _webCamTexture.Stop();
            yield break;
        }

        Debug.Log($"[L2CSGazeTracker] 카메라 해상도: {_webCamTexture.width}x{_webCamTexture.height}");

        // 프리뷰 설정
        if (_cameraPreview != null)
        {
            _cameraPreview.texture = _webCamTexture;
        }

        // 텍스처 준비
        _frameTexture = new Texture2D(_webCamTexture.width, _webCamTexture.height, TextureFormat.RGBA32, false);
        _pixelBuffer = new Color32[_webCamTexture.width * _webCamTexture.height];

        // 컴포넌트 초기화 확인
        if (_faceDetector != null && !_faceDetector.IsInitialized)
        {
            Debug.LogWarning("[L2CSGazeTracker] FaceDetector 모델이 로드되지 않았습니다. Inspector에서 ONNX 모델을 할당하세요.");
        }

        if (_gazeEstimator != null && !_gazeEstimator.IsInitialized)
        {
            Debug.LogWarning("[L2CSGazeTracker] GazeEstimator 모델이 로드되지 않았습니다. Inspector에서 ONNX 모델을 할당하세요.");
        }

        _isInitialized = true;
        Debug.Log("[L2CSGazeTracker] 초기화 완료");
    }

    /// <summary>
    /// 프레임 처리
    /// </summary>
    private void ProcessFrame()
    {
        // 웹캠에서 픽셀 가져오기
        _webCamTexture.GetPixels32(_pixelBuffer);

        // 좌우 반전 (전면 카메라 또는 강제 설정 시)
        if (_isFrontFacing || _forceHorizontalFlip)
        {
            FlipHorizontal(_pixelBuffer, _webCamTexture.width, _webCamTexture.height);
        }

        _frameTexture.SetPixels32(_pixelBuffer);
        _frameTexture.Apply();

        // 1. 얼굴 검출
        FaceDetector.FaceDetectionResult faceResult;
        if (_faceDetector != null && _faceDetector.IsInitialized)
        {
            faceResult = _faceDetector.Detect(_frameTexture);
        }
        else
        {
            faceResult = new FaceDetector.FaceDetectionResult
            {
                IsValid = true,
                BoundingBox = new Rect(0.2f, 0.2f, 0.6f, 0.6f),
                Confidence = 1f
            };
        }

        if (!faceResult.IsValid)
        {
            HandleTrackingLost();
            return;
        }

        // 2. 얼굴 크롭
        Texture2D croppedFace = null;
        if (_faceDetector != null && _faceDetector.IsInitialized)
        {
            croppedFace = _faceDetector.CropFace(_frameTexture, faceResult, 448);
        }
        else
        {
            croppedFace = _frameTexture;
        }

        if (croppedFace == null)
        {
            HandleTrackingLost();
            return;
        }

        // 디버그용 크롭 이미지 저장 (UI 표시용)
        if (_debugCroppedFace == null || _debugCroppedFace.width != croppedFace.width)
        {
            if (_debugCroppedFace != null) Destroy(_debugCroppedFace);
            _debugCroppedFace = new Texture2D(croppedFace.width, croppedFace.height, TextureFormat.RGBA32, false);
        }
        Graphics.CopyTexture(croppedFace, _debugCroppedFace);

        // 파일로 저장 (S키 누르면)
        if (_saveNextFrame)
        {
            _saveNextFrame = false;
            SaveCroppedFace(croppedFace);
        }

        // 3. 시선 추정
        GazeEstimator.GazeEstimationResult gazeResult;
        if (_gazeEstimator != null && _gazeEstimator.IsInitialized)
        {
            gazeResult = _gazeEstimator.Estimate(croppedFace);
        }
        else
        {
            gazeResult = new GazeEstimator.GazeEstimationResult
            {
                IsValid = true,
                Pitch = 0f,
                Yaw = 0f,
                PitchDeg = 0f,
                YawDeg = 0f,
                GazeVector = new Vector3(0, 0, -1)
            };
        }

        // 크롭된 텍스처 정리
        if (croppedFace != _frameTexture)
        {
            Destroy(croppedFace);
        }

        if (!gazeResult.IsValid)
        {
            HandleTrackingLost();
            return;
        }

        // Raw 모델 출력 저장 (디버그용)
        _rawModelPitch = gazeResult.PitchDeg;
        _rawModelYaw = gazeResult.YawDeg;

        // 자동 캘리브레이션 (처음 30프레임 평균을 정면 기준으로 설정)
        if (!_hasCalibrated)
        {
            _calibrationPitchSum += _rawModelPitch;
            _calibrationYawSum += _rawModelYaw;
            _calibrationFrameCount++;

            if (_calibrationFrameCount >= CALIBRATION_FRAMES)
            {
                _pitchOffset = _calibrationPitchSum / CALIBRATION_FRAMES;
                _yawOffset = _calibrationYawSum / CALIBRATION_FRAMES;
                _hasCalibrated = true;
                Debug.Log($"[L2CSGazeTracker] 자동 캘리브레이션 완료 - Offset: Pitch={_pitchOffset:F1}, Yaw={_yawOffset:F1}");
            }
        }

        // 4. 시선 각도 계산 (단순 선형 변환)
        float pitchDeg = _rawModelPitch - _pitchOffset;
        float yawDeg = _rawModelYaw - _yawOffset;

        // 라디안 변환
        float pitch = pitchDeg * Mathf.Deg2Rad;
        float yaw = yawDeg * Mathf.Deg2Rad;

        // 필터 적용 (선택적)
        if (_useFilter && _gazeFilter != null)
        {
            // 1단계: One Euro Filter (적응형 필터)
            Vector2 filtered = _gazeFilter.FilterAngles(pitch, yaw, _cameraFps);
            pitch = filtered.x;
            yaw = filtered.y;

            // 2단계: 이동 평균 필터 (추가 스무딩)
            if (_movingAvgFilter != null)
            {
                Vector2 avgFiltered = _movingAvgFilter.Filter(new Vector2(pitch, yaw));
                pitch = avgFiltered.x;
                yaw = avgFiltered.y;
            }

            pitchDeg = pitch * Mathf.Rad2Deg;
            yawDeg = yaw * Mathf.Rad2Deg;
        }

        // 5. 캘리브레이션 중이면 샘플 추가
        if (_gazeToScreen != null && _gazeToScreen.IsCalibrating)
        {
            _gazeToScreen.AddCalibrationSample(pitch, yaw);
        }

        // 6. 화면 좌표 변환 (PolynomialMapper 사용)
        Vector2 screenPosition;
        if (_gazeToScreen != null)
        {
            // GazeToScreen에서 픽셀 좌표 → 정규화 좌표로 변환
            screenPosition = _gazeToScreen.GazeToNormalizedScreenPoint(pitch, yaw);
        }
        else
        {
            // Fallback: 기본 선형 매핑
            float sensitivity = 0.05f;
            screenPosition = new Vector2(
                Mathf.Clamp01(0.5f + yawDeg * sensitivity),
                Mathf.Clamp01(0.5f + pitchDeg * sensitivity)
            );
        }

        // 7. 데이터 업데이트
        _currentGazeData = new L2CSGazeData
        {
            Timestamp = Time.time,
            ScreenPosition = screenPosition,
            Pitch = pitch,
            Yaw = yaw,
            PitchDeg = pitchDeg,
            YawDeg = yawDeg,
            GazeVector = gazeResult.GazeVector,
            FaceConfidence = faceResult.Confidence,
            IsValid = true
        };

        // 추적 성공
        ResetLostCounter();

        if (!_isTracking)
        {
            _isTracking = true;
            OnTrackingStarted?.Invoke();
        }

        OnGazeUpdated?.Invoke(_currentGazeData);
    }

    /// <summary>
    /// 픽셀 상하 반전
    /// </summary>
    private void FlipVertical(Color32[] pixels, int width, int height)
    {
        for (int y = 0; y < height / 2; y++)
        {
            int topRow = y * width;
            int bottomRow = (height - 1 - y) * width;

            for (int x = 0; x < width; x++)
            {
                var temp = pixels[topRow + x];
                pixels[topRow + x] = pixels[bottomRow + x];
                pixels[bottomRow + x] = temp;
            }
        }
    }

    /// <summary>
    /// 텍스처를 상하 반전하여 새 텍스처 반환
    /// </summary>
    private Texture2D FlipTextureVertical(Texture2D source)
    {
        int width = source.width;
        int height = source.height;
        var flipped = new Texture2D(width, height, source.format, false);
        
        for (int y = 0; y < height; y++)
        {
            var row = source.GetPixels(0, y, width, 1);
            flipped.SetPixels(0, height - 1 - y, width, 1, row);
        }
        flipped.Apply();
        return flipped;
    }

    /// <summary>
    /// 픽셀 좌우 반전 (전면 카메라 미러링 제거용)
    /// </summary>
    private void FlipHorizontal(Color32[] pixels, int width, int height)
    {
        for (int y = 0; y < height; y++)
        {
            int row = y * width;
            for (int x = 0; x < width / 2; x++)
            {
                int left = row + x;
                int right = row + (width - 1 - x);
                var temp = pixels[left];
                pixels[left] = pixels[right];
                pixels[right] = temp;
            }
        }
    }

    /// <summary>
    /// 추적 실패 처리
    /// </summary>
    private void HandleTrackingLost()
    {
        _consecutiveLostFrames++;

        if (_consecutiveLostFrames >= _lostFrameThreshold && _isTracking)
        {
            _isTracking = false;
            _currentGazeData = L2CSGazeData.Invalid;
            OnTrackingLost?.Invoke();
        }
    }

    /// <summary>
    /// 실패 카운터 리셋
    /// </summary>
    private void ResetLostCounter()
    {
        _consecutiveLostFrames = 0;
    }

    /// <summary>
    /// 종료
    /// </summary>
    private void Shutdown()
    {
        if (_webCamTexture != null)
        {
            _webCamTexture.Stop();
            Destroy(_webCamTexture);
            _webCamTexture = null;
        }

        if (_frameTexture != null)
        {
            Destroy(_frameTexture);
            _frameTexture = null;
        }

        _isInitialized = false;
    }

    /// <summary>
    /// 캘리브레이션 시작
    /// </summary>
    public void StartCalibration()
    {
        _gazeToScreen?.StartCalibration();
        _gazeFilter?.Reset();
        Debug.Log("[L2CSGazeTracker] 캘리브레이션 시작");
    }

    /// <summary>
    /// 캘리브레이션 취소
    /// </summary>
    public void CancelCalibration()
    {
        _gazeToScreen?.CancelCalibration();
        Debug.Log("[L2CSGazeTracker] 캘리브레이션 취소");
    }

    /// <summary>
    /// 현재 캘리브레이션 타겟 위치 (정규화 0~1)
    /// </summary>
    public Vector2 GetCurrentCalibrationTarget()
    {
        return _gazeToScreen?.GetCurrentCalibrationTarget() ?? new Vector2(0.5f, 0.5f);
    }

    /// <summary>
    /// 현재 캘리브레이션 진행률 (0~1)
    /// </summary>
    public float GetCalibrationProgress()
    {
        return _gazeToScreen?.GetCurrentPointProgress() ?? 0f;
    }

    /// <summary>
    /// 캘리브레이션 중인지 확인
    /// </summary>
    public bool IsCalibrationInProgress => _gazeToScreen?.IsCalibrating ?? false;

    /// <summary>
    /// 캘리브레이션 저장
    /// </summary>
    public void SaveCalibration(string filePath = null)
    {
        _gazeToScreen?.SaveCalibration(filePath);
    }

    /// <summary>
    /// 캘리브레이션 로드
    /// </summary>
    public bool LoadCalibration(string filePath = null)
    {
        return _gazeToScreen?.LoadCalibration(filePath) ?? false;
    }

    /// <summary>
    /// 캘리브레이션 초기화
    /// </summary>
    public void ResetCalibration()
    {
        _gazeToScreen?.ResetCalibration();

        // 자동 캘리브레이션 리셋
        _hasCalibrated = false;
        _calibrationFrameCount = 0;
        _calibrationPitchSum = 0f;
        _calibrationYawSum = 0f;
        _pitchOffset = 0f;
        _yawOffset = 0f;

        Debug.Log("[L2CSGazeTracker] 자동 캘리브레이션 리셋");
    }

    /// <summary>
    /// 필터 리셋
    /// </summary>
    public void ResetFilter()
    {
        _gazeFilter?.Reset();
    }

    /// <summary>
    /// 다음 프레임의 크롭 이미지를 파일로 저장
    /// </summary>
    public void SaveNextCroppedFace()
    {
        _saveNextFrame = true;
        Debug.Log("[L2CSGazeTracker] 다음 프레임 크롭 이미지 저장 예약됨");
    }

    /// <summary>
    /// 크롭 이미지 상하 반전 토글 (디버그용)
    /// </summary>
    public void ToggleFlipVertical()
    {
        _flipCropVertical = !_flipCropVertical;
        // 반전 변경 시 캘리브레이션 리셋
        ResetCalibration();
        Debug.Log($"[L2CSGazeTracker] 크롭 이미지 상하 반전: {_flipCropVertical} (캘리브레이션 리셋됨)");
    }

    /// <summary>
    /// 크롭 이미지를 파일로 저장
    /// </summary>
    private void SaveCroppedFace(Texture2D texture)
    {
        try
        {
            byte[] bytes = texture.EncodeToPNG();
            string filename = $"cropped_face_{System.DateTime.Now:yyyyMMdd_HHmmss}.png";

            #if UNITY_EDITOR
            string path = System.IO.Path.Combine(Application.dataPath, "..", filename);
            #else
            string path = System.IO.Path.Combine(Application.persistentDataPath, filename);
            #endif

            System.IO.File.WriteAllBytes(path, bytes);
            Debug.Log($"[L2CSGazeTracker] 크롭 이미지 저장됨: {path}");
        }
        catch (System.Exception e)
        {
            Debug.LogError($"[L2CSGazeTracker] 이미지 저장 실패: {e.Message}");
        }
    }
}

/// <summary>
/// L2CS-Net 시선 데이터
/// </summary>
[Serializable]
public struct L2CSGazeData
{
    public float Timestamp;
    public Vector2 ScreenPosition;
    public float Pitch;
    public float Yaw;
    public float PitchDeg;
    public float YawDeg;
    public Vector3 GazeVector;
    public float FaceConfidence;
    public bool IsValid;

    public static L2CSGazeData Invalid => new L2CSGazeData
    {
        Timestamp = Time.time,
        ScreenPosition = Vector2.zero,
        Pitch = 0f,
        Yaw = 0f,
        GazeVector = Vector3.zero,
        FaceConfidence = 0f,
        IsValid = false
    };
}
