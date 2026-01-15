using System;
using UnityEngine;
using Unity.Sentis;

/// <summary>
/// L2CS-Net 기반 시선 추정기
/// 얼굴 이미지에서 pitch, yaw 시선 각도를 추정합니다.
/// </summary>
public class GazeEstimator : MonoBehaviour
{
    [Header("Model")]
    [SerializeField] private ModelAsset _modelAsset;

    [Header("L2CS-Net Settings")]
    [SerializeField] private int _numBins = 90;  // L2CS-Net 각도 bin 개수
    [SerializeField] private float _binWidth = 4f;  // 각 bin당 각도 (degrees)

    private Model _runtimeModel;
    private Worker _worker;
    private bool _isInitialized = false;
    private bool _outputFormatLogged = false;
    private int _frameCount = 0;

    // 시선 추정 결과
    public struct GazeEstimationResult
    {
        public float Pitch;      // 상하 시선 각도 (라디안)
        public float Yaw;        // 좌우 시선 각도 (라디안)
        public float PitchDeg;   // 상하 시선 각도 (도)
        public float YawDeg;     // 좌우 시선 각도 (도)
        public Vector3 GazeVector;  // 시선 방향 벡터
        public bool IsValid;
    }

    public event Action<GazeEstimationResult> OnGazeEstimated;

    public bool IsInitialized => _isInitialized;

    private void Awake()
    {
        Initialize();
    }

    private void OnDestroy()
    {
        Dispose();
    }

    /// <summary>
    /// 모델 초기화
    /// </summary>
    public void Initialize()
    {
        if (_modelAsset == null)
        {
            Debug.LogWarning("[GazeEstimator] 모델이 할당되지 않았습니다. Inspector에서 L2CS-Net ONNX 모델을 할당하세요.");
            return;
        }

        try
        {
            _runtimeModel = ModelLoader.Load(_modelAsset);

            // GPU 사용 가능 시 GPU, 아니면 CPU 사용
            BackendType backend = SystemInfo.supportsComputeShaders ? BackendType.GPUCompute : BackendType.CPU;
            _worker = new Worker(_runtimeModel, backend);
            _isInitialized = true;
            Debug.Log($"[GazeEstimator] 초기화 완료 ({backend})");
        }
        catch (Exception e)
        {
            Debug.LogError($"[GazeEstimator] 초기화 실패: {e.Message}");
            _isInitialized = false;
        }
    }

    /// <summary>
    /// 시선 추정 수행
    /// </summary>
    /// <param name="faceImage">크롭된 얼굴 이미지 (448x448 권장)</param>
    /// <returns>시선 추정 결과</returns>
    public GazeEstimationResult Estimate(Texture2D faceImage)
    {
        var result = new GazeEstimationResult { IsValid = false };

        if (!_isInitialized)
        {
            Debug.LogWarning("[GazeEstimator] 모델이 초기화되지 않았습니다.");
            return result;
        }

        if (faceImage == null)
        {
            Debug.LogWarning("[GazeEstimator] 입력 이미지가 null입니다.");
            return result;
        }

        _frameCount++;

        try
        {
            using var inputTensor = Preprocess(faceImage);
            _worker.Schedule(inputTensor);
            result = ParseOutput();

            if (result.IsValid)
            {
                OnGazeEstimated?.Invoke(result);
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"[GazeEstimator] 추론 실패: {e.Message}\n{e.StackTrace}");
        }

        return result;
    }

