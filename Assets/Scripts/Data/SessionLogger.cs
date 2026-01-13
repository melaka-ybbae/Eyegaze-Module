using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

/// <summary>
/// 세션 데이터 로깅
/// CSV 및 JSON 형식으로 저장
/// </summary>
public class SessionLogger : MonoBehaviour
{
    public static SessionLogger Instance { get; private set; }

    [Header("Settings")]
    [SerializeField] private bool _autoStartLogging = true;
    [SerializeField] private float _logInterval = 0.033f; // ~30fps
    [SerializeField] private bool _saveAsCSV = true;
    [SerializeField] private bool _saveAsJSON = true;

    // 세션 데이터
    private SessionData _currentSession;
    private List<GazeLogEntry> _gazeEntries = new List<GazeLogEntry>();
    private bool _isLogging = false;
    private float _nextLogTime = 0f;

    public bool IsLogging => _isLogging;
    public SessionData CurrentSession => _currentSession;

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
        if (_autoStartLogging)
        {
            StartNewSession();
        }
    }

    private void Update()
    {
        if (!_isLogging) return;

        if (Time.time >= _nextLogTime)
        {
            _nextLogTime = Time.time + _logInterval;
            LogCurrentGaze();
        }
    }

    private void OnDestroy()
    {
        if (_isLogging)
        {
            EndSession();
        }

        if (Instance == this)
            Instance = null;
    }

    /// <summary>
    /// 새 세션 시작
    /// </summary>
    public void StartNewSession(string participantId = "")
    {
        _currentSession = new SessionData
        {
            SessionId = Guid.NewGuid().ToString(),
            ParticipantId = string.IsNullOrEmpty(participantId) ? "Unknown" : participantId,
            StartTime = DateTime.Now,
            DeviceInfo = SystemInfo.deviceModel,
            ScreenResolution = $"{Screen.width}x{Screen.height}",
            CalibrationQuality = GazeTracker.Instance?.Calibrator?.GetQualityScore() ?? 0f
        };

        _gazeEntries.Clear();
        _isLogging = true;
        _nextLogTime = Time.time;

        // 이벤트 구독
        SubscribeToEvents();

        Debug.Log($"[SessionLogger] 세션 시작: {_currentSession.SessionId}");
    }

    /// <summary>
    /// 세션 종료 및 저장
    /// </summary>
    public void EndSession()
    {
        if (!_isLogging) return;

        _isLogging = false;
        _currentSession.EndTime = DateTime.Now;

        UnsubscribeFromEvents();

        // 파일 저장
        SaveSession();

        Debug.Log($"[SessionLogger] 세션 종료: {_gazeEntries.Count}개 로그 저장됨");
    }

    /// <summary>
    /// 이벤트 구독
    /// </summary>
    private void SubscribeToEvents()
    {
        if (GazeTracker.Instance != null)
        {
            GazeTracker.Instance.OnTrackingLost += OnTrackingLost;
            GazeTracker.Instance.OnTrackingStarted += OnTrackingStarted;
        }

        if (StimulusManager.Instance != null)
        {
            StimulusManager.Instance.OnStimulusPresented += OnStimulusPresented;
            StimulusManager.Instance.OnStimulusEnded += OnStimulusEnded;
        }
    }

    /// <summary>
    /// 이벤트 구독 해제
    /// </summary>
    private void UnsubscribeFromEvents()
    {
        if (GazeTracker.Instance != null)
        {
            GazeTracker.Instance.OnTrackingLost -= OnTrackingLost;
            GazeTracker.Instance.OnTrackingStarted -= OnTrackingStarted;
        }

        if (StimulusManager.Instance != null)
        {
            StimulusManager.Instance.OnStimulusPresented -= OnStimulusPresented;
            StimulusManager.Instance.OnStimulusEnded -= OnStimulusEnded;
        }
    }

    /// <summary>
    /// 현재 시선 데이터 로깅
    /// </summary>
    private void LogCurrentGaze()
    {
        if (GazeTracker.Instance == null) return;

        GazeData gazeData = GazeTracker.Instance.CurrentGazeData;
        StimulusItem currentStimulus = StimulusManager.Instance?.CurrentStimulus;

        GazeLogEntry entry = new GazeLogEntry
        {
            Timestamp = Time.time,
            SystemTime = DateTime.Now.ToString("HH:mm:ss.fff"),
            GazeX = gazeData.ScreenPosition.x,
            GazeY = gazeData.ScreenPosition.y,
            LeftIrisX = gazeData.LeftIrisCenter.x,
            LeftIrisY = gazeData.LeftIrisCenter.y,
            RightIrisX = gazeData.RightIrisCenter.x,
            RightIrisY = gazeData.RightIrisCenter.y,
            IsValid = gazeData.IsValid,
            Confidence = gazeData.Confidence,
            StimulusId = currentStimulus?.Id ?? "",
            StimulusName = currentStimulus?.Name ?? "",
            AOIIndex = currentStimulus?.GetAOIIndex(gazeData.ScreenPosition) ?? -1,
            IsOnStimulus = currentStimulus?.IsGazeOnStimulus(gazeData.ScreenPosition) ?? false
        };

        _gazeEntries.Add(entry);
    }

    /// <summary>
    /// 세션 저장
    /// </summary>
    private void SaveSession()
    {
        string basePath = Application.persistentDataPath;
        string timestamp = _currentSession.StartTime.ToString("yyyyMMdd_HHmmss");
        string baseFileName = $"session_{timestamp}_{_currentSession.ParticipantId}";

        if (_saveAsCSV)
        {
            SaveAsCSV(Path.Combine(basePath, baseFileName + ".csv"));
        }

        if (_saveAsJSON)
        {
            SaveAsJSON(Path.Combine(basePath, baseFileName + ".json"));
        }
    }

    /// <summary>
    /// CSV 형식으로 저장
    /// </summary>
    private void SaveAsCSV(string filePath)
    {
        StringBuilder sb = new StringBuilder();

        // 헤더
        sb.AppendLine("timestamp,system_time,gaze_x,gaze_y,left_iris_x,left_iris_y,right_iris_x,right_iris_y,is_valid,confidence,stimulus_id,stimulus_name,aoi_index,is_on_stimulus");

        // 데이터
        foreach (var entry in _gazeEntries)
        {
            sb.AppendLine($"{entry.Timestamp:F4},{entry.SystemTime},{entry.GazeX:F4},{entry.GazeY:F4},{entry.LeftIrisX:F4},{entry.LeftIrisY:F4},{entry.RightIrisX:F4},{entry.RightIrisY:F4},{(entry.IsValid ? 1 : 0)},{entry.Confidence:F3},{entry.StimulusId},{entry.StimulusName},{entry.AOIIndex},{(entry.IsOnStimulus ? 1 : 0)}");
        }

        File.WriteAllText(filePath, sb.ToString());
        Debug.Log($"[SessionLogger] CSV 저장: {filePath}");
    }

    /// <summary>
    /// JSON 형식으로 저장
    /// </summary>
    private void SaveAsJSON(string filePath)
    {
        SessionExport export = new SessionExport
        {
            Session = _currentSession,
            GazeData = _gazeEntries.ToArray()
        };

        string json = JsonUtility.ToJson(export, true);
        File.WriteAllText(filePath, json);
        Debug.Log($"[SessionLogger] JSON 저장: {filePath}");
    }

    // 이벤트 핸들러
    private void OnTrackingLost()
    {
        AddEventLog("tracking_lost");
    }

    private void OnTrackingStarted()
    {
        AddEventLog("tracking_started");
    }

    private void OnStimulusPresented(StimulusItem stimulus, int index)
    {
        AddEventLog($"stimulus_presented:{stimulus.Id}:{index}");
    }

    private void OnStimulusEnded(StimulusItem stimulus)
    {
        AddEventLog($"stimulus_ended:{stimulus.Id}");
    }

    private void AddEventLog(string eventType)
    {
        // 이벤트를 별도 리스트에 저장하거나 현재 로그에 마킹
        Debug.Log($"[SessionLogger] Event: {eventType} at {Time.time:F4}");
    }

    /// <summary>
    /// 저장 경로 반환
    /// </summary>
    public string GetSavePath()
    {
        return Application.persistentDataPath;
    }
}

/// <summary>
/// 세션 메타데이터
/// </summary>
[Serializable]
public class SessionData
{
    public string SessionId;
    public string ParticipantId;
    public DateTime StartTime;
    public DateTime EndTime;
    public string DeviceInfo;
    public string ScreenResolution;
    public float CalibrationQuality;
}

/// <summary>
/// 시선 로그 엔트리
/// </summary>
[Serializable]
public class GazeLogEntry
{
    public float Timestamp;
    public string SystemTime;
    public float GazeX;
    public float GazeY;
    public float LeftIrisX;
    public float LeftIrisY;
    public float RightIrisX;
    public float RightIrisY;
    public bool IsValid;
    public float Confidence;
    public string StimulusId;
    public string StimulusName;
    public int AOIIndex;
    public bool IsOnStimulus;
}

/// <summary>
/// 세션 내보내기 구조
/// </summary>
[Serializable]
public class SessionExport
{
    public SessionData Session;
    public GazeLogEntry[] GazeData;
}
