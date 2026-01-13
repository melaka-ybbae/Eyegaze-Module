using System.Collections.Generic;
using System.IO;
using UnityEngine;

/// <summary>
/// 시선 각도 (pitch, yaw)를 화면 좌표로 변환
/// Python calibration 모듈과 동일한 2차 다항식 회귀 사용
/// </summary>
public class GazeToScreen : MonoBehaviour
{
    [Header("Screen Settings")]
    [SerializeField] private int _screenWidth = 1920;
    [SerializeField] private int _screenHeight = 1080;

    [Header("Calibration Settings")]
    [SerializeField] private int _samplesPerPoint = 30;
    [SerializeField] private float _sampleDelay = 0.8f;  // 포인트당 수집 시작 전 대기시간
    [SerializeField] private string _calibrationFilePath = "calibration_data.json";

    [Header("Smoothing")]
    [SerializeField] private float _smoothingFactor = 0.3f;
    private Vector2 _lastScreenPosition;

    // PolynomialMapper (Python과 동일한 2차 다항식 회귀)
    private PolynomialMapper _mapper;

    // 캘리브레이션 상태
    private bool _isCalibrating = false;
    private bool _isCalibrated = false;
    private int _calibrationPointIndex = 0;
    private Vector2[] _calibrationTargets;  // 정규화된 화면 좌표 (0~1)
    private List<Vector2> _collectedGazeData;  // pitch, yaw 라디안
    private List<Vector2> _collectedScreenData;  // 픽셀 좌표
    private List<Vector2> _currentPointSamples;  // 현재 포인트의 샘플들
    private float _calibrationStartTime;
    private int _currentSampleCount;

    public bool IsCalibrated => _isCalibrated;
    public bool IsCalibrating => _isCalibrating;
    public int CurrentCalibrationPointIndex => _calibrationPointIndex;
    public int TotalCalibrationPoints => _calibrationTargets?.Length ?? 9;

    private void Awake()
    {
        _mapper = new PolynomialMapper();
        _lastScreenPosition = new Vector2(_screenWidth / 2f, _screenHeight / 2f);

        // 저장된 캘리브레이션 자동 로드
        string fullPath = Path.Combine(Application.persistentDataPath, _calibrationFilePath);
        if (_mapper.Load(fullPath))
        {
            _isCalibrated = true;
            Debug.Log($"[GazeToScreen] 캘리브레이션 자동 로드 성공: {fullPath}");
        }
    }

    /// <summary>
    /// 시선 각도를 화면 좌표로 변환 (픽셀)
    /// </summary>
    /// <param name="pitch">Pitch 각도 (라디안)</param>
    /// <param name="yaw">Yaw 각도 (라디안)</param>
    /// <returns>화면 좌표 (픽셀)</returns>
    public Vector2 GazeToScreenPoint(float pitch, float yaw)
    {
        Vector2 screenPoint;

        if (_isCalibrated && _mapper.IsCalibrated)
        {
            // 캘리브레이션된 다항식 매핑 사용
            screenPoint = _mapper.Predict(pitch, yaw);
        }
        else
        {
            // 캘리브레이션 전: 기본 선형 매핑 (대략적)
            // pitch: 위(-) / 아래(+) → Y 좌표
            // yaw: 왼쪽(-) / 오른쪽(+) → X 좌표
            float normalizedX = 0.5f - yaw / Mathf.PI;  // 대략 -0.5π ~ 0.5π 범위
            float normalizedY = 0.5f + pitch / (Mathf.PI / 2f);  // 대략 -0.25π ~ 0.25π 범위

            screenPoint = new Vector2(
                normalizedX * _screenWidth,
                normalizedY * _screenHeight
            );
        }

        // 스무딩 적용
        screenPoint = Vector2.Lerp(_lastScreenPosition, screenPoint, _smoothingFactor);
        _lastScreenPosition = screenPoint;

        // 범위 제한
        screenPoint.x = Mathf.Clamp(screenPoint.x, 0, _screenWidth - 1);
        screenPoint.y = Mathf.Clamp(screenPoint.y, 0, _screenHeight - 1);

        return screenPoint;
    }

