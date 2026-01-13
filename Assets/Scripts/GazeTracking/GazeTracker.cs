using System;
using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using Mediapipe.Tasks.Vision.FaceLandmarker;
using Mediapipe.Tasks.Vision.Core;

public class GazeTracker : MonoBehaviour
{
    public static GazeTracker Instance { get; private set; }

    [Header("Camera Settings")]
    [SerializeField] private int _cameraWidth = 1280;
    [SerializeField] private int _cameraHeight = 720;
    [SerializeField] private int _cameraFps = 30;
    [SerializeField] private RawImage _cameraPreview; // 선택적 카메라 미리보기

    [Header("MediaPipe Settings")]
    [SerializeField] private string _modelPath = "face_landmarker.task";
    [SerializeField] private int _numFaces = 1;
    [SerializeField] private float _minDetectionConfidence = 0.3f;
    [SerializeField] private float _minTrackingConfidence = 0.3f;

    [Header("Tracking Stability")]
    [SerializeField] private int _lostFrameThreshold = 10; // 연속 N프레임 실패해야 Lost
    private int _consecutiveLostFrames = 0;

    [Header("Iris Landmark Indices")]
    private static readonly int[] LeftIrisIndices = { 468, 469, 470, 471, 472 };
    private static readonly int[] RightIrisIndices = { 473, 474, 475, 476, 477 };

    // 눈 영역 랜드마크 (시선 방향 계산용)
    private static readonly int LeftEyeInner = 133;
    private static readonly int LeftEyeOuter = 33;
    private static readonly int RightEyeInner = 362;
    private static readonly int RightEyeOuter = 263;

    // MediaPipe
    private WebCamTexture _webCamTexture;
    private FaceLandmarker _faceLandmarker;
    private Texture2D _inputTexture;
    private Color32[] _pixelBuffer;
    private bool _isInitialized = false;

    // 이벤트
    public event Action<GazeData> OnGazeUpdated;
    public event Action OnTrackingLost;
    public event Action OnTrackingStarted;

    // 상태
    private bool _isTracking = false;
    public bool IsTracking => _isTracking;
    public bool IsInitialized => _isInitialized;

    private GazeData _currentGazeData;
    public GazeData CurrentGazeData => _currentGazeData;

    // 캘리브레이션
    private GazeCalibrator _calibrator;
    public GazeCalibrator Calibrator => _calibrator;

    // 웹캠 텍스처 (디버그용)
    public WebCamTexture WebCamTexture => _webCamTexture;

    // 전면 카메라 여부 (좌표 반전용)
    private bool _isFrontFacing = false;
    public bool IsFrontFacing => _isFrontFacing;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        DontDestroyOnLoad(gameObject);

