#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
using Unity.Sentis;

/// <summary>
/// L2CS-Net 데모씬 생성 에디터 유틸리티
/// </summary>
public class L2CSDemoSceneCreator : EditorWindow
{
    private ModelAsset _gazeModel;

    [MenuItem("Eyegaze/Create L2CS Demo Scene")]
    public static void ShowWindow()
    {
        var window = GetWindow<L2CSDemoSceneCreator>("L2CS Demo Scene Creator");
        window.minSize = new Vector2(400, 300);

        // 기본 모델 경로에서 자동 로드 시도
        window.TryLoadDefaultModels();
    }

    private void TryLoadDefaultModels()
    {
        // L2CS 모델 자동 탐색
        string[] gazeModelGuids = AssetDatabase.FindAssets("L2CSNet_gaze360 t:Unity.Sentis.ModelAsset");
        if (gazeModelGuids.Length > 0)
        {
            string path = AssetDatabase.GUIDToAssetPath(gazeModelGuids[0]);
            _gazeModel = AssetDatabase.LoadAssetAtPath<ModelAsset>(path);
        }
    }

    private void OnGUI()
    {
        EditorGUILayout.Space(10);
        EditorGUILayout.LabelField("L2CS-Net Demo Scene Creator", EditorStyles.boldLabel);
        EditorGUILayout.Space(5);

        EditorGUILayout.HelpBox(
            "이 도구는 L2CS-Net 시선 추적 데모씬을 자동으로 생성합니다.\n" +
            "모델 파일을 할당하고 'Create Demo Scene' 버튼을 클릭하세요.",
            MessageType.Info);

        EditorGUILayout.Space(10);

        // 모델 할당 필드
        EditorGUILayout.LabelField("Model Assets", EditorStyles.boldLabel);

        _gazeModel = (ModelAsset)EditorGUILayout.ObjectField(
            new GUIContent("Gaze Model (필수)", "L2CSNet_gaze360.onnx"),
            _gazeModel,
            typeof(ModelAsset),
            false);

        EditorGUILayout.Space(10);

        // 모델 정보
        EditorGUILayout.LabelField("Model Info", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            "L2CSNet_gaze360.onnx 모델 정보:\n" +
            "• 입력: face (1, 3, 448, 448) - RGB 얼굴 이미지\n" +
            "• 출력: gaze (1, 2) - [pitch, yaw] 라디안\n" +
            "• 크기: 약 0.28 MB",
            MessageType.None);

        EditorGUILayout.Space(20);

        // 생성 버튼
        GUI.enabled = _gazeModel != null;
        if (GUILayout.Button("Create Demo Scene", GUILayout.Height(40)))
        {
            CreateDemoScene();
        }
        GUI.enabled = true;

        if (_gazeModel == null)
        {
            EditorGUILayout.HelpBox(
                "Gaze Model을 할당해야 합니다.\n" +
                "Assets/Models/L2CSNet/L2CSNet_gaze360.onnx 파일을 할당하세요.",
                MessageType.Warning);
        }

        EditorGUILayout.Space(10);

        // 기존 씬에 추가 버튼
        EditorGUILayout.LabelField("Or add to current scene:", EditorStyles.miniLabel);
        GUI.enabled = _gazeModel != null;
        if (GUILayout.Button("Add L2CS Tracker to Current Scene"))
        {
            AddTrackerToCurrentScene();
        }
        GUI.enabled = true;
    }

    private void CreateDemoScene()
    {
        // 새 씬 생성
        Scene newScene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);

        // 씬 이름 설정
        string scenePath = "Assets/Scenes/L2CSDemo.unity";

        // Scenes 폴더가 없으면 생성
        if (!AssetDatabase.IsValidFolder("Assets/Scenes"))
        {
            AssetDatabase.CreateFolder("Assets", "Scenes");
        }

        // 트래커 추가
        CreateTrackerObject();

        // 씬 저장
        EditorSceneManager.SaveScene(newScene, scenePath);
        AssetDatabase.Refresh();

        Debug.Log($"[L2CSDemoSceneCreator] 데모씬이 생성되었습니다: {scenePath}");
        EditorUtility.DisplayDialog("Success", $"데모씬이 생성되었습니다!\n\n{scenePath}\n\n플레이 버튼을 눌러 테스트하세요.", "OK");
    }

    private void AddTrackerToCurrentScene()
    {
        // 기존 트래커 확인
        var existing = Object.FindObjectOfType<L2CSGazeTracker>();
        if (existing != null)
        {
            if (!EditorUtility.DisplayDialog("Warning",
                "이미 L2CSGazeTracker가 씬에 있습니다. 새로 생성하시겠습니까?",
                "Yes", "No"))
            {
                return;
            }
            DestroyImmediate(existing.gameObject);
        }

        CreateTrackerObject();

        // 씬 dirty 표시
        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());

        Debug.Log("[L2CSDemoSceneCreator] L2CS Tracker가 현재 씬에 추가되었습니다.");
    }

    private void CreateTrackerObject()
    {
        // 메인 오브젝트 생성
        var trackerObj = new GameObject("L2CSGazeTracker");

        // 컴포넌트 추가
        trackerObj.AddComponent<FaceDetector>();
        var gazeEstimator = trackerObj.AddComponent<GazeEstimator>();
        trackerObj.AddComponent<GazeToScreen>();
        trackerObj.AddComponent<L2CSGazeTracker>();
        trackerObj.AddComponent<L2CSDebugSetup>();

        // 모델 할당 (SerializedObject 사용)
        if (_gazeModel != null)
        {
            var so = new SerializedObject(gazeEstimator);
            so.FindProperty("_modelAsset").objectReferenceValue = _gazeModel;
            so.ApplyModifiedProperties();
        }

        // 선택
        Selection.activeGameObject = trackerObj;

        Debug.Log("[L2CSDemoSceneCreator] L2CSGazeTracker 오브젝트 생성 완료");
        Debug.Log("  조작법:");
        Debug.Log("  - S: 크롭 이미지 저장");
        Debug.Log("  - F: 크롭 이미지 상하 반전 토글");
        Debug.Log("  - Quick Calibrate 버튼: 빠른 캘리브레이션");
    }
}
#endif
