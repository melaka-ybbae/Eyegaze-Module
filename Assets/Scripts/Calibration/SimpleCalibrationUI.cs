using System.Collections;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 9포인트 자동 캘리브레이션 UI
/// Python과 동일한 PolynomialMapper 기반 캘리브레이션 사용
/// </summary>
public class SimpleCalibrationUI : MonoBehaviour
{
    [Header("Settings")]
    [SerializeField] private float _pointSize = 50f;
    [SerializeField] private Color _pointColor = Color.red;
    [SerializeField] private Color _activeColor = Color.yellow;

    [Header("Instructions")]
    [SerializeField] private bool _showInstructions = true;

    private Canvas _canvas;
    private RectTransform _canvasRect;
    private Image _calibrationPoint;
    private Image _progressIndicator;
    private Text _instructionText;
    private Text _progressText;
    private bool _isRunning = false;

    private Button _startButton;
    private Button _resetButton;
    private Button _cancelButton;

    private void Start()
    {
        SetupUI();
    }

    private void Update()
    {
        // 캘리브레이션 중이면 UI 업데이트
        if (_isRunning && L2CSGazeTracker.Instance != null)
        {
            UpdateCalibrationUI();
        }
    }

    private void SetupUI()
    {
        // EventSystem 확인 및 생성
        if (FindObjectOfType<UnityEngine.EventSystems.EventSystem>() == null)
        {
            var eventSystemObj = new GameObject("EventSystem");
            eventSystemObj.AddComponent<UnityEngine.EventSystems.EventSystem>();
            eventSystemObj.AddComponent<UnityEngine.EventSystems.StandaloneInputModule>();
        }

        // Canvas 찾기 또는 생성
        _canvas = GetComponentInParent<Canvas>();
        if (_canvas == null)
        {
            var canvasObj = new GameObject("CalibrationCanvas");
            _canvas = canvasObj.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = 200;
            canvasObj.AddComponent<CanvasScaler>();
            canvasObj.AddComponent<GraphicRaycaster>();
            transform.SetParent(canvasObj.transform);
        }
        _canvasRect = _canvas.GetComponent<RectTransform>();

        // 캘리브레이션 포인트 생성
        var pointObj = new GameObject("CalibrationPoint");
        pointObj.transform.SetParent(_canvas.transform, false);
        _calibrationPoint = pointObj.AddComponent<Image>();
        _calibrationPoint.color = _pointColor;
        _calibrationPoint.rectTransform.sizeDelta = new Vector2(_pointSize, _pointSize);
        _calibrationPoint.gameObject.SetActive(false);

        // 진행률 표시 (원형)
        var progressObj = new GameObject("ProgressIndicator");
        progressObj.transform.SetParent(pointObj.transform, false);
        _progressIndicator = progressObj.AddComponent<Image>();
        _progressIndicator.color = _activeColor;
        _progressIndicator.rectTransform.sizeDelta = new Vector2(_pointSize * 1.5f, _pointSize * 1.5f);
        _progressIndicator.type = Image.Type.Filled;
        _progressIndicator.fillMethod = Image.FillMethod.Radial360;
        _progressIndicator.fillOrigin = (int)Image.Origin360.Top;
        _progressIndicator.fillAmount = 0f;
        progressObj.transform.SetAsFirstSibling();

        // 안내 텍스트 생성
        if (_showInstructions)
        {
            var textObj = new GameObject("InstructionText");
            textObj.transform.SetParent(_canvas.transform, false);

            // 배경
            var bg = textObj.AddComponent<Image>();
            bg.color = new Color(0, 0, 0, 0.7f);
            var bgRect = bg.rectTransform;
            bgRect.anchorMin = new Vector2(0.5f, 0);
            bgRect.anchorMax = new Vector2(0.5f, 0);
            bgRect.pivot = new Vector2(0.5f, 0);
            bgRect.anchoredPosition = new Vector2(0, 20);
            bgRect.sizeDelta = new Vector2(450, 130);

            // 텍스트
            var textChild = new GameObject("Text");
            textChild.transform.SetParent(textObj.transform, false);
            _instructionText = textChild.AddComponent<Text>();
            _instructionText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            _instructionText.fontSize = 18;
            _instructionText.color = Color.white;
            _instructionText.alignment = TextAnchor.MiddleCenter;
            var textRect = _instructionText.rectTransform;
            textRect.anchorMin = new Vector2(0, 0.5f);
            textRect.anchorMax = new Vector2(1, 1);
            textRect.offsetMin = new Vector2(10, 0);
            textRect.offsetMax = new Vector2(-10, -10);

            // 진행률 텍스트
            var progressTextObj = new GameObject("ProgressText");
            progressTextObj.transform.SetParent(textObj.transform, false);
            _progressText = progressTextObj.AddComponent<Text>();
            _progressText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            _progressText.fontSize = 14;
            _progressText.color = new Color(0.8f, 0.8f, 0.8f);
            _progressText.alignment = TextAnchor.MiddleCenter;
            var progressTextRect = _progressText.rectTransform;
            progressTextRect.anchorMin = new Vector2(0, 0);
            progressTextRect.anchorMax = new Vector2(1, 0.5f);
            progressTextRect.offsetMin = new Vector2(10, 10);
            progressTextRect.offsetMax = new Vector2(-10, 0);

            UpdateInstructions("9점 캘리브레이션\n시작 버튼 또는 스페이스바를 누르세요");
            _progressText.text = "";
        }

        // 버튼 생성
        CreateButtons();
    }

