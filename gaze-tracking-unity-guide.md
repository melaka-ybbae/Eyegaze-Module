# 시선추적 테스트 앱 (Unity) - 프로젝트 가이드

자폐 발달장애 진단 보조용 시선추적 테스트 애플리케이션

---

## 1. Unity 프로젝트 세팅

### 프로젝트 생성
```
Unity Hub → New Project
- Editor: 2022.3 LTS
- Template: 3D (URP 아니어도 됨)
- Project Name: GazeTrackingTest
```

### MediaPipe Unity Plugin 설치
```
Window → Package Manager → + → Add package from git URL

https://github.com/homuler/MediaPipeUnityPlugin.git?path=/Packages/com.github.homuler.mediapipe
```

### 빌드 세팅
```
File → Build Settings
- Platform: Android
- Texture Compression: ASTC
- Scripting Backend: IL2CPP
- Target Architectures: ARM64

Player Settings:
- Camera Usage Description 입력
- Minimum API Level: 24
```

### 폴더 구조 생성
```
Assets/
├── Scripts/
│   ├── GazeTracking/
│   ├── Calibration/
│   ├── Stimulus/
│   └── Data/
├── Scenes/
├── Prefabs/
└── Resources/
    └── Stimuli/
```

---

## 2. Claude Code 작업 프롬프트

```markdown
# 프로젝트 개요

자폐 발달장애 진단 보조용 시선추적 테스트 애플리케이션 (Unity)

## 기술 스택
- Unity 2022.3 LTS
- MediaPipe Unity Plugin (Face Mesh + Iris Tracking)
- 타겟: Android (ARM64)

## 프로젝트 구조

Assets/
├── Scripts/
│   ├── GazeTracking/
│   │   ├── GazeTracker.cs          # MediaPipe 연동, 홍채 추적
│   │   ├── GazeCalibrator.cs       # 캘리브레이션 로직
│   │   └── GazeData.cs             # 시선 데이터 구조체
│   ├── Calibration/
│   │   └── CalibrationManager.cs   # 9점 캘리브레이션 씬 관리
│   ├── Stimulus/
│   │   ├── StimulusManager.cs      # 자극 제시 순서 관리
│   │   └── StimulusItem.cs         # 개별 자극 아이템
│   └── Data/
│       └── SessionLogger.cs        # 세션 데이터 CSV/JSON 저장
├── Scenes/
│   ├── Calibration.unity
│   └── StimulusTest.unity
└── Resources/
    └── Stimuli/                    # 테스트용 이미지/영상

## 핵심 기능 요구사항

### 1. GazeTracker.cs
- MediaPipe Face Mesh 솔루션 초기화
- 카메라 프레임에서 478개 랜드마크 추출
- 홍채 랜드마크(468~477)로 시선 방향 계산
- 이벤트: OnGazeUpdated(Vector2 screenPosition)

### 2. GazeCalibrator.cs
- 9점 캘리브레이션 (화면 모서리 + 중앙)
- 각 점당 수집된 홍채 위치 → 화면 좌표 매핑
- 호모그래피 또는 다항식 회귀로 변환 행렬 계산
- 캘리브레이션 데이터 저장/로드

### 3. CalibrationManager.cs
- 캘리브레이션 포인트 순차 표시 (애니메이션으로 주목 유도)
- 아동용: 캐릭터/별 등 흥미 요소 사용
- 각 포인트 응시 시간: 2초
- 완료 후 StimulusTest 씬으로 전환

### 4. StimulusManager.cs
- 자극 세트 로드 (Resources/Stimuli)
- 자극 제시 순서 랜덤화 옵션
- 자극별 제시 시간 설정 (기본 5초)
- 자극 간 빈 화면(fixation cross) 삽입

### 5. SessionLogger.cs
- 세션 메타데이터: 시작시간, 기기정보, 캘리브레이션 품질
- 프레임별 로깅: timestamp, gaze_x, gaze_y, stimulus_id, stimulus_position
- 출력 형식: CSV (분석 편의), JSON (구조화)
- 저장 경로: Application.persistentDataPath

## MediaPipe 연동 참고

```csharp
// MediaPipe Unity Plugin 사용 예시
using Mediapipe.Unity;
using Mediapipe.Unity.FaceMesh;

public class GazeTracker : MonoBehaviour
{
    [SerializeField] private FaceMeshSolution _faceMeshSolution;
    
    // 홍채 랜드마크 인덱스
    private static readonly int[] LeftIrisIndices = { 468, 469, 470, 471, 472 };
    private static readonly int[] RightIrisIndices = { 473, 474, 475, 476, 477 };
    
    // 눈 영역 랜드마크 (시선 방향 계산용)
    private static readonly int LeftEyeInner = 133;
    private static readonly int LeftEyeOuter = 33;
    private static readonly int RightEyeInner = 362;
    private static readonly int RightEyeOuter = 263;
    
    public event Action<Vector2> OnGazeUpdated;
    
    private void OnFaceLandmarksOutput(NormalizedLandmarkList landmarks)
    {
        // 홍채 중심 계산
        // 눈 영역 내 상대 위치로 시선 방향 추정
        // 캘리브레이션 매트릭스 적용
        // OnGazeUpdated 이벤트 발생
    }
}
```

## 작업 우선순위

1. GazeTracker 기본 구현 (MediaPipe 연동 확인)
2. 캘리브레이션 씬 + GazeCalibrator
3. StimulusManager + 기본 자극 제시
4. SessionLogger 데이터 로깅
5. UI 다듬기 + 아동 친화적 요소

## 주의사항

- MediaPipe Unity Plugin은 에디터에서도 동작함 (PC 웹캠 사용 가능)
- 최종 성능 테스트는 실기기에서 확인 권장
- 카메라 권한 처리 필요
- 홍채 추적 실패 시 fallback 처리 (눈 중심점 사용)
- 조명/거리에 따른 정확도 변화 고려

## 참고 자료

- MediaPipe Unity Plugin: https://github.com/homuler/MediaPipeUnityPlugin
- Face Mesh 랜드마크 맵: https://github.com/google/mediapipe/blob/master/mediapipe/modules/face_geometry/data/canonical_face_model_uv_visualization.png
- 홍채 랜드마크 설명: https://google.github.io/mediapipe/solutions/iris
```

---

## 3. 빠른 시작 체크리스트

- [ ] Unity 2022.3 LTS 설치
- [ ] 새 프로젝트 생성 (GazeTrackingTest)
- [ ] MediaPipe Unity Plugin 설치
- [ ] Android 빌드 세팅 완료
- [ ] 폴더 구조 생성
- [ ] 테스트 기기 연결 확인
- [ ] Claude Code에서 프롬프트로 작업 시작