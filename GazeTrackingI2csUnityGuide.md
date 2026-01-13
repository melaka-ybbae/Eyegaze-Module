# 시선추적 테스트 앱 (Unity + L2CS-Net)

자폐 발달장애 진단 보조용 시선추적 테스트 애플리케이션

---

## 왜 L2CS-Net인가

| 항목 | MediaPipe | L2CS-Net |
|------|-----------|----------|
| 출력 | 홍채 위치 (변환 필요) | **시선 각도 (pitch, yaw)** |
| 학습 데이터 | 얼굴 랜드마크용 | **시선추적 전용** |
| 정확도 | 캘리브레이션 의존 | 3.92° (MPIIGaze 기준) |
| 캘리브레이션 | 필수 (9점) | 간소화 가능 (1~3점) |

---

## 1. 프로젝트 구조

```
GazeTrackingTest/
├── Assets/
│   ├── Models/
│   │   ├── l2cs_gaze360.onnx       # 시선 추정 모델
│   │   └── retinaface.onnx         # 얼굴 검출 모델
│   ├── Scripts/
│   │   ├── GazeTracking/
│   │   │   ├── FaceDetector.cs     # 얼굴 검출 (RetinaFace)
│   │   │   ├── GazeEstimator.cs    # 시선 추정 (L2CS-Net)
│   │   │   ├── GazeToScreen.cs     # 시선 각도 → 화면 좌표 변환
│   │   │   └── GazeFilter.cs       # One Euro Filter
│   │   ├── Calibration/
│   │   │   └── CalibrationManager.cs
│   │   ├── Stimulus/
│   │   │   ├── StimulusManager.cs
│   │   │   └── StimulusItem.cs
│   │   └── Data/
│   │       └── SessionLogger.cs
│   ├── Scenes/
│   │   ├── Calibration.unity
│   │   └── StimulusTest.unity
│   └── Resources/
│       └── Stimuli/
└── Packages/
```

---

## 2. ONNX 모델 준비

### 방법 1: MobileGaze 프로젝트 활용 (추천)

```bash
# MobileGaze (L2CS-Net 기반, ONNX 변환 지원)
git clone https://github.com/yakhyo/gaze-estimation
cd gaze-estimation

# 모델 다운로드 (MobileNet v2 - 모바일 최적화)
# https://github.com/yakhyo/gaze-estimation/releases

# ONNX 변환 (이미 제공됨)
# mobilenetv2_gaze.onnx 사용
```

### 방법 2: 공식 L2CS-Net에서 직접 변환

```bash
git clone https://github.com/Ahmednull/L2CS-Net
cd L2CS-Net

# 가중치 다운로드
# models/L2CSNet_gaze360.pkl

# ONNX 변환 스크립트 (직접 작성 필요)
python export_onnx.py --weights models/L2CSNet_gaze360.pkl --output l2cs_gaze360.onnx
```

### 모델 선택 가이드

| 모델 | 크기 | 속도 | 정확도 |
|------|------|------|--------|
| ResNet50 (L2CS 원본) | ~100MB | 느림 | 최고 |
| MobileNet v2 | ~15MB | 빠름 | 좋음 |
| MobileOne s0 | ~5MB | 매우 빠름 | 보통 |

**추천: MobileNet v2** - 모바일에서 실시간 가능하면서 정확도 괜찮음

---

## 3. Unity 세팅

### Unity 버전 및 패키지
```
Unity 2022.3 LTS
Barracuda 3.0+ (ONNX 런타임)
```

### Barracuda 설치
```
Window → Package Manager → + → Add package by name
com.unity.barracuda
```

### 빌드 세팅
```
File → Build Settings
- Platform: Android
- Scripting Backend: IL2CPP
- Target Architectures: ARM64

Player Settings:
- Camera Usage Description 입력
- Minimum API Level: 24
```

---

## 4. Claude Code 작업 프롬프트

