using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

/// <summary>
/// 2차 다항식 회귀를 사용한 시선-화면 좌표 매핑
/// Python calibration 모듈과 동일한 알고리즘
/// </summary>
public class PolynomialMapper
{
    // 캘리브레이션 데이터
    private float[] _mean;      // [pitch_mean, yaw_mean]
    private float[] _std;       // [pitch_std, yaw_std]
    private float[] _coefX;     // X좌표 회귀 계수 (6개 for degree=2)
    private float[] _coefY;     // Y좌표 회귀 계수 (6개 for degree=2)
    private float _interceptX;
    private float _interceptY;

    private bool _isCalibrated = false;
    private int _degree = 2;
    private float _alpha = 0.1f;  // Ridge 정규화 계수

    public bool IsCalibrated => _isCalibrated;

    /// <summary>
    /// 캘리브레이션 데이터로 매퍼 피팅
    /// </summary>
    /// <param name="gazePoints">시선 데이터 (pitch, yaw) 리스트</param>
    /// <param name="screenPoints">화면 좌표 리스트</param>
    public void Fit(List<Vector2> gazePoints, List<Vector2> screenPoints)
    {
        if (gazePoints.Count != screenPoints.Count || gazePoints.Count < 6)
        {
            Debug.LogError("[PolynomialMapper] 최소 6개 이상의 캘리브레이션 포인트가 필요합니다.");
            return;
        }

        int n = gazePoints.Count;

        // 1. 정규화 파라미터 계산
        float pitchSum = 0, yawSum = 0;
        foreach (var gaze in gazePoints)
        {
            pitchSum += gaze.x;
            yawSum += gaze.y;
        }
        float pitchMean = pitchSum / n;
        float yawMean = yawSum / n;

        float pitchVar = 0, yawVar = 0;
        foreach (var gaze in gazePoints)
        {
            pitchVar += Mathf.Pow(gaze.x - pitchMean, 2);
            yawVar += Mathf.Pow(gaze.y - yawMean, 2);
        }
        float pitchStd = Mathf.Sqrt(pitchVar / n) + 1e-8f;
        float yawStd = Mathf.Sqrt(yawVar / n) + 1e-8f;

        _mean = new float[] { pitchMean, yawMean };
        _std = new float[] { pitchStd, yawStd };

        // 2. 다항식 특성 행렬 생성 (N x 6)
        // [1, p, y, p², p*y, y²]
        int numFeatures = 6;
        float[,] X = new float[n, numFeatures];
        float[] targetX = new float[n];
        float[] targetY = new float[n];

        for (int i = 0; i < n; i++)
        {
            float p = (gazePoints[i].x - pitchMean) / pitchStd;
            float y = (gazePoints[i].y - yawMean) / yawStd;

            X[i, 0] = 1;        // bias
            X[i, 1] = p;        // pitch
            X[i, 2] = y;        // yaw
            X[i, 3] = p * p;    // pitch²
            X[i, 4] = p * y;    // pitch * yaw
            X[i, 5] = y * y;    // yaw²

            targetX[i] = screenPoints[i].x;
            targetY[i] = screenPoints[i].y;
        }

        // 3. Ridge 회귀 수행: (X^T X + alpha*I)^-1 X^T y
        _coefX = RidgeRegression(X, targetX, n, numFeatures, _alpha);
        _coefY = RidgeRegression(X, targetY, n, numFeatures, _alpha);

        // intercept는 coef[0]에 포함됨 (bias term)
        _interceptX = 0;
        _interceptY = 0;

        _isCalibrated = true;

        Debug.Log($"[PolynomialMapper] 캘리브레이션 완료 - {n}개 포인트, mean=({pitchMean:F4}, {yawMean:F4}), std=({pitchStd:F4}, {yawStd:F4})");
    }

    /// <summary>
    /// Ridge 회귀: (X^T X + alpha*I)^-1 X^T y
    /// </summary>
    private float[] RidgeRegression(float[,] X, float[] y, int n, int m, float alpha)
    {
        // X^T X (m x m)
        float[,] XtX = new float[m, m];
        for (int i = 0; i < m; i++)
        {
            for (int j = 0; j < m; j++)
            {
                float sum = 0;
                for (int k = 0; k < n; k++)
                {
                    sum += X[k, i] * X[k, j];
                }
                XtX[i, j] = sum;
                // Ridge: 대각선에 alpha 추가 (bias term 제외)
                if (i == j && i > 0)
                {
                    XtX[i, j] += alpha;
                }
            }
        }

        // X^T y (m x 1)
        float[] Xty = new float[m];
        for (int i = 0; i < m; i++)
        {
            float sum = 0;
            for (int k = 0; k < n; k++)
            {
                sum += X[k, i] * y[k];
            }
            Xty[i] = sum;
        }

        // (X^T X + alpha*I)^-1 계산 - Gauss-Jordan 소거법
        float[,] augmented = new float[m, m + 1];
        for (int i = 0; i < m; i++)
        {
            for (int j = 0; j < m; j++)
            {
                augmented[i, j] = XtX[i, j];
            }
            augmented[i, m] = Xty[i];
        }

        // Gauss-Jordan 소거
        for (int col = 0; col < m; col++)
        {
            // 피벗 찾기
            int maxRow = col;
            for (int row = col + 1; row < m; row++)
            {
                if (Mathf.Abs(augmented[row, col]) > Mathf.Abs(augmented[maxRow, col]))
                {
                    maxRow = row;
                }
            }

            // 행 교환
            for (int j = 0; j <= m; j++)
            {
                float temp = augmented[col, j];
                augmented[col, j] = augmented[maxRow, j];
                augmented[maxRow, j] = temp;
            }

            // 피벗이 0에 가까우면 스킵
            if (Mathf.Abs(augmented[col, col]) < 1e-10f)
            {
                continue;
            }

            // 피벗 행 정규화
            float pivot = augmented[col, col];
            for (int j = 0; j <= m; j++)
            {
                augmented[col, j] /= pivot;
            }

            // 다른 행 소거
            for (int row = 0; row < m; row++)
            {
                if (row != col)
                {
                    float factor = augmented[row, col];
                    for (int j = 0; j <= m; j++)
                    {
                        augmented[row, j] -= factor * augmented[col, j];
                    }
                }
            }
        }

        // 결과 추출
        float[] result = new float[m];
        for (int i = 0; i < m; i++)
        {
            result[i] = augmented[i, m];
        }

        return result;
    }