    /// <summary>
    /// 입력 이미지 전처리
    /// Sentis는 NCHW 형식 사용: [1, 3, height, width]
    /// </summary>
    private Tensor<float> Preprocess(Texture2D texture)
    {
        int width = texture.width;
        int height = texture.height;

        var pixels = texture.GetPixels32();
        float[] data = new float[3 * height * width];

        // ImageNet 정규화 파라미터
        float[] mean = { 0.485f, 0.456f, 0.406f };
        float[] std = { 0.229f, 0.224f, 0.225f };

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                // Y축 반전
                int srcIdx = (height - 1 - y) * width + x;
                var pixel = pixels[srcIdx];

                // NCHW 형식: [channel, y, x]
                int dstIdxR = 0 * height * width + y * width + x;
                int dstIdxG = 1 * height * width + y * width + x;
                int dstIdxB = 2 * height * width + y * width + x;

                // RGB 정규화
                data[dstIdxR] = (pixel.r / 255f - mean[0]) / std[0];
                data[dstIdxG] = (pixel.g / 255f - mean[1]) / std[1];
                data[dstIdxB] = (pixel.b / 255f - mean[2]) / std[2];
            }
        }

        // Sentis Tensor: (batch, channels, height, width)
        return new Tensor<float>(new TensorShape(1, 3, height, width), data);
    }

    /// <summary>
    /// 모델 출력 파싱
    /// </summary>
    private GazeEstimationResult ParseOutput()
    {
        var result = new GazeEstimationResult { IsValid = false };

        try
        {
            var outputNames = _runtimeModel.outputs;

            if (outputNames.Count >= 2)
            {
                // 두 개의 출력이 있는 경우 (L2CS-Net 스타일)
                var pitchTensorRaw = _worker.PeekOutput(outputNames[0].name) as Tensor<float>;
                var yawTensorRaw = _worker.PeekOutput(outputNames[1].name) as Tensor<float>;

                if (pitchTensorRaw != null && yawTensorRaw != null)
                {
                    using var pitchCpu = pitchTensorRaw.ReadbackAndClone();
                    using var yawCpu = yawTensorRaw.ReadbackAndClone();
                    _outputFormatLogged = true;

                    float pitch, yaw;
                    int pitchLength = pitchCpu.shape.length;

                    // Binned 출력인 경우 (90개 bin)
                    if (pitchLength == _numBins)
                    {
                        pitch = BinnedToAngle(pitchCpu);
                        yaw = BinnedToAngle(yawCpu);
                    }
                    // 직접 각도 출력인 경우 (1개 값)
                    else if (pitchLength == 1)
                    {
                        pitch = pitchCpu[0];
                        yaw = yawCpu[0];

                        // 값 범위에 따라 degrees/radians 판단
                        if (Mathf.Abs(pitch) > 3.15f || Mathf.Abs(yaw) > 3.15f)
                        {
                            result.PitchDeg = pitch;
                            result.YawDeg = yaw;
                            result.Pitch = pitch * Mathf.Deg2Rad;
                            result.Yaw = yaw * Mathf.Deg2Rad;
                            result.GazeVector = AnglesToVector(result.Pitch, result.Yaw);
                            result.IsValid = true;
                            return result;
                        }
                        else
                        {
                            pitch *= Mathf.Rad2Deg;
                            yaw *= Mathf.Rad2Deg;
                        }
                    }
                    else
                    {
                        return result;
                    }

                    result.PitchDeg = pitch;
                    result.YawDeg = yaw;
                    result.Pitch = pitch * Mathf.Deg2Rad;
                    result.Yaw = yaw * Mathf.Deg2Rad;
                    result.GazeVector = AnglesToVector(result.Pitch, result.Yaw);
                    result.IsValid = true;
                }
            }
            else
            {
                // 단일 출력인 경우
                var singleOutputRaw = _worker.PeekOutput() as Tensor<float>;

                if (singleOutputRaw == null)
                    return result;

                using var singleOutput = singleOutputRaw.ReadbackAndClone();
                int outputLength = singleOutput.shape.length;
                _outputFormatLogged = true;

                // 출력이 2개 값 (pitch, yaw)인 경우 - 라디안 출력
                if (outputLength == 2)
                {
                    // 모델 출력은 항상 라디안
                    float pitch = singleOutput[0];
                    float yaw = singleOutput[1];

                    result.Pitch = pitch;
                    result.Yaw = yaw;
                    result.PitchDeg = pitch * Mathf.Rad2Deg;
                    result.YawDeg = yaw * Mathf.Rad2Deg;
                    result.GazeVector = AnglesToVector(result.Pitch, result.Yaw);
                    result.IsValid = true;
                }
                // 출력이 180개 (binned * 2)인 경우
                else if (outputLength == 180)
                {
                    float[] pitchData = new float[_numBins];
                    float[] yawData = new float[_numBins];
                    for (int i = 0; i < _numBins; i++)
                    {
                        pitchData[i] = singleOutput[i];
                        yawData[i] = singleOutput[_numBins + i];
                    }

                    using var pitchTensor = new Tensor<float>(new TensorShape(1, _numBins), pitchData);
                    using var yawTensor = new Tensor<float>(new TensorShape(1, _numBins), yawData);

                    float pitch = BinnedToAngle(pitchTensor);
                    float yaw = BinnedToAngle(yawTensor);

                    result.PitchDeg = pitch;
                    result.YawDeg = yaw;
                    result.Pitch = pitch * Mathf.Deg2Rad;
                    result.Yaw = yaw * Mathf.Deg2Rad;
                    result.GazeVector = AnglesToVector(result.Pitch, result.Yaw);
                    result.IsValid = true;
                }
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"[GazeEstimator] 출력 파싱 실패: {e.Message}\n{e.StackTrace}");
        }

        return result;
    }

    /// <summary>
    /// Binned classification 출력을 continuous angle로 변환
    /// L2CS-Net 방식: softmax 적용 후 기댓값 계산
    /// </summary>
    private float BinnedToAngle(Tensor<float> binnedOutput)
    {
        // Softmax 적용
        float[] probs = new float[_numBins];
        float maxVal = float.MinValue;

        for (int i = 0; i < _numBins; i++)
        {
            maxVal = Mathf.Max(maxVal, binnedOutput[i]);
        }

        float sumExp = 0f;
        for (int i = 0; i < _numBins; i++)
        {
            probs[i] = Mathf.Exp(binnedOutput[i] - maxVal);
            sumExp += probs[i];
        }

        for (int i = 0; i < _numBins; i++)
        {
            probs[i] /= sumExp;
        }

        // 기댓값 계산 (각 bin의 중심 각도 * 확률)
        float expectedAngle = 0f;
        float angleOffset = -(_numBins / 2f) * _binWidth;

        for (int i = 0; i < _numBins; i++)
        {
            float binCenter = angleOffset + i * _binWidth + _binWidth / 2f;
            expectedAngle += binCenter * probs[i];
        }

        return expectedAngle;
    }

    /// <summary>
    /// pitch, yaw 각도를 시선 방향 벡터로 변환
    /// </summary>
    private Vector3 AnglesToVector(float pitch, float yaw)
    {
        return new Vector3(
            Mathf.Sin(yaw) * Mathf.Cos(pitch),
            -Mathf.Sin(pitch),
            -Mathf.Cos(yaw) * Mathf.Cos(pitch)
        ).normalized;
    }

    /// <summary>
    /// 리소스 해제
    /// </summary>
    public void Dispose()
    {
        _worker?.Dispose();
        _worker = null;
        _runtimeModel = null;
        _isInitialized = false;
    }
}
