using UnityEngine;
using UnityEngine.UI;
using Unity.Sentis;

/// <summary>
/// L2CS-Net 데모씬 자동 설정 스크립트
/// 빈 씬에 이 스크립트를 추가하면 필요한 모든 컴포넌트를 자동으로 구성합니다.
/// </summary>
public class L2CSDemoSceneSetup : MonoBehaviour
{
    [Header("Model Assets")]
    [Tooltip("L2CS-Net ONNX 모델 (448x448 입력, [pitch, yaw] 라디안 출력)")]
    [SerializeField] private ModelAsset _gazeModel;

    [Header("Camera Settings")]
    [SerializeField] private int _cameraWidth = 640;
    [SerializeField] private int _cameraHeight = 480;
    [SerializeField] private int _cameraFps = 30;

    [Header("UI Settings")]
    [SerializeField] private bool _showCameraPreview = true;
    [SerializeField] private bool _showGazePoint = true;
    [SerializeField] private bool _showDebugInfo = true;
    [SerializeField] private float _gazePointSize = 30f;
    [SerializeField] private Color _gazePointColor = Color.green;

    [Header("Filter Settings")]
    [SerializeField] private bool _useFilter = true;
    [SerializeField] private float _filterMinCutoff = 0.3f;
    [SerializeField] private float _filterBeta = 0.001f;

    private void Awake()
    {
        SetupScene();
    }

    /// <summary>
    /// 씬 자동 구성
    /// </summary>
    private void SetupScene()
    {
        // 1. 기존 GazeTracker가 있으면 사용
        var existingTracker = FindObjectOfType<L2CSGazeTracker>();
        if (existingTracker != null)
        {
            Debug.Log("[L2CSDemoSceneSetup] 기존 L2CSGazeTracker 발견, 설정만 업데이트합니다.");
            ConfigureExistingTracker(existingTracker);
            return;
        }

        // 2. GazeTracker 오브젝트 생성
        var trackerObj = new GameObject("L2CSGazeTracker");

        // 3. 컴포넌트 추가
        var faceDetector = trackerObj.AddComponent<FaceDetector>();
        var gazeEstimator = trackerObj.AddComponent<GazeEstimator>();
        var gazeToScreen = trackerObj.AddComponent<GazeToScreen>();
        var gazeTracker = trackerObj.AddComponent<L2CSGazeTracker>();

        // 4. 모델 할당 (리플렉션 사용)
        if (_gazeModel != null)
        {
            SetPrivateField(gazeEstimator, "_modelAsset", _gazeModel);
            Debug.Log("[L2CSDemoSceneSetup] GazeEstimator 모델 할당됨");
        }
        else
        {
            Debug.LogWarning("[L2CSDemoSceneSetup] GazeEstimator 모델이 할당되지 않았습니다. Inspector에서 L2CSNet_gaze360.onnx를 할당하세요.");
        }

        // 5. GazeTracker 설정
        SetPrivateField(gazeTracker, "_faceDetector", faceDetector);
        SetPrivateField(gazeTracker, "_gazeEstimator", gazeEstimator);
        SetPrivateField(gazeTracker, "_gazeToScreen", gazeToScreen);
        SetPrivateField(gazeTracker, "_cameraWidth", _cameraWidth);
        SetPrivateField(gazeTracker, "_cameraHeight", _cameraHeight);
        SetPrivateField(gazeTracker, "_cameraFps", _cameraFps);
        SetPrivateField(gazeTracker, "_useFilter", _useFilter);
        SetPrivateField(gazeTracker, "_filterMinCutoff", _filterMinCutoff);
        SetPrivateField(gazeTracker, "_filterBeta", _filterBeta);

        // 6. 디버그 UI 추가
        var debugSetup = trackerObj.AddComponent<L2CSDebugSetup>();
        SetPrivateField(debugSetup, "_showCameraPreview", _showCameraPreview);
        SetPrivateField(debugSetup, "_showGazePoint", _showGazePoint);
        SetPrivateField(debugSetup, "_showDebugInfo", _showDebugInfo);
        SetPrivateField(debugSetup, "_gazePointSize", _gazePointSize);
        SetPrivateField(debugSetup, "_gazePointColor", _gazePointColor);

        Debug.Log("[L2CSDemoSceneSetup] L2CS 데모씬 구성 완료!");
        Debug.Log("[L2CSDemoSceneSetup] 조작법:");
        Debug.Log("  - S: 크롭 이미지 저장");
        Debug.Log("  - F: 크롭 이미지 상하 반전 토글");
        Debug.Log("  - Quick Calibrate 버튼: 빠른 캘리브레이션");
    }

    /// <summary>
    /// 기존 트래커 설정 업데이트
    /// </summary>
    private void ConfigureExistingTracker(L2CSGazeTracker tracker)
    {
        // 모델 할당
        var gazeEstimator = tracker.GetComponent<GazeEstimator>();
        if (gazeEstimator != null && _gazeModel != null)
        {
            SetPrivateField(gazeEstimator, "_modelAsset", _gazeModel);
        }

        // 디버그 UI가 없으면 추가
        var debugSetup = tracker.GetComponent<L2CSDebugSetup>();
        if (debugSetup == null)
        {
            debugSetup = tracker.gameObject.AddComponent<L2CSDebugSetup>();
        }

        SetPrivateField(debugSetup, "_showCameraPreview", _showCameraPreview);
        SetPrivateField(debugSetup, "_showGazePoint", _showGazePoint);
        SetPrivateField(debugSetup, "_showDebugInfo", _showDebugInfo);
    }

    /// <summary>
    /// 리플렉션을 통한 private 필드 설정
    /// </summary>
    private void SetPrivateField(object target, string fieldName, object value)
    {
        var field = target.GetType().GetField(fieldName,
            System.Reflection.BindingFlags.NonPublic |
            System.Reflection.BindingFlags.Instance);

        if (field != null)
        {
            field.SetValue(target, value);
        }
        else
        {
            Debug.LogWarning($"[L2CSDemoSceneSetup] 필드를 찾을 수 없음: {target.GetType().Name}.{fieldName}");
        }
    }
}
