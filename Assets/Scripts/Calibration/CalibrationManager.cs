using System;
using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;

/// <summary>
/// 9점 캘리브레이션 씬 관리
/// 아동 친화적 UI로 캘리브레이션 진행
/// </summary>
public class CalibrationManager : MonoBehaviour
{
    [Header("UI References")]
    [SerializeField] private RectTransform _calibrationPoint;
    [SerializeField] private Image _pointImage;
    [SerializeField] private Text _instructionText;
    [SerializeField] private Canvas _canvas;

    [Header("Calibration Settings")]
    [SerializeField] private float _pointDisplayTime = 2f;
    [SerializeField] private float _sampleInterval = 0.1f;
    [SerializeField] private float _transitionTime = 0.5f;
    [SerializeField] private float _pointAnimationScale = 1.5f;
    [SerializeField] private string _nextSceneName = "StimulusTest";

    [Header("Point Positions (0~1 normalized)")]
    [SerializeField] private Vector2[] _calibrationPositions = new Vector2[]
    {
        new Vector2(0.1f, 0.9f),  // 좌상
        new Vector2(0.5f, 0.9f),  // 중상
        new Vector2(0.9f, 0.9f),  // 우상
        new Vector2(0.1f, 0.5f),  // 좌중
        new Vector2(0.5f, 0.5f),  // 중앙
        new Vector2(0.9f, 0.5f),  // 우중
        new Vector2(0.1f, 0.1f),  // 좌하
        new Vector2(0.5f, 0.1f),  // 중하
        new Vector2(0.9f, 0.1f),  // 우하
    };

    [Header("Child-Friendly Settings")]
    [SerializeField] private Color[] _pointColors = new Color[]
    {
        Color.red, Color.yellow, Color.green, Color.cyan,
        Color.blue, Color.magenta, new Color(1f, 0.5f, 0f), // orange
        new Color(0.5f, 0f, 1f), // purple
        Color.white
    };
    [SerializeField] private Sprite[] _pointSprites; // 별, 하트 등 캐릭터

    // 상태
    private int _currentPointIndex = 0;
    private bool _isCalibrating = false;
    private Coroutine _calibrationCoroutine;

    // 이벤트
    public event Action<int, int> OnPointChanged; // currentIndex, totalPoints
    public event Action<float> OnCalibrationComplete; // quality score
    public event Action OnCalibrationCancelled;

    private void Start()
    {
        if (_canvas == null)
            _canvas = GetComponentInParent<Canvas>();

        StartCalibration();
    }

    /// <summary>
    /// 캘리브레이션 시작
    /// </summary>
    public void StartCalibration()
    {
        if (_isCalibrating) return;

        _isCalibrating = true;
        _currentPointIndex = 0;

        if (GazeTracker.Instance != null)
        {
            GazeTracker.Instance.StartCalibration();
        }

        if (_instructionText != null)
        {
            _instructionText.text = "화면의 점을 바라봐 주세요";
        }

        _calibrationCoroutine = StartCoroutine(CalibrationSequence());
    }

    /// <summary>
    /// 캘리브레이션 취소
    /// </summary>
    public void CancelCalibration()
    {
        if (!_isCalibrating) return;

        if (_calibrationCoroutine != null)
        {
            StopCoroutine(_calibrationCoroutine);
            _calibrationCoroutine = null;
        }

        _isCalibrating = false;
        OnCalibrationCancelled?.Invoke();
    }

    /// <summary>
    /// 캘리브레이션 시퀀스
    /// </summary>
    private IEnumerator CalibrationSequence()
    {
        // 시작 전 대기
        yield return new WaitForSeconds(1f);

        for (int i = 0; i < _calibrationPositions.Length; i++)
        {
            _currentPointIndex = i;
            OnPointChanged?.Invoke(i, _calibrationPositions.Length);

            // 포인트 이동
            yield return MovePointTo(_calibrationPositions[i], i);

            // 포인트 표시 및 샘플 수집
            yield return DisplayAndCollect(_calibrationPositions[i]);
        }

        // 캘리브레이션 완료
        CompleteCalibration();
    }