    /// <summary>
    /// 정규화된 화면 좌표 반환 (0~1)
    /// </summary>
    public Vector2 GazeToNormalizedScreenPoint(float pitch, float yaw)
    {
        Vector2 pixelPoint = GazeToScreenPoint(pitch, yaw);
        return new Vector2(
            pixelPoint.x / _screenWidth,
            pixelPoint.y / _screenHeight
        );
    }

    #region Calibration

    /// <summary>
    /// 9점 캘리브레이션 시작
    /// </summary>
    public void StartCalibration()
    {
        // 9점 그리드 (15% 마진)
        float margin = 0.15f;
        float left = margin;
        float center = 0.5f;
        float right = 1f - margin;
        float top = margin;
        float middle = 0.5f;
        float bottom = 1f - margin;

        _calibrationTargets = new Vector2[]
        {
            new Vector2(left, top),      // 좌상단
            new Vector2(center, top),    // 중상단
            new Vector2(right, top),     // 우상단
            new Vector2(left, middle),   // 좌중단
            new Vector2(center, middle), // 중앙
            new Vector2(right, middle),  // 우중단
            new Vector2(left, bottom),   // 좌하단
            new Vector2(center, bottom), // 중하단
            new Vector2(right, bottom)   // 우하단
        };

        _collectedGazeData = new List<Vector2>();
        _collectedScreenData = new List<Vector2>();
        _currentPointSamples = new List<Vector2>();
        _calibrationPointIndex = 0;
        _currentSampleCount = 0;
        _calibrationStartTime = Time.time;
        _isCalibrating = true;
        _isCalibrated = false;

        Debug.Log("[GazeToScreen] 9점 캘리브레이션 시작");
    }

    /// <summary>
    /// 현재 캘리브레이션 타겟 위치 (정규화 0~1)
    /// </summary>
    public Vector2 GetCurrentCalibrationTarget()
    {
        if (_calibrationTargets == null || _calibrationPointIndex >= _calibrationTargets.Length)
        {
            return new Vector2(0.5f, 0.5f);
        }
        return _calibrationTargets[_calibrationPointIndex];
    }

    /// <summary>
    /// 현재 캘리브레이션 타겟 위치 (픽셀)
    /// </summary>
    public Vector2 GetCurrentCalibrationTargetPixels()
    {
        Vector2 normalized = GetCurrentCalibrationTarget();
        return new Vector2(
            normalized.x * _screenWidth,
            normalized.y * _screenHeight
        );
    }