    /// <summary>
    /// 시선 각도를 화면 좌표로 변환
    /// </summary>
    /// <param name="pitch">Pitch 각도 (라디안)</param>
    /// <param name="yaw">Yaw 각도 (라디안)</param>
    /// <returns>화면 좌표 (픽셀)</returns>
    public Vector2 Predict(float pitch, float yaw)
    {
        if (!_isCalibrated)
        {
            Debug.LogWarning("[PolynomialMapper] 캘리브레이션이 필요합니다.");
            return new Vector2(Screen.width / 2f, Screen.height / 2f);
        }

        // 1. 정규화
        float pitchNorm = (pitch - _mean[0]) / _std[0];
        float yawNorm = (yaw - _mean[1]) / _std[1];

        // 2. 다항식 특성 생성 (degree=2)
        float[] features = new float[]
        {
            1.0f,                       // bias
            pitchNorm,                  // pitch
            yawNorm,                    // yaw
            pitchNorm * pitchNorm,      // pitch²
            pitchNorm * yawNorm,        // pitch * yaw
            yawNorm * yawNorm           // yaw²
        };

        // 3. 예측: dot(features, coef) + intercept
        float screenX = _interceptX;
        float screenY = _interceptY;

        for (int i = 0; i < features.Length; i++)
        {
            screenX += features[i] * _coefX[i];
            screenY += features[i] * _coefY[i];
        }

        return new Vector2(screenX, screenY);
    }

    /// <summary>
    /// 캘리브레이션 데이터를 JSON 파일로 저장
    /// </summary>
    public void Save(string filePath)
    {
        if (!_isCalibrated)
        {
            Debug.LogError("[PolynomialMapper] 저장할 캘리브레이션 데이터가 없습니다.");
            return;
        }

        var data = new CalibrationData
        {
            type = "polynomial",
            degree = _degree,
            alpha = _alpha,
            mean = _mean,
            std = _std,
            coef_x = _coefX,
            intercept_x = _interceptX,
            coef_y = _coefY,
            intercept_y = _interceptY
        };

        string json = JsonUtility.ToJson(data, true);
        File.WriteAllText(filePath, json);
        Debug.Log($"[PolynomialMapper] 캘리브레이션 저장됨: {filePath}");
    }

    /// <summary>
    /// JSON 파일에서 캘리브레이션 데이터 로드
    /// </summary>
    public bool Load(string filePath)
    {
        if (!File.Exists(filePath))
        {
            Debug.LogWarning($"[PolynomialMapper] 파일을 찾을 수 없습니다: {filePath}");
            return false;
        }

        try
        {
            string json = File.ReadAllText(filePath);
            var data = JsonUtility.FromJson<CalibrationData>(json);

            _mean = data.mean;
            _std = data.std;
            _coefX = data.coef_x;
            _coefY = data.coef_y;
            _interceptX = data.intercept_x;
            _interceptY = data.intercept_y;
            _degree = data.degree;
            _alpha = data.alpha;
            _isCalibrated = true;

            Debug.Log($"[PolynomialMapper] 캘리브레이션 로드됨: {filePath}");
            return true;
        }
        catch (Exception e)
        {
            Debug.LogError($"[PolynomialMapper] 로드 실패: {e.Message}");
            return false;
        }
    }

    /// <summary>
    /// 캘리브레이션 초기화
    /// </summary>
    public void Reset()
    {
        _isCalibrated = false;
        _mean = null;
        _std = null;
        _coefX = null;
        _coefY = null;
        _interceptX = 0;
        _interceptY = 0;
    }

    [Serializable]
    private class CalibrationData
    {
        public string type;
        public int degree;
        public float alpha;
        public float[] mean;
        public float[] std;
        public float[] coef_x;
        public float intercept_x;
        public float[] coef_y;
        public float intercept_y;
    }
}