    private void CreateButtons()
    {
        // 버튼 컨테이너
        var buttonContainer = new GameObject("ButtonContainer");
        buttonContainer.transform.SetParent(_canvas.transform, false);
        var containerRect = buttonContainer.AddComponent<RectTransform>();
        containerRect.anchorMin = new Vector2(0.5f, 0);
        containerRect.anchorMax = new Vector2(0.5f, 0);
        containerRect.pivot = new Vector2(0.5f, 0);
        containerRect.anchoredPosition = new Vector2(0, 160);
        containerRect.sizeDelta = new Vector2(480, 60);

        // 시작 버튼
        _startButton = CreateButton(buttonContainer.transform, "StartButton", "시작", new Vector2(-150, 0), OnStartButtonClick, new Color(0.2f, 0.7f, 0.3f));

        // 리셋 버튼
        _resetButton = CreateButton(buttonContainer.transform, "ResetButton", "리셋", new Vector2(0, 0), OnResetButtonClick, new Color(0.2f, 0.5f, 0.9f));

        // 취소 버튼 (캘리브레이션 중에만 활성화)
        _cancelButton = CreateButton(buttonContainer.transform, "CancelButton", "취소", new Vector2(150, 0), OnCancelButtonClick, new Color(0.9f, 0.3f, 0.3f));
        _cancelButton.gameObject.SetActive(false);
    }

    private Button CreateButton(Transform parent, string name, string label, Vector2 position, UnityEngine.Events.UnityAction onClick, Color buttonColor)
    {
        var buttonObj = new GameObject(name);
        buttonObj.transform.SetParent(parent, false);

        // 버튼 배경
        var buttonImage = buttonObj.AddComponent<Image>();
        buttonImage.color = buttonColor;
        var buttonRect = buttonImage.rectTransform;
        buttonRect.anchoredPosition = position;
        buttonRect.sizeDelta = new Vector2(130, 55);

        // 버튼 컴포넌트
        var button = buttonObj.AddComponent<Button>();
        button.targetGraphic = buttonImage;
        button.onClick.AddListener(onClick);

        // 버튼 텍스트
        var textObj = new GameObject("Text");
        textObj.transform.SetParent(buttonObj.transform, false);
        var buttonText = textObj.AddComponent<Text>();
        buttonText.text = label;
        buttonText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        buttonText.fontSize = 22;
        buttonText.color = Color.white;
        buttonText.alignment = TextAnchor.MiddleCenter;
        var textRect = buttonText.rectTransform;
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = Vector2.zero;
        textRect.offsetMax = Vector2.zero;

        return button;
    }

    private void OnStartButtonClick()
    {
        if (!_isRunning && L2CSGazeTracker.Instance != null && L2CSGazeTracker.Instance.IsInitialized)
        {
            StartCalibration();
        }
        else if (L2CSGazeTracker.Instance == null || !L2CSGazeTracker.Instance.IsInitialized)
        {
            UpdateInstructions("L2CSGazeTracker가 초기화되지 않았습니다");
            Debug.LogWarning("[Calibration] L2CSGazeTracker가 초기화되지 않았습니다.");
        }
    }

    private void OnResetButtonClick()
    {
        ResetCalibration();
    }

    private void OnCancelButtonClick()
    {
        CancelCalibration();
    }

    private void StartCalibration()
    {
        if (_isRunning) return;

        _isRunning = true;
        _calibrationPoint.gameObject.SetActive(true);
        _calibrationPoint.color = _pointColor;
        _progressIndicator.fillAmount = 0f;

        // 버튼 상태 변경: 시작/리셋 숨기고 취소 표시
        if (_startButton != null) _startButton.gameObject.SetActive(false);
        if (_resetButton != null) _resetButton.gameObject.SetActive(false);
        if (_cancelButton != null) _cancelButton.gameObject.SetActive(true);

        // GazeToScreen 캘리브레이션 시작
        L2CSGazeTracker.Instance.StartCalibration();

        UpdateInstructions("빨간 점을 응시하세요\n자동으로 샘플이 수집됩니다");
        Debug.Log("[Calibration] 9점 자동 캘리브레이션 시작");
    }