    /// <summary>
    /// 캘리브레이션 중 시선 샘플 추가
    /// 매 프레임 호출해야 함
    /// </summary>
    /// <param name="pitch">현재 pitch (라디안)</param>
    /// <param name="yaw">현재 yaw (라디안)</param>
    /// <returns>true if calibration complete</returns>
    public bool AddCalibrationSample(float pitch, float yaw)
    {
        if (!_isCalibrating || _calibrationTargets == null)
        {
            return false;
        }

        // 대기 시간 확인
        float elapsed = Time.time - _calibrationStartTime;
        if (elapsed < _sampleDelay)
        {
            return false;  // 아직 대기 중
        }

        // 샘플 수집
        _currentPointSamples.Add(new Vector2(pitch, yaw));
        _currentSampleCount++;

        // 샘플 수 충족 시 다음 포인트로
        if (_currentSampleCount >= _samplesPerPoint)
        {
            // 현재 포인트의 평균 gaze 계산
            Vector2 avgGaze = Vector2.zero;
            foreach (var sample in _currentPointSamples)
            {
                avgGaze += sample;
            }
            avgGaze /= _currentPointSamples.Count;

            // 타겟 화면 좌표 (픽셀)
            Vector2 targetPixel = GetCurrentCalibrationTargetPixels();

            _collectedGazeData.Add(avgGaze);
            _collectedScreenData.Add(targetPixel);

            Debug.Log($"[GazeToScreen] 캘리브레이션 포인트 {_calibrationPointIndex + 1}/9 완료: " +
                     $"gaze=({Mathf.Rad2Deg * avgGaze.x:F2}°, {Mathf.Rad2Deg * avgGaze.y:F2}°), " +
                     $"target=({targetPixel.x:F0}, {targetPixel.y:F0})");

            // 다음 포인트로 이동
            _calibrationPointIndex++;
            _currentPointSamples.Clear();
            _currentSampleCount = 0;
            _calibrationStartTime = Time.time;

            // 모든 포인트 완료?
            if (_calibrationPointIndex >= _calibrationTargets.Length)
            {
                FinishCalibration();
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// 캘리브레이션 완료 - 다항식 피팅
    /// </summary>
    private void FinishCalibration()
    {
        _isCalibrating = false;

        if (_collectedGazeData.Count < 6)
        {
            Debug.LogError("[GazeToScreen] 캘리브레이션 실패: 최소 6개 포인트 필요");
            return;
        }

        // PolynomialMapper로 피팅
        _mapper.Fit(_collectedGazeData, _collectedScreenData);
        _isCalibrated = _mapper.IsCalibrated;

        if (_isCalibrated)
        {
            Debug.Log("[GazeToScreen] 캘리브레이션 완료 - 다항식 회귀 피팅 성공");

            // 자동 저장
            string fullPath = Path.Combine(Application.persistentDataPath, _calibrationFilePath);
            _mapper.Save(fullPath);
        }
        else
        {
            Debug.LogError("[GazeToScreen] 캘리브레이션 실패 - 다항식 피팅 실패");
        }
    }

    /// <summary>
    /// 캘리브레이션 취소
    /// </summary>
    public void CancelCalibration()
    {
        _isCalibrating = false;
        _calibrationPointIndex = 0;
        _collectedGazeData?.Clear();
        _collectedScreenData?.Clear();
        _currentPointSamples?.Clear();

        Debug.Log("[GazeToScreen] 캘리브레이션 취소됨");
    }

    /// <summary>
    /// 캘리브레이션 초기화
    /// </summary>
    public void ResetCalibration()
    {
        _isCalibrating = false;
        _isCalibrated = false;
        _calibrationPointIndex = 0;
        _collectedGazeData?.Clear();
        _collectedScreenData?.Clear();
        _currentPointSamples?.Clear();
        _mapper.Reset();

        Debug.Log("[GazeToScreen] 캘리브레이션 초기화");
    }

    /// <summary>
    /// 캘리브레이션 저장
    /// </summary>
    public void SaveCalibration(string filePath = null)
    {
        if (!_isCalibrated)
        {
            Debug.LogWarning("[GazeToScreen] 저장할 캘리브레이션이 없습니다.");
            return;
        }

        string fullPath = filePath ?? Path.Combine(Application.persistentDataPath, _calibrationFilePath);
        _mapper.Save(fullPath);
    }

    /// <summary>
    /// 캘리브레이션 로드
    /// </summary>
    public bool LoadCalibration(string filePath = null)
    {
        string fullPath = filePath ?? Path.Combine(Application.persistentDataPath, _calibrationFilePath);
        if (_mapper.Load(fullPath))
        {
            _isCalibrated = true;
            return true;
        }
        return false;
    }

    /// <summary>
    /// 현재 수집 진행률 (0~1)
    /// </summary>
    public float GetCurrentPointProgress()
    {
        if (!_isCalibrating) return 0f;

        float elapsed = Time.time - _calibrationStartTime;
        if (elapsed < _sampleDelay)
        {
            return 0f;  // 대기 중
        }

        return (float)_currentSampleCount / _samplesPerPoint;
    }

    #endregion

    /// <summary>
    /// 화면 크기 설정
    /// </summary>
    public void SetScreenSize(int width, int height)
    {
        _screenWidth = width;
        _screenHeight = height;
        _lastScreenPosition = new Vector2(width / 2f, height / 2f);
    }

    /// <summary>
    /// 스무딩 팩터 설정
    /// </summary>
    public void SetSmoothingFactor(float factor)
    {
        _smoothingFactor = Mathf.Clamp01(factor);
    }
}
