using System;
using System.Collections;
using UnityEngine;
using Mediapipe;
using Mediapipe.Tasks.Vision.FaceDetector;
using MPFaceDetectionResult = Mediapipe.Tasks.Components.Containers.DetectionResult;

/// <summary>
/// MediaPipe Tasks API 기반 얼굴 검출기
/// homuler의 MediaPipeUnityPlugin 패키지를 사용합니다.
/// </summary>
public class FaceDetector : MonoBehaviour
{
    [Header("Detection Settings")]
    [SerializeField] private float _minDetectionConfidence = 0.5f;
    [SerializeField] private float _minSuppressionThreshold = 0.3f;
    [SerializeField] private int _numFaces = 1;

    [Header("Model Settings")]
    [SerializeField] private string _modelPath = "blaze_face_short_range.bytes";

    private Mediapipe.Tasks.Vision.FaceDetector.FaceDetector _detector;
    private bool _isInitialized = false;
    private bool _isInitializing = false;

    // 검출 결과
    public struct FaceDetectionResult
    {
        public UnityEngine.Rect BoundingBox;           // 얼굴 영역 (정규화된 좌표 0~1)
        public Vector2 LeftEye;            // 왼쪽 눈 위치
        public Vector2 RightEye;           // 오른쪽 눈 위치
        public Vector2 Nose;               // 코 위치
        public Vector2 LeftMouth;          // 왼쪽 입꼬리
        public Vector2 RightMouth;         // 오른쪽 입꼬리
        public float Confidence;           // 검출 신뢰도
        public bool IsValid;               // 유효한 검출인지
    }

    public event Action<FaceDetectionResult> OnFaceDetected;

    public bool IsInitialized => _isInitialized;

    private void Awake()
    {
        StartCoroutine(InitializeAsync());
    }

    private void OnDestroy()
    {
        Cleanup();
    }

    /// <summary>
    /// 검출기 비동기 초기화
    /// </summary>
    private IEnumerator InitializeAsync()
    {
        if (_isInitializing) yield break;
        _isInitializing = true;

        Debug.Log("[FaceDetector] MediaPipe 초기화 시작...");

        // 모델 파일이 StreamingAssets에 있는지 확인
        string modelFullPath = System.IO.Path.Combine(Application.streamingAssetsPath, _modelPath);

        // StreamingAssets 폴더가 없을 수 있으므로 약간의 지연
        yield return new WaitForSeconds(0.1f);

        try
        {
            var baseOptions = new Mediapipe.Tasks.Core.BaseOptions(
                Mediapipe.Tasks.Core.BaseOptions.Delegate.CPU,
                modelAssetPath: modelFullPath
            );

            var options = new FaceDetectorOptions(
                baseOptions,
                runningMode: Mediapipe.Tasks.Vision.Core.RunningMode.IMAGE,
                minDetectionConfidence: _minDetectionConfidence,
                minSuppressionThreshold: _minSuppressionThreshold,
                numFaces: _numFaces
            );

            _detector = Mediapipe.Tasks.Vision.FaceDetector.FaceDetector.CreateFromOptions(options);
            _isInitialized = true;
            Debug.Log("[FaceDetector] MediaPipe 초기화 완료");
        }
        catch (Exception e)
        {
            Debug.LogError($"[FaceDetector] 초기화 실패: {e.Message}\n{e.StackTrace}");
            Debug.LogWarning("[FaceDetector] 모델 파일이 StreamingAssets 폴더에 있는지 확인하세요: " + _modelPath);
            _isInitialized = false;
        }

        _isInitializing = false;
    }

    /// <summary>
    /// 동기 초기화 (이미 모델이 로드된 경우)
    /// </summary>
    public void Initialize()
    {
        if (_isInitialized) return;
        StartCoroutine(InitializeAsync());
    }