```markdown
# 프로젝트 개요

자폐 발달장애 진단 보조용 시선추적 앱 (Unity + L2CS-Net)

## 기술 스택
- Unity 2022.3 LTS + Barracuda (ONNX 런타임)
- L2CS-Net 기반 시선 추정 (MobileNet v2 백본)
- 타겟: Android (ARM64)

## 핵심 구현 사항

### 1. FaceDetector.cs
- RetinaFace ONNX 모델로 얼굴 검출
- 바운딩박스 + 5개 랜드마크 (눈, 코, 입꼬리)
- 얼굴 영역 크롭하여 GazeEstimator에 전달

### 2. GazeEstimator.cs
- L2CS-Net ONNX 모델 로드 (Barracuda)
- 입력: 얼굴 이미지 (224x224)
- 출력: pitch, yaw (시선 각도, 라디안)

```csharp
using Unity.Barracuda;

public class GazeEstimator : MonoBehaviour
{
    [SerializeField] private NNModel modelAsset;
    private Model runtimeModel;
    private IWorker worker;
    
    public event Action<float, float> OnGazeEstimated; // pitch, yaw
    
    void Start()
    {
        runtimeModel = ModelLoader.Load(modelAsset);
        worker = WorkerFactory.CreateWorker(WorkerFactory.Type.ComputePrecompiled, runtimeModel);
    }
    
    public void Estimate(Texture2D faceImage)
    {
        // 1. 전처리: 224x224 리사이즈, 정규화
        var input = Preprocess(faceImage);
        
        // 2. 추론
        worker.Execute(input);
        
        // 3. 출력 파싱 (pitch, yaw)
        var pitchOutput = worker.PeekOutput("pitch");
        var yawOutput = worker.PeekOutput("yaw");
        
        float pitch = PostprocessAngle(pitchOutput);
        float yaw = PostprocessAngle(yawOutput);
        
        OnGazeEstimated?.Invoke(pitch, yaw);
    }
    
    private Tensor Preprocess(Texture2D tex)
    {
        // RGB 정규화, 텐서 변환
        // mean = [0.485, 0.456, 0.406]
        // std = [0.229, 0.224, 0.225]
    }
    
    private float PostprocessAngle(Tensor output)
    {
        // L2CS-Net은 binned classification + regression
        // softmax → bin index → continuous angle
    }
}
```

### 3. GazeToScreen.cs
- 시선 각도 (pitch, yaw) → 화면 좌표 변환
- 카메라-화면 기하학 기반 계산
- 간단 캘리브레이션으로 오프셋 보정

```csharp
public class GazeToScreen : MonoBehaviour
{
    [SerializeField] private float screenDistance = 0.4f; // 화면까지 거리 (m)
    
    private Vector2 calibrationOffset;
    
    public Vector2 GazeToScreenPoint(float pitch, float yaw)
    {
        // 시선 벡터 계산
        Vector3 gazeDir = new Vector3(
            Mathf.Sin(yaw),
            -Mathf.Sin(pitch),
            -Mathf.Cos(pitch) * Mathf.Cos(yaw)
        );
        
        // 화면 평면과의 교점 계산
        float t = screenDistance / -gazeDir.z;
        float screenX = gazeDir.x * t;
        float screenY = gazeDir.y * t;
        
        // 정규화 (0~1)
        Vector2 normalized = new Vector2(
            screenX / (Screen.width * pixelSize) + 0.5f,
            screenY / (Screen.height * pixelSize) + 0.5f
        );
        
        return normalized + calibrationOffset;
    }
    
    public void Calibrate(Vector2 targetPoint, Vector2 measuredGaze)
    {
        calibrationOffset = targetPoint - measuredGaze;
    }
}
```

### 4. GazeFilter.cs (One Euro Filter)
- 떨림 제거용 필터
- 느린 움직임 = 많이 스무딩
- 빠른 움직임 = 적게 스무딩

```csharp
public class OneEuroFilter
{
    private float minCutoff;
    private float beta;
    private float dCutoff;
    private LowPassFilter xFilter = new LowPassFilter();
    private LowPassFilter dxFilter = new LowPassFilter();
    private float lastValue;
    private bool firstTime = true;

