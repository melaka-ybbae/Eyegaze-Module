using System;
using System.Collections;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 자극 제시 순서 관리
/// </summary>
public class StimulusManager : MonoBehaviour
{
    public static StimulusManager Instance { get; private set; }

    [Header("UI References")]
    [SerializeField] private Image _stimulusDisplay;
    [SerializeField] private Image _fixationCross;
    [SerializeField] private Canvas _canvas;

    [Header("Stimulus Settings")]
    [SerializeField] private StimulusSet _stimulusSet;
    [SerializeField] private float _defaultDisplayDuration = 5f;
    [SerializeField] private float _fixationDuration = 1f;

    [Header("Fixation Cross")]
    [SerializeField] private Color _fixationColor = Color.white;
    [SerializeField] private float _fixationSize = 50f;

    // 상태
    private int _currentIndex = -1;
    private int[] _presentationOrder;
    private bool _isRunning = false;
    private Coroutine _presentationCoroutine;

    // 현재 자극
    public StimulusItem CurrentStimulus =>
        _currentIndex >= 0 && _currentIndex < _presentationOrder.Length
            ? _stimulusSet.Items[_presentationOrder[_currentIndex]]
            : null;

    public bool IsRunning => _isRunning;
    public int CurrentIndex => _currentIndex;
    public int TotalCount => _stimulusSet?.Items?.Length ?? 0;

    // 이벤트
    public event Action<StimulusItem, int> OnStimulusPresented; // stimulus, index
    public event Action<StimulusItem> OnStimulusEnded;
    public event Action OnFixationStarted;
    public event Action OnSessionCompleted;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }

    private void Start()
    {
        if (_canvas == null)
            _canvas = GetComponentInParent<Canvas>();

        SetupFixationCross();
    }

    private void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

    /// <summary>
    /// Fixation Cross 설정
    /// </summary>
    private void SetupFixationCross()
    {
        if (_fixationCross != null)
        {
            _fixationCross.color = _fixationColor;
            _fixationCross.rectTransform.sizeDelta = new Vector2(_fixationSize, _fixationSize);
            _fixationCross.gameObject.SetActive(false);
        }
    }

    /// <summary>
    /// 자극 세트 로드
    /// </summary>
    public void LoadStimulusSet(StimulusSet set)
    {
        _stimulusSet = set;
        if (set != null)
        {
            _fixationDuration = set.FixationDuration;
            Debug.Log($"[StimulusManager] 자극 세트 로드: {set.SetName} ({set.Items.Length}개 자극)");
        }
    }

    /// <summary>
    /// 자극 제시 시작
    /// </summary>
    public void StartPresentation()
    {
        if (_stimulusSet == null || _stimulusSet.Items.Length == 0)
        {
            Debug.LogError("[StimulusManager] 자극 세트가 없거나 비어있습니다.");
            return;
        }

        if (_isRunning)
        {
            Debug.LogWarning("[StimulusManager] 이미 실행 중입니다.");
            return;
        }

        _isRunning = true;
        _currentIndex = -1;
        _presentationOrder = _stimulusSet.GetRandomizedOrder();

        Debug.Log($"[StimulusManager] 자극 제시 시작 - 순서: {string.Join(", ", _presentationOrder)}");

        _presentationCoroutine = StartCoroutine(PresentationSequence());
    }

    /// <summary>
    /// 자극 제시 중지
    /// </summary>
    public void StopPresentation()
    {
        if (!_isRunning) return;

        if (_presentationCoroutine != null)
        {
            StopCoroutine(_presentationCoroutine);
            _presentationCoroutine = null;
        }

        _isRunning = false;
        HideAll();
    }

    /// <summary>
    /// 자극 제시 시퀀스
    /// </summary>
    private IEnumerator PresentationSequence()
    {
        for (int i = 0; i < _presentationOrder.Length; i++)
        {
            _currentIndex = i;
            StimulusItem stimulus = _stimulusSet.Items[_presentationOrder[i]];

            // Fixation Cross 표시
            yield return ShowFixation();

            // 자극 표시
            yield return ShowStimulus(stimulus);
        }

        // 세션 완료
        CompleteSession();
    }

    /// <summary>
    /// Fixation Cross 표시
    /// </summary>
    private IEnumerator ShowFixation()
    {
        if (_fixationCross != null)
        {
            _fixationCross.gameObject.SetActive(true);
        }
        if (_stimulusDisplay != null)
        {
            _stimulusDisplay.gameObject.SetActive(false);
        }

        OnFixationStarted?.Invoke();

        yield return new WaitForSeconds(_fixationDuration);

        if (_fixationCross != null)
        {
            _fixationCross.gameObject.SetActive(false);
        }
    }

    /// <summary>
    /// 자극 표시
    /// </summary>
    private IEnumerator ShowStimulus(StimulusItem stimulus)
    {
        if (_stimulusDisplay != null && stimulus.Type == StimulusType.Image)
        {
            _stimulusDisplay.gameObject.SetActive(true);
            _stimulusDisplay.sprite = stimulus.ImageContent;

            // 위치 및 크기 설정
            RectTransform rect = _stimulusDisplay.rectTransform;
            RectTransform canvasRect = _canvas.GetComponent<RectTransform>();

            rect.anchoredPosition = new Vector2(
                (stimulus.Position.x - 0.5f) * canvasRect.rect.width,
                (stimulus.Position.y - 0.5f) * canvasRect.rect.height
            );
            rect.sizeDelta = new Vector2(
                stimulus.Size.x * canvasRect.rect.width,
                stimulus.Size.y * canvasRect.rect.height
            );
        }

        OnStimulusPresented?.Invoke(stimulus, _currentIndex);

        float duration = stimulus.DisplayDuration > 0 ? stimulus.DisplayDuration : _defaultDisplayDuration;
        yield return new WaitForSeconds(duration);

        OnStimulusEnded?.Invoke(stimulus);

        if (_stimulusDisplay != null)
        {
            _stimulusDisplay.gameObject.SetActive(false);
        }
    }

    /// <summary>
    /// 모든 UI 숨기기
    /// </summary>
    private void HideAll()
    {
        if (_stimulusDisplay != null)
            _stimulusDisplay.gameObject.SetActive(false);
        if (_fixationCross != null)
            _fixationCross.gameObject.SetActive(false);
    }

    /// <summary>
    /// 세션 완료
    /// </summary>
    private void CompleteSession()
    {
        _isRunning = false;
        HideAll();

        Debug.Log("[StimulusManager] 자극 제시 세션 완료");
        OnSessionCompleted?.Invoke();
    }

    /// <summary>
    /// 다음 자극으로 건너뛰기
    /// </summary>
    public void SkipToNext()
    {
        if (!_isRunning) return;

        StopCoroutine(_presentationCoroutine);

        if (_currentIndex < _presentationOrder.Length - 1)
        {
            _presentationCoroutine = StartCoroutine(ContinueFromIndex(_currentIndex + 1));
        }
        else
        {
            CompleteSession();
        }
    }

    /// <summary>
    /// 특정 인덱스부터 계속
    /// </summary>
    private IEnumerator ContinueFromIndex(int startIndex)
    {
        for (int i = startIndex; i < _presentationOrder.Length; i++)
        {
            _currentIndex = i;
            StimulusItem stimulus = _stimulusSet.Items[_presentationOrder[i]];

            yield return ShowFixation();
            yield return ShowStimulus(stimulus);
        }

        CompleteSession();
    }
}