    /// <summary>
    /// 얼굴 검출 수행
    /// </summary>
    /// <param name="texture">입력 이미지</param>
    /// <returns>검출 결과</returns>
    public FaceDetectionResult Detect(Texture texture)
    {
        if (!_isInitialized || _detector == null)
        {
            return new FaceDetectionResult { IsValid = false };
        }

        try
        {
            // Texture2D로 변환
            Texture2D tex2D = texture as Texture2D;
            bool shouldDestroy = false;

            if (tex2D == null)
            {
                // RenderTexture 등 다른 타입인 경우 변환
                tex2D = ConvertToTexture2D(texture);
                shouldDestroy = true;
            }

            // Texture2D에서 직접 Image 생성
            // 주의: Unity Texture2D는 Y=0이 하단, MediaPipe는 Y=0이 상단
            // MediaPipe Unity Plugin이 내부적으로 변환을 처리한다고 가정
            using var image = new Image(tex2D);

            // 검출 실행
            var mpResult = _detector.Detect(image);

            // 결과 파싱
            var result = ParseDetections(mpResult, tex2D.width, tex2D.height);

            if (shouldDestroy)
            {
                Destroy(tex2D);
            }

            if (result.IsValid)
            {
                OnFaceDetected?.Invoke(result);
            }

            return result;
        }
        catch (Exception e)
        {
            Debug.LogError($"[FaceDetector] 검출 실패: {e.Message}");
            return new FaceDetectionResult { IsValid = false };
        }
    }

    /// <summary>
    /// Texture를 Texture2D로 변환
    /// </summary>
    private Texture2D ConvertToTexture2D(Texture texture)
    {
        RenderTexture rt = RenderTexture.GetTemporary(texture.width, texture.height, 0, RenderTextureFormat.ARGB32);
        Graphics.Blit(texture, rt);

        RenderTexture previous = RenderTexture.active;
        RenderTexture.active = rt;

        Texture2D tex2D = new Texture2D(texture.width, texture.height, TextureFormat.RGBA32, false);
        tex2D.ReadPixels(new UnityEngine.Rect(0, 0, texture.width, texture.height), 0, 0);
        tex2D.Apply();

        RenderTexture.active = previous;
        RenderTexture.ReleaseTemporary(rt);

        return tex2D;
    }

    /// <summary>
    /// MediaPipe 검출 결과 파싱
    /// </summary>
    private FaceDetectionResult ParseDetections(MPFaceDetectionResult mpResult, int imageWidth, int imageHeight)
    {
        var result = new FaceDetectionResult { IsValid = false };

        if (mpResult.detections == null || mpResult.detections.Count == 0)
        {
            return result;
        }

        // 가장 높은 confidence를 가진 얼굴 찾기
        int bestIdx = 0;
        float bestScore = 0f;

        for (int i = 0; i < mpResult.detections.Count; i++)
        {
            var detection = mpResult.detections[i];
            if (detection.categories != null && detection.categories.Count > 0)
            {
                float score = detection.categories[0].score;
                if (score > bestScore)
                {
                    bestScore = score;
                    bestIdx = i;
                }
            }
        }

        var bestDetection = mpResult.detections[bestIdx];
        var bbox = bestDetection.boundingBox;

        // 바운딩 박스 (픽셀 좌표 -> 정규화 좌표)
        // MediaPipe Rect는 left, top, right, bottom을 사용
        float bboxWidth = (bbox.right - bbox.left) / (float)imageWidth;
        float bboxHeight = (bbox.bottom - bbox.top) / (float)imageHeight;

        result.BoundingBox = new UnityEngine.Rect(
            bbox.left / (float)imageWidth,
            bbox.top / (float)imageHeight,
            bboxWidth,
            bboxHeight
        );

        // 랜드마크 추출 (MediaPipe BlazeFace는 6개 키포인트 제공)
        // 0: 오른쪽 눈, 1: 왼쪽 눈, 2: 코 끝, 3: 입 중앙, 4: 오른쪽 귀, 5: 왼쪽 귀
        if (bestDetection.keypoints != null && bestDetection.keypoints.Count >= 4)
        {
            result.RightEye = new Vector2(bestDetection.keypoints[0].x, bestDetection.keypoints[0].y);
            result.LeftEye = new Vector2(bestDetection.keypoints[1].x, bestDetection.keypoints[1].y);
            result.Nose = new Vector2(bestDetection.keypoints[2].x, bestDetection.keypoints[2].y);

            // 입 중앙에서 좌우 추정
            var mouthCenter = bestDetection.keypoints[3];
            float mouthOffset = result.BoundingBox.width * 0.15f;
            result.LeftMouth = new Vector2(mouthCenter.x - mouthOffset, mouthCenter.y);
            result.RightMouth = new Vector2(mouthCenter.x + mouthOffset, mouthCenter.y);
        }

        result.Confidence = bestScore;
        result.IsValid = true;

        return result;
    }