    public OneEuroFilter(float minCutoff = 1.0f, float beta = 0.007f, float dCutoff = 1.0f)
    {
        this.minCutoff = minCutoff;
        this.beta = beta;
        this.dCutoff = dCutoff;
    }

    public float Filter(float x, float rate)
    {
        float dx = firstTime ? 0 : (x - lastValue) * rate;
        firstTime = false;
        lastValue = x;
        
        float edx = dxFilter.Filter(dx, Alpha(rate, dCutoff));
        float cutoff = minCutoff + beta * Mathf.Abs(edx);
        
        return xFilter.Filter(x, Alpha(rate, cutoff));
    }

    private float Alpha(float rate, float cutoff)
    {
        float tau = 1.0f / (2 * Mathf.PI * cutoff);
        float te = 1.0f / rate;
        return 1.0f / (1.0f + tau / te);
    }
}

public class LowPassFilter
{
    private float lastValue;
    private bool firstTime = true;

    public float Filter(float x, float alpha)
    {
        if (firstTime)
        {
            firstTime = false;
            lastValue = x;
            return x;
        }
        lastValue = alpha * x + (1 - alpha) * lastValue;
        return lastValue;
    }
}
```

### 5. CalibrationManager.cs
- 3점 캘리브레이션 (좌상단, 중앙, 우하단)
- 아동용 애니메이션 캐릭터로 주의 유도
- 각 점당 2초 응시

### 6. StimulusManager.cs
- 자극 세트 로드 및 순차 제시
- 자극별 제시 시간 설정
- fixation cross 삽입

### 7. SessionLogger.cs
- 프레임별 로깅: timestamp, pitch, yaw, screen_x, screen_y, stimulus_id
- CSV/JSON 출력
- Application.persistentDataPath에 저장

## 데이터 흐름

```
카메라 프레임
    ↓
FaceDetector (얼굴 검출)
    ↓
얼굴 크롭 이미지
    ↓
GazeEstimator (pitch, yaw 출력)
    ↓
GazeFilter (떨림 제거)
    ↓
GazeToScreen (화면 좌표 변환)
    ↓
UI 표시 + 로깅
```

## 작업 순서

1. ONNX 모델 Unity에 임포트 + Barracuda 세팅
2. FaceDetector 구현 (RetinaFace)
3. GazeEstimator 구현 (L2CS-Net)
4. GazeFilter + GazeToScreen 구현
5. 3점 캘리브레이션 씬
6. 자극 제시 + 로깅

## 참고 자료

- L2CS-Net 공식: https://github.com/Ahmednull/L2CS-Net
- MobileGaze (ONNX 지원): https://github.com/yakhyo/gaze-estimation
- Unity Barracuda: https://docs.unity3d.com/Packages/com.unity.barracuda@3.0/manual/index.html

## 주의사항

- L2CS-Net 출력은 binned classification → 후처리 필요
- 얼굴 검출 실패 시 이전 프레임 값 유지 또는 스킵
- 에디터에서 웹캠으로 테스트 가능, 최종 성능은 실기기 확인
- 조명/거리 변화에 따른 정확도 변화 고려
```

---

## 5. 빠른 시작 체크리스트

- [ ] Unity 2022.3 LTS 설치
- [ ] 새 프로젝트 생성
- [ ] Barracuda 패키지 설치
- [ ] MobileGaze에서 ONNX 모델 다운로드
- [ ] 모델 파일 Assets/Models/에 배치
- [ ] Android 빌드 세팅 완료
- [ ] Claude Code에서 작업 시작

---

## 6. 기대 정확도

| 단계 | 예상 정확도 |
|------|------------|
| L2CS-Net 그대로 | 4~5° |
| + 3점 캘리브레이션 | 2~3° |
| + 필터링 | 1.5~2.5° (안정성 ↑) |
| + 아동 데이터 파인튜닝 | 1~2° (추후) |

자폐 진단 보조 지표 (영역 단위 분석)에는 2~3° 정도면 충분히 유의미함.