    /// <summary>
    /// 포인트 이동 애니메이션
    /// </summary>
    private IEnumerator MovePointTo(Vector2 normalizedPosition, int index)
    {
        if (_calibrationPoint == null) yield break;

        Vector2 screenPos = NormalizedToScreenPosition(normalizedPosition);
        Vector2 startPos = _calibrationPoint.anchoredPosition;

        // 색상 및 스프라이트 변경
        if (_pointImage != null)
        {
            if (_pointColors != null && index < _pointColors.Length)
            {
                _pointImage.color = _pointColors[index];
            }

            if (_pointSprites != null && index < _pointSprites.Length && _pointSprites[index] != null)
            {
                _pointImage.sprite = _pointSprites[index];
            }
        }

        // 이동 애니메이션
        float elapsed = 0f;
        while (elapsed < _transitionTime)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.SmoothStep(0f, 1f, elapsed / _transitionTime);
            _calibrationPoint.anchoredPosition = Vector2.Lerp(startPos, screenPos, t);
            yield return null;
        }

        _calibrationPoint.anchoredPosition = screenPos;
    }

    /// <summary>
    /// 포인트 표시 및 샘플 수집
    /// </summary>
    private IEnumerator DisplayAndCollect(Vector2 normalizedPosition)
    {
        if (_calibrationPoint == null) yield break;

        // 주목 유도 애니메이션 (크기 변화)
        float elapsed = 0f;
        float nextSampleTime = 0f;
        Vector3 originalScale = _calibrationPoint.localScale;

        while (elapsed < _pointDisplayTime)
        {
            elapsed += Time.deltaTime;

            // 펄스 애니메이션
            float pulse = 1f + Mathf.Sin(elapsed * Mathf.PI * 2f) * 0.1f * _pointAnimationScale;
            _calibrationPoint.localScale = originalScale * pulse;

            // 샘플 수집
            if (elapsed >= nextSampleTime)
            {
                nextSampleTime += _sampleInterval;
                CollectSample(normalizedPosition);
            }

            yield return null;
        }

        _calibrationPoint.localScale = originalScale;
    }

    /// <summary>
    /// 샘플 수집
    /// </summary>
    private void CollectSample(Vector2 targetPosition)
    {
        if (GazeTracker.Instance != null)
        {
            GazeTracker.Instance.CollectCalibrationSample(targetPosition);
        }
    }

    /// <summary>
    /// 캘리브레이션 완료
    /// </summary>
    private void CompleteCalibration()
    {
        _isCalibrating = false;

        if (GazeTracker.Instance != null)
        {
            GazeTracker.Instance.FinishCalibration();
            float quality = GazeTracker.Instance.Calibrator.GetQualityScore();

            if (_instructionText != null)
            {
                _instructionText.text = $"캘리브레이션 완료!\n품질: {quality * 100:F0}%";
            }

            OnCalibrationComplete?.Invoke(quality);

            // 품질이 낮으면 재시도 권유
            if (quality < 0.5f)
            {
                Debug.LogWarning("[CalibrationManager] 캘리브레이션 품질이 낮습니다. 재시도를 권장합니다.");
            }
            else
            {
                // 다음 씬으로 전환
                StartCoroutine(TransitionToNextScene());
            }
        }
    }

    /// <summary>
    /// 다음 씬으로 전환
    /// </summary>
    private IEnumerator TransitionToNextScene()
    {
        yield return new WaitForSeconds(2f);

        if (!string.IsNullOrEmpty(_nextSceneName))
        {
            SceneManager.LoadScene(_nextSceneName);
        }
    }

    /// <summary>
    /// 정규화된 좌표를 화면 좌표로 변환
    /// </summary>
    private Vector2 NormalizedToScreenPosition(Vector2 normalized)
    {
        if (_canvas == null) return Vector2.zero;

        RectTransform canvasRect = _canvas.GetComponent<RectTransform>();
        return new Vector2(
            (normalized.x - 0.5f) * canvasRect.rect.width,
            (normalized.y - 0.5f) * canvasRect.rect.height
        );
    }

    /// <summary>
    /// 재캘리브레이션
    /// </summary>
    public void Recalibrate()
    {
        CancelCalibration();
        StartCalibration();
    }
}