    /// <summary>
    /// 얼굴 영역 크롭
    /// </summary>
    public Texture2D CropFace(Texture2D source, FaceDetectionResult detection, int outputSize = 448)
    {
        if (!detection.IsValid)
        {
            return null;
        }

        // 바운딩 박스를 픽셀 좌표로 변환
        // MediaPipe: Y=0이 상단 (표준 이미지 좌표계)
        // Unity Texture2D: Y=0이 하단 → 변환 필요
        var box = detection.BoundingBox;
        int x = Mathf.FloorToInt(box.x * source.width);
        int w = Mathf.FloorToInt(box.width * source.width);
        int h = Mathf.FloorToInt(box.height * source.height);
        
        // MediaPipe Y좌표를 Unity Y좌표로 변환
        // MediaPipe에서 box.y는 상단에서의 거리, box.y + box.height는 하단
        // Unity에서는 Y=0이 하단이므로:
        // Unity Y = source.height - (MediaPipe Y + MediaPipe Height)
        int y = source.height - Mathf.FloorToInt((box.y + box.height) * source.height);

        // 마진 추가 (얼굴 주변 컨텍스트 포함)
        int margin = Mathf.RoundToInt(Mathf.Max(w, h) * 0.2f);
        x -= margin;
        y -= margin;
        w += margin * 2;
        h += margin * 2;

        // 정사각형으로 확장 (L2CS-Net 입력용)
        int size = Mathf.Max(w, h);
        int centerX = x + w / 2;
        int centerY = y + h / 2;
        x = centerX - size / 2;
        y = centerY - size / 2;

        // 경계 체크
        x = Mathf.Clamp(x, 0, source.width - 1);
        y = Mathf.Clamp(y, 0, source.height - 1);
        size = Mathf.Min(size, Mathf.Min(source.width - x, source.height - y));

        if (size <= 0)
        {
            return null;
        }

        // 크롭
        var pixels = source.GetPixels(x, y, size, size);
        var cropped = new Texture2D(size, size, TextureFormat.RGBA32, false);
        cropped.SetPixels(pixels);
        cropped.Apply();

        // 리사이즈
        if (size != outputSize)
        {
            var resized = ResizeTexture(cropped, outputSize, outputSize);
            Destroy(cropped);
            return resized;
        }

        return cropped;
    }

    /// <summary>
    /// 텍스처 리사이즈
    /// </summary>
    private Texture2D ResizeTexture(Texture2D source, int width, int height)
    {
        RenderTexture rt = RenderTexture.GetTemporary(width, height);
        Graphics.Blit(source, rt);

        RenderTexture previous = RenderTexture.active;
        RenderTexture.active = rt;

        Texture2D result = new Texture2D(width, height, TextureFormat.RGBA32, false);
        result.ReadPixels(new UnityEngine.Rect(0, 0, width, height), 0, 0);
        result.Apply();

        RenderTexture.active = previous;
        RenderTexture.ReleaseTemporary(rt);

        return result;
    }

    /// <summary>
    /// 텍스처 상하 반전
    /// </summary>
    private Texture2D FlipTextureVertical(Texture2D source)
    {
        int width = source.width;
        int height = source.height;
        var flipped = new Texture2D(width, height, source.format, false);
        
        for (int y = 0; y < height; y++)
        {
            var row = source.GetPixels(0, y, width, 1);
            flipped.SetPixels(0, height - 1 - y, width, 1, row);
        }
        flipped.Apply();
        return flipped;
    }

    /// <summary>
    /// 리소스 해제
    /// </summary>
    public void Cleanup()
    {
        if (_detector != null)
        {
            try
            {
                // MediaPipe FaceDetector는 MpResourceHandle을 상속하므로 Dispose가 가능할 수 있음
                // 하지만 API에 Dispose가 없다면 null 처리만
                (_detector as IDisposable)?.Dispose();
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[FaceDetector] Cleanup warning: {e.Message}");
            }
            _detector = null;
        }
        _isInitialized = false;
    }
}