    private void UpdateCalibrationUI()
    {
        var tracker = L2CSGazeTracker.Instance;
        if (tracker == null) return;

        var gazeToScreen = tracker.GazeToScreenComponent;
        if (gazeToScreen == null) return;

        // 캘리브레이션 완료 체크
        if (!gazeToScreen.IsCalibrating)
        {
            OnCalibrationComplete(gazeToScreen.IsCalibrated);
            return;
        }

        // 현재 타겟 위치 가져오기 (정규화 좌표)
        Vector2 targetNormalized = gazeToScreen.GetCurrentCalibrationTarget();

        // 화면 좌표로 변환
        // Unity UI: 좌하단이 (0,0), 우상단이 (width, height)
        // 정규화 좌표: 좌상단이 (0,0), 우하단이 (1,1) 가정
        float screenX = targetNormalized.x * _canvasRect.rect.width;
        float screenY = (1f - targetNormalized.y) * _canvasRect.rect.height;  // Y 반전

        // 캔버스 중심 기준 위치로 변환
        Vector2 anchoredPos = new Vector2(screenX, screenY) - _canvasRect.rect.size / 2f;
        _calibrationPoint.rectTransform.anchoredPosition = anchoredPos;

        // 진행률 업데이트
        float progress = gazeToScreen.GetCurrentPointProgress();
        _progressIndicator.fillAmount = progress;

        // 색상 업데이트
        if (progress > 0)
        {
            _calibrationPoint.color = _activeColor;
        }
        else
        {
            _calibrationPoint.color = _pointColor;
        }

        // 텍스트 업데이트
        int currentPoint = gazeToScreen.CurrentCalibrationPointIndex + 1;
        int totalPoints = gazeToScreen.TotalCalibrationPoints;
        UpdateInstructions($"포인트 {currentPoint}/{totalPoints} 응시 중\n점이 노란색이면 샘플 수집 중");
        _progressText.text = $"진행률: {(progress * 100):F0}%";
    }

    private void OnCalibrationComplete(bool success)
    {
        _isRunning = false;
        _calibrationPoint.gameObject.SetActive(false);

        // 버튼 상태 복원: 취소 숨기고 시작/리셋 표시
        if (_startButton != null) _startButton.gameObject.SetActive(true);
        if (_resetButton != null) _resetButton.gameObject.SetActive(true);
        if (_cancelButton != null) _cancelButton.gameObject.SetActive(false);

        if (success)
        {
            UpdateInstructions("캘리브레이션 완료!\n시선 추적이 더 정확해집니다");
            _progressText.text = "Python과 동일한 다항식 회귀 적용됨";
            Debug.Log("[Calibration] 9점 캘리브레이션 성공!");
        }
        else
        {
            UpdateInstructions("캘리브레이션 실패\n시작 버튼을 눌러 다시 시도하세요");
            _progressText.text = "";
            Debug.LogWarning("[Calibration] 캘리브레이션 실패");
        }

        // 잠시 후 기본 메시지로 변경
        StartCoroutine(ResetInstructionsAfterDelay(5f));
    }

    private IEnumerator ResetInstructionsAfterDelay(float delay)
    {
        yield return new WaitForSeconds(delay);
        if (!_isRunning)
        {
            var gazeToScreen = L2CSGazeTracker.Instance?.GazeToScreenComponent;
            bool isCalibrated = gazeToScreen?.IsCalibrated ?? false;

            if (isCalibrated)
            {
                UpdateInstructions("캘리브레이션 적용 중\n시작: 재캘리브레이션 | 리셋: 초기화");
            }
            else
            {
                UpdateInstructions("9점 캘리브레이션\n시작 버튼 또는 스페이스바를 누르세요");
            }
            _progressText.text = "";
        }
    }

    private void ResetCalibration()
    {
        if (L2CSGazeTracker.Instance != null)
        {
            L2CSGazeTracker.Instance.ResetCalibration();
            _isRunning = false;
            _calibrationPoint.gameObject.SetActive(false);

            // 버튼 상태 복원
            if (_startButton != null) _startButton.gameObject.SetActive(true);
            if (_resetButton != null) _resetButton.gameObject.SetActive(true);
            if (_cancelButton != null) _cancelButton.gameObject.SetActive(false);

            UpdateInstructions("캘리브레이션 리셋됨\n시작 버튼을 누르세요");
            _progressText.text = "";
            Debug.Log("[Calibration] 캘리브레이션 리셋");
        }
    }

    private void CancelCalibration()
    {
        if (L2CSGazeTracker.Instance != null)
        {
            L2CSGazeTracker.Instance.CancelCalibration();
            _isRunning = false;
            _calibrationPoint.gameObject.SetActive(false);

            // 버튼 상태 복원
            if (_startButton != null) _startButton.gameObject.SetActive(true);
            if (_resetButton != null) _resetButton.gameObject.SetActive(true);
            if (_cancelButton != null) _cancelButton.gameObject.SetActive(false);

            UpdateInstructions("캘리브레이션 취소됨\n시작 버튼을 누르세요");
            _progressText.text = "";
            Debug.Log("[Calibration] 캘리브레이션 취소됨");
        }
    }

    private void UpdateInstructions(string text)
    {
        if (_instructionText != null)
        {
            _instructionText.text = text;
        }
    }
}