        _calibrator = new GazeCalibrator();
        _gazeFilter = new OneEuroFilterVector2(_filterMinCutoff, _filterBeta, _filterDCutoff);
    }

    private IEnumerator Start()
    {
        Application.targetFrameRate = _cameraFps;
        yield return StartCoroutine(InitializeMediaPipe());
    }

    private int _frameSkipCounter = 0;
    [SerializeField] private int _processEveryNFrames = 1; // 1 = 모든 프레임 처리, 2 = 2프레임마다 처리

    private void Update()
    {
        if (!_isInitialized || _webCamTexture == null || !_webCamTexture.isPlaying)
            return;

        // 웹캠이 실제로 새 프레임을 받았을 때만 처리
        if (!_webCamTexture.didUpdateThisFrame)
            return;

        // 프레임 스킵 (성능 최적화)
        _frameSkipCounter++;
        if (_frameSkipCounter < _processEveryNFrames)
            return;
        _frameSkipCounter = 0;

        ProcessFrame();
    }

    private void OnDestroy()
    {
        ShutdownMediaPipe();
        if (Instance == this)
        {
            Instance = null;
        }
    }

    /// <summary>
    /// MediaPipe 초기화
    /// </summary>
    private IEnumerator InitializeMediaPipe()
    {
        Debug.Log("[GazeTracker] MediaPipe 초기화 시작...");

        // Android에서 카메라 권한 요청
        #if UNITY_ANDROID && !UNITY_EDITOR
        Debug.Log("[GazeTracker] Android 플랫폼 감지 - 권한 확인 중...");

        if (!UnityEngine.Android.Permission.HasUserAuthorizedPermission(UnityEngine.Android.Permission.Camera))
        {
            Debug.Log("[GazeTracker] 카메라 권한 요청...");
            UnityEngine.Android.Permission.RequestUserPermission(UnityEngine.Android.Permission.Camera);
            yield return new WaitForSeconds(1f);

            // 권한 확인 대기
            float permissionWait = 0f;
            while (!UnityEngine.Android.Permission.HasUserAuthorizedPermission(UnityEngine.Android.Permission.Camera) && permissionWait < 15f)
            {
                Debug.Log($"[GazeTracker] 권한 대기 중... ({permissionWait}s)");
                permissionWait += 1f;
                yield return new WaitForSeconds(1f);
            }

            if (!UnityEngine.Android.Permission.HasUserAuthorizedPermission(UnityEngine.Android.Permission.Camera))
            {
                Debug.LogError("[GazeTracker] 카메라 권한이 거부되었습니다.");
                yield break;
            }
            Debug.Log("[GazeTracker] 카메라 권한 획득!");
        }
        else
        {
            Debug.Log("[GazeTracker] 카메라 권한 이미 있음");
        }
        #endif

        // 잠시 대기 (Android에서 권한 후 카메라 접근 가능해질 때까지)
        yield return new WaitForSeconds(0.5f);

        // 웹캠 장치 확인
        Debug.Log("[GazeTracker] 카메라 장치 검색 중...");
        if (WebCamTexture.devices.Length == 0)
        {
            Debug.LogError("[GazeTracker] 카메라를 찾을 수 없습니다.");
            yield break;
        }

        // 사용 가능한 카메라 목록 출력
        Debug.Log($"[GazeTracker] 사용 가능한 카메라 수: {WebCamTexture.devices.Length}");
        foreach (var cam in WebCamTexture.devices)
        {
            Debug.Log($"  - {cam.name} (Front: {cam.isFrontFacing})");
        }

        // 전면 카메라 우선 선택 (모바일용)
        WebCamDevice? selectedDevice = null;
        foreach (var cam in WebCamTexture.devices)
        {
            if (cam.isFrontFacing)
            {
                selectedDevice = cam;
                break;
            }
        }
        
        // 전면 카메라가 없으면 첫 번째 카메라 사용
        if (selectedDevice == null)
        {
            selectedDevice = WebCamTexture.devices[0];
        }
        
        var device = selectedDevice.Value;
        _isFrontFacing = device.isFrontFacing;
        Debug.Log($"[GazeTracker] 선택된 카메라: {device.name} (Front: {_isFrontFacing})");

        // 웹캠 시작 (Inspector에서 설정한 해상도 사용)
        try
        {
            _webCamTexture = new WebCamTexture(device.name, _cameraWidth, _cameraHeight, _cameraFps);
            _webCamTexture.Play();
        }
        catch (System.Exception e)
        {
            Debug.LogError($"[GazeTracker] 카메라 시작 실패: {e.Message}");
            yield break;
        }

        // 카메라 준비 대기 (최대 5초)
        float timeout = 5f;
        float elapsed = 0f;
        while (_webCamTexture.width <= 16 && elapsed < timeout)
        {
            elapsed += Time.deltaTime;
            yield return null;
        }

        if (_webCamTexture.width <= 16)
        {
            Debug.LogError("[GazeTracker] 카메라 초기화 타임아웃");
            _webCamTexture.Stop();
            yield break;
        }

        Debug.Log($"[GazeTracker] 카메라 해상도: {_webCamTexture.width}x{_webCamTexture.height}");

        // 카메라 미리보기 설정
        if (_cameraPreview != null)
        {
            _cameraPreview.texture = _webCamTexture;
        }

        // 입력 텍스처 준비
        _inputTexture = new Texture2D(_webCamTexture.width, _webCamTexture.height, TextureFormat.RGBA32, false);
        _pixelBuffer = new Color32[_webCamTexture.width * _webCamTexture.height];

        // FaceLandmarker 모델 로드
        yield return StartCoroutine(LoadFaceLandmarker());

        if (_faceLandmarker != null)
        {
            _isInitialized = true;
            Debug.Log("[GazeTracker] MediaPipe 초기화 완료");
        }
        else
        {
            Debug.LogError("[GazeTracker] FaceLandmarker 로드 실패로 초기화 중단");
        }
    }

    /// <summary>
    /// FaceLandmarker 모델 로드
    /// </summary>
    private IEnumerator LoadFaceLandmarker()
    {
        Debug.Log("[GazeTracker] FaceLandmarker 모델 로드 시작...");
        string modelFullPath;

        #if UNITY_ANDROID && !UNITY_EDITOR
        // Android: StreamingAssets에서 persistentDataPath로 복사 필요
        string sourcePath = System.IO.Path.Combine(Application.streamingAssetsPath, _modelPath);
        modelFullPath = System.IO.Path.Combine(Application.persistentDataPath, _modelPath);

        Debug.Log($"[GazeTracker] Android 모델 경로:");
        Debug.Log($"[GazeTracker]   소스: {sourcePath}");
        Debug.Log($"[GazeTracker]   대상: {modelFullPath}");
        Debug.Log($"[GazeTracker]   persistentDataPath: {Application.persistentDataPath}");

        // 이미 복사된 파일이 없으면 복사
        if (!System.IO.File.Exists(modelFullPath))
        {
            Debug.Log("[GazeTracker] 모델 파일 복사 시작...");

            var request = UnityEngine.Networking.UnityWebRequest.Get(sourcePath);
            yield return request.SendWebRequest();

            Debug.Log($"[GazeTracker] UnityWebRequest 결과: {request.result}");

            if (request.result != UnityEngine.Networking.UnityWebRequest.Result.Success)
            {
                Debug.LogError($"[GazeTracker] 모델 다운로드 실패: {request.error}");
                Debug.LogError($"[GazeTracker] HTTP 응답 코드: {request.responseCode}");
                Debug.LogError($"[GazeTracker] 경로: {sourcePath}");
                request.Dispose();
                yield break;
            }

            Debug.Log($"[GazeTracker] 다운로드 완료, 크기: {request.downloadHandler.data.Length} bytes");

            try
            {
                // 디렉토리 생성 확인
                string dir = System.IO.Path.GetDirectoryName(modelFullPath);
                if (!System.IO.Directory.Exists(dir))
                {
                    System.IO.Directory.CreateDirectory(dir);
                    Debug.Log($"[GazeTracker] 디렉토리 생성: {dir}");
                }

                System.IO.File.WriteAllBytes(modelFullPath, request.downloadHandler.data);
                Debug.Log($"[GazeTracker] 모델 저장 완료: {modelFullPath}");

                // 저장 확인
                if (System.IO.File.Exists(modelFullPath))
                {
                    var fileInfo = new System.IO.FileInfo(modelFullPath);
                    Debug.Log($"[GazeTracker] 저장된 파일 크기: {fileInfo.Length} bytes");
                }
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[GazeTracker] 모델 저장 실패: {e.Message}");
                Debug.LogError($"[GazeTracker] 스택: {e.StackTrace}");
                request.Dispose();
                yield break;
            }

            request.Dispose();
        }
        else
        {
            var fileInfo = new System.IO.FileInfo(modelFullPath);
            Debug.Log($"[GazeTracker] 캐시된 모델 사용 (크기: {fileInfo.Length} bytes)");
        }
        #else
        // Editor / Standalone: 직접 경로 사용
        modelFullPath = System.IO.Path.Combine(Application.streamingAssetsPath, _modelPath);
        Debug.Log($"[GazeTracker] 모델 로드 중: {modelFullPath}");

        if (!System.IO.File.Exists(modelFullPath))
        {
            Debug.LogError($"[GazeTracker] 모델 파일을 찾을 수 없습니다: {modelFullPath}");
            Debug.LogError("[GazeTracker] Assets/StreamingAssets/ 폴더에 face_landmarker.task 파일을 배치해주세요.");
            yield break;
        }
        #endif

        Debug.Log("[GazeTracker] FaceLandmarker 인스턴스 생성 중...");

        try
        {
            Debug.Log("[GazeTracker] BaseOptions 생성...");
            var baseOptions = new Mediapipe.Tasks.Core.BaseOptions(
                Mediapipe.Tasks.Core.BaseOptions.Delegate.CPU,
                modelAssetPath: modelFullPath
            );

            Debug.Log("[GazeTracker] FaceLandmarkerOptions 생성...");
            var options = new FaceLandmarkerOptions(
                baseOptions,
                runningMode: RunningMode.VIDEO,
                numFaces: _numFaces,
                minFaceDetectionConfidence: _minDetectionConfidence,
                minFacePresenceConfidence: _minDetectionConfidence,
                minTrackingConfidence: _minTrackingConfidence,
                outputFaceBlendshapes: false,
                outputFaceTransformationMatrixes: false
            );

            Debug.Log("[GazeTracker] FaceLandmarker.CreateFromOptions 호출...");
            _faceLandmarker = FaceLandmarker.CreateFromOptions(options);
            Debug.Log("[GazeTracker] FaceLandmarker 생성 완료!");
        }
        catch (System.Exception e)
        {
            Debug.LogError($"[GazeTracker] FaceLandmarker 생성 실패: {e.Message}");
            Debug.LogError($"[GazeTracker] 예외 타입: {e.GetType().Name}");
            Debug.LogError($"[GazeTracker] 스택: {e.StackTrace}");

            if (e.InnerException != null)
            {
                Debug.LogError($"[GazeTracker] 내부 예외: {e.InnerException.Message}");
            }
        }

        yield return null;
    }

    [Header("Image Processing")]
    [SerializeField] private bool _flipHorizontal = false; // 거울 모드 (테스트용으로 기본 false)
    [SerializeField] private bool _flipVertical = true;   // 웹캠 이미지가 뒤집혀 있는 경우

    [Header("Gaze Sensitivity (No Calibration)")]
    [SerializeField] private float _eyeRatioCenterX = 0.5f;  // 눈 중심 위치 (정면 볼 때)
    [SerializeField] private float _eyeRatioCenterY = 0.5f;  // 눈 중심 위치 (정면 볼 때)
    [SerializeField] private float _eyeRatioRangeX = 0.25f;  // X축 움직임 범위 (±)
    [SerializeField] private float _eyeRatioRangeY = 0.15f;  // Y축 움직임 범위 (±)

    [Header("One Euro Filter (Jitter Reduction)")]
    [SerializeField] private bool _useOneEuroFilter = true;
    [SerializeField] private float _filterMinCutoff = 1.0f;  // 낮을수록 더 부드러움
    [SerializeField] private float _filterBeta = 0.007f;     // 높을수록 빠른 움직임에 반응
    [SerializeField] private float _filterDCutoff = 1.0f;

    private OneEuroFilterVector2 _gazeFilter;

    /// <summary>
    /// 프레임 처리
    /// </summary>
    private void ProcessFrame()
    {
        if (_faceLandmarker == null) return;

        // 웹캠 텍스처에서 픽셀 가져오기
        _webCamTexture.GetPixels32(_pixelBuffer);

        // 이미지 뒤집기 처리 (Unity 텍스처 좌표계 → MediaPipe 좌표계)
        if (_flipVertical || _flipHorizontal)
        {
            FlipPixels(_pixelBuffer, _webCamTexture.width, _webCamTexture.height, _flipHorizontal, _flipVertical);
        }

        _inputTexture.SetPixels32(_pixelBuffer);
        _inputTexture.Apply();

        // MediaPipe Image 생성
        using var image = new Mediapipe.Image(
            Mediapipe.ImageFormat.Types.Format.Srgba,
            _inputTexture.width,
            _inputTexture.height,
            _inputTexture.width * 4,
            _inputTexture.GetRawTextureData<byte>()
        );

        // 타임스탬프 (밀리초) - 반드시 단조 증가해야 함
        long timestampMs = (long)(Time.unscaledTime * 1000);

        // 얼굴 랜드마크 감지
        var result = FaceLandmarkerResult.Alloc(_numFaces);
        
        try
        {
            bool detected = _faceLandmarker.TryDetectForVideo(image, timestampMs, null, ref result);

            if (detected)
            {
                // detected가 true라도 실제 얼굴이 있는지 확인
                ProcessFaceLandmarks(result);
            }
            else
            {
                // TryDetectForVideo가 false 반환 - 얼굴 없음으로 처리
                _consecutiveLostFrames++;
                
                if (_consecutiveLostFrames >= _lostFrameThreshold)
                {
                    HandleTrackingLost();
                }
            }
        }
        catch (System.Exception e)
        {
            // 예외 발생 시 로깅
            if (_consecutiveLostFrames % 60 == 0)
            {
                Debug.LogError($"[GazeTracker] TryDetectForVideo 예외: {e.Message}");
            }
            _consecutiveLostFrames++;
            HandleTrackingLost();
        }
    }

    /// <summary>
    /// 픽셀 배열을 수평/수직으로 뒤집습니다.
    /// </summary>
    private void FlipPixels(Color32[] pixels, int width, int height, bool flipHorizontal, bool flipVertical)
    {
        if (flipVertical)
        {
            // 수직 뒤집기 (상하 반전)
            for (int y = 0; y < height / 2; y++)
            {
                int topRowStart = y * width;
                int bottomRowStart = (height - 1 - y) * width;
                
                for (int x = 0; x < width; x++)
                {
                    Color32 temp = pixels[topRowStart + x];
                    pixels[topRowStart + x] = pixels[bottomRowStart + x];
                    pixels[bottomRowStart + x] = temp;
                }
            }
        }

        if (flipHorizontal)
        {
            // 수평 뒤집기 (좌우 반전)
            for (int y = 0; y < height; y++)
            {
                int rowStart = y * width;
                
                for (int x = 0; x < width / 2; x++)
                {
                    int leftIndex = rowStart + x;
                    int rightIndex = rowStart + (width - 1 - x);
                    
                    Color32 temp = pixels[leftIndex];
                    pixels[leftIndex] = pixels[rightIndex];
                    pixels[rightIndex] = temp;
                }
            }
        }
    }

    /// <summary>
    /// 얼굴 랜드마크 처리
    /// </summary>
    private void ProcessFaceLandmarks(FaceLandmarkerResult result)
    {
        if (result.faceLandmarks == null || result.faceLandmarks.Count == 0)
        {
            if (_consecutiveLostFrames % 30 == 0)
            {
                Debug.LogWarning($"[GazeTracker] 얼굴 감지 실패: faceLandmarks 없음 (연속 {_consecutiveLostFrames}회)");
            }
            HandleTrackingLost();
            return;
        }

        var landmarks = result.faceLandmarks[0].landmarks;
        if (landmarks == null || landmarks.Count < 478)
        {
            if (_consecutiveLostFrames % 30 == 0)
            {
                Debug.LogWarning($"[GazeTracker] 랜드마크 부족: {landmarks?.Count ?? 0}/478 (연속 {_consecutiveLostFrames}회)");
            }
            HandleTrackingLost();
            return;
        }

        // NormalizedLandmark를 Vector3 배열로 변환
        Vector3[] landmarkArray = new Vector3[landmarks.Count];
        for (int i = 0; i < landmarks.Count; i++)
        {
            var lm = landmarks[i];
            landmarkArray[i] = new Vector3(lm.x, lm.y, lm.z);
        }

        ProcessLandmarks(landmarkArray);
    }

    /// <summary>
    /// 랜드마크에서 시선 데이터 추출
    /// </summary>
    /// <param name="landmarks">478개의 얼굴 랜드마크</param>
    public void ProcessLandmarks(Vector3[] landmarks)
    {
        if (landmarks == null || landmarks.Length < 478)
        {
            HandleTrackingLost();
            return;
        }

        // 홍채 중심 계산 (원본 좌표로 계산)
        Vector2 leftIrisCenter = CalculateIrisCenter(landmarks, LeftIrisIndices);
        Vector2 rightIrisCenter = CalculateIrisCenter(landmarks, RightIrisIndices);

        // 눈 영역 내 상대 위치 계산 (원본 랜드마크 기준으로 계산)
        Vector2 leftEyeRatio = CalculateEyeRatio(landmarks, leftIrisCenter, LeftEyeInner, LeftEyeOuter);
        Vector2 rightEyeRatio = CalculateEyeRatio(landmarks, rightIrisCenter, RightEyeInner, RightEyeOuter);

        // 전면 카메라일 경우 최종 결과만 X 반전 (거울 모드)
        if (_isFrontFacing)
        {
            // EyeRatio X 반전
            leftEyeRatio.x = 1f - leftEyeRatio.x;
            rightEyeRatio.x = 1f - rightEyeRatio.x;

            // 홍채 중심도 반전 (디버그 표시용)
            leftIrisCenter.x = 1f - leftIrisCenter.x;
            rightIrisCenter.x = 1f - rightIrisCenter.x;

            // 전면 카메라에서는 왼쪽/오른쪽이 반대로 보이므로 스왑
            (leftIrisCenter, rightIrisCenter) = (rightIrisCenter, leftIrisCenter);
            (leftEyeRatio, rightEyeRatio) = (rightEyeRatio, leftEyeRatio);
        }

        // 양안 평균
        Vector2 averageRatio = (leftEyeRatio + rightEyeRatio) / 2f;

        // One Euro Filter 적용 (떨림 제거)
        Vector2 filteredRatio = averageRatio;
        if (_useOneEuroFilter && _gazeFilter != null)
        {
            filteredRatio = _gazeFilter.Filter(averageRatio, Time.time);
        }

        // 캘리브레이션 적용 또는 기본 매핑
        Vector2 screenPosition;
        if (_calibrator.IsCalibrated)
        {
            screenPosition = _calibrator.MapToScreen(filteredRatio);
        }
        else
        {
            // 캘리브레이션 없이 기본 매핑 사용
            screenPosition = MapEyeRatioToScreen(filteredRatio);
        }

        // 신뢰도 계산 (양안 차이가 적을수록 높음)
        float confidence = 1f - Mathf.Clamp01(Vector2.Distance(leftEyeRatio, rightEyeRatio) * 2f);

        _currentGazeData = new GazeData(
            Time.time,
            screenPosition,
            leftIrisCenter,
            rightIrisCenter,
            filteredRatio,  // 필터링된 EyeRatio 사용
            true,
            confidence
        );

        // 추적 성공 시 실패 카운터 리셋
        ResetLostCounter();

        if (!_isTracking)
        {
            _isTracking = true;
            OnTrackingStarted?.Invoke();
        }

        OnGazeUpdated?.Invoke(_currentGazeData);
    }

    /// <summary>
    /// 홍채 중심 좌표 계산
    /// </summary>
    private Vector2 CalculateIrisCenter(Vector3[] landmarks, int[] irisIndices)
    {
        Vector2 center = Vector2.zero;
        foreach (int idx in irisIndices)
        {
            center += new Vector2(landmarks[idx].x, landmarks[idx].y);
        }
        return center / irisIndices.Length;
    }

    /// <summary>
    /// 눈 영역 내 홍채의 상대 위치 계산 (0~1)
    /// </summary>
    private Vector2 CalculateEyeRatio(Vector3[] landmarks, Vector2 irisCenter, int innerIdx, int outerIdx)
    {
        Vector2 inner = new Vector2(landmarks[innerIdx].x, landmarks[innerIdx].y);
        Vector2 outer = new Vector2(landmarks[outerIdx].x, landmarks[outerIdx].y);

        float eyeWidth = Vector2.Distance(inner, outer);
        if (eyeWidth < 0.001f) return new Vector2(0.5f, 0.5f);

        // 홍채가 눈의 어느 위치에 있는지 계산 (0~1 범위, 0.5가 중앙)
        float t = Vector2.Dot(irisCenter - outer, inner - outer) / (eyeWidth * eyeWidth);

        // 수직 방향도 계산 (위아래 움직임)
        Vector2 eyeDirection = (inner - outer).normalized;
        Vector2 perpendicular = new Vector2(-eyeDirection.y, eyeDirection.x);
        float verticalOffset = Vector2.Dot(irisCenter - outer, perpendicular) / eyeWidth;

        // 원본 비율 반환 (확장은 나중에 적용)
        return new Vector2(t, 0.5f + verticalOffset);
    }

    /// <summary>
    /// 캘리브레이션 없이 EyeRatio를 화면 좌표로 변환
    /// </summary>
    private Vector2 MapEyeRatioToScreen(Vector2 eyeRatio)
    {
        // 중심점 기준으로 범위 내 값을 0~1로 매핑
        float screenX = (eyeRatio.x - _eyeRatioCenterX + _eyeRatioRangeX) / (_eyeRatioRangeX * 2f);
        float screenY = (eyeRatio.y - _eyeRatioCenterY + _eyeRatioRangeY) / (_eyeRatioRangeY * 2f);

        return new Vector2(
            Mathf.Clamp01(screenX),
            Mathf.Clamp01(screenY)
        );
    }

    /// <summary>
    /// 추적 실패 처리 (연속 N프레임 실패해야 실제 Lost로 처리)
    /// </summary>
    private void HandleTrackingLost()
    {
        _consecutiveLostFrames++;

        // 연속 실패 횟수가 임계값을 넘어야 실제로 Lost 처리
        if (_consecutiveLostFrames >= _lostFrameThreshold && _isTracking)
        {
            _isTracking = false;
            _currentGazeData = GazeData.Invalid;
            OnTrackingLost?.Invoke();
        }
    }

    /// <summary>
    /// 추적 성공 시 실패 카운터 리셋
    /// </summary>
    private void ResetLostCounter()
    {
        _consecutiveLostFrames = 0;
    }

    /// <summary>
    /// MediaPipe 종료
    /// </summary>
    private void ShutdownMediaPipe()
    {
        Debug.Log("[GazeTracker] MediaPipe 종료");

        if (_webCamTexture != null)
        {
            _webCamTexture.Stop();
            Destroy(_webCamTexture);
            _webCamTexture = null;
        }

        if (_inputTexture != null)
        {
            Destroy(_inputTexture);
            _inputTexture = null;
        }

        _faceLandmarker?.Close();
        _faceLandmarker = null;

        _isInitialized = false;
    }

    /// <summary>
    /// 캘리브레이션 시작
    /// </summary>
    public void StartCalibration()
    {
        _calibrator.Reset();
        _gazeFilter?.Reset();  // 필터도 리셋
        Debug.Log("[GazeTracker] 캘리브레이션 시작");
    }

    /// <summary>
    /// One Euro Filter 리셋 (추적 재시작 시 사용)
    /// </summary>
    public void ResetFilter()
    {
        _gazeFilter?.Reset();
    }

    /// <summary>
    /// 캘리브레이션 포인트 수집
    /// </summary>
    public void CollectCalibrationSample(Vector2 targetPosition)
    {
        if (_currentGazeData.IsValid)
        {
            // EyeRatio를 사용 (눈 영역 내 상대 위치)
            _calibrator.AddSample(targetPosition, _currentGazeData.EyeRatio);
        }
    }

    /// <summary>
    /// 캘리브레이션 완료
    /// </summary>
    public void FinishCalibration()
    {
        _calibrator.ComputeCalibration();
        Debug.Log($"[GazeTracker] 캘리브레이션 완료 - 유효: {_calibrator.IsCalibrated}");
    }
}
