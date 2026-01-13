using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

/// <summary>
/// 시선 캘리브레이션 로직
/// 홍채 위치를 화면 좌표로 매핑하는 변환 행렬 계산
/// </summary>

[Serializable]
public class GazeCalibrator
{
    // 캘리브레이션 포인트 (9점)
    private List<Vector2> _targetPoints = new List<Vector2>();
    private List<Vector2> _irisPoints = new List<Vector2>();

    // 변환 계수 (다항식 회귀)
    private float[] _coefficientsX = new float[6]; // a0 + a1*x + a2*y + a3*x^2 + a4*xy + a5*y^2
    private float[] _coefficientsY = new float[6];

    private bool _isCalibrated = false;
    public bool IsCalibrated => _isCalibrated;

    // 실시간 오프셋 보정
    private Vector2 _offsetCorrection = Vector2.zero;
    private bool _useOffsetCorrection = false;

    /// <summary>
    /// 현재 오프셋 보정값
    /// </summary>
    public Vector2 OffsetCorrection => _offsetCorrection;

    /// <summary>
    /// 수집된 샘플 수
    /// </summary>
    public int SampleCount => _targetPoints.Count;

    /// <summary>
    /// 캘리브레이션 초기화
    /// </summary>
    public void Reset()
    {
        _targetPoints.Clear();
        _irisPoints.Clear();
        _isCalibrated = false;
        Array.Clear(_coefficientsX, 0, _coefficientsX.Length);
        Array.Clear(_coefficientsY, 0, _coefficientsY.Length);
        _offsetCorrection = Vector2.zero;
        _useOffsetCorrection = false;
    }

    /// <summary>
    /// 캘리브레이션 샘플 추가
    /// </summary>
    /// <param name="targetPosition">화면상의 타겟 위치 (0~1)</param>
    /// <param name="irisPosition">홍채 위치</param>
    public void AddSample(Vector2 targetPosition, Vector2 irisPosition)
    {
        _targetPoints.Add(targetPosition);
        _irisPoints.Add(irisPosition);
        
        // 디버그: 샘플 추가 확인
        if (_targetPoints.Count % 10 == 0)
        {
            Debug.Log($"[GazeCalibrator] 샘플 {_targetPoints.Count}개 수집됨 - 타겟: {targetPosition}, 홍채: {irisPosition}");
        }
    }

    /// <summary>
    /// 캘리브레이션 계산 (다항식 회귀)
    /// </summary>
    public void ComputeCalibration()
    {
        if (_targetPoints.Count < 4)
        {
            Debug.LogWarning($"[GazeCalibrator] 캘리브레이션을 위해 최소 4개의 포인트가 필요합니다. (현재: {_targetPoints.Count})");
            _isCalibrated = false;
            return;
        }

        try
        {
            // 단순화된 다항식 회귀 (최소자승법)
            ComputePolynomialRegression();
            _isCalibrated = true;
            
            Debug.Log($"[GazeCalibrator] 캘리브레이션 완료 - {_targetPoints.Count}개 포인트 사용");
            Debug.Log($"[GazeCalibrator] X 계수: [{string.Join(", ", _coefficientsX.Select(c => c.ToString("F4")))}]");
            Debug.Log($"[GazeCalibrator] Y 계수: [{string.Join(", ", _coefficientsY.Select(c => c.ToString("F4")))}]");
            
            // 캘리브레이션 품질 계산
            float quality = GetQualityScore();
            Debug.Log($"[GazeCalibrator] 캘리브레이션 품질: {quality:P0}");
        }
        catch (Exception e)
        {
            Debug.LogError($"[GazeCalibrator] 캘리브레이션 실패: {e.Message}");
            _isCalibrated = false;
        }
    }

    /// <summary>
    /// 다항식 회귀 계산
    /// </summary>
    private void ComputePolynomialRegression()
    {
        int n = _targetPoints.Count;

        // 간단한 선형 회귀로 시작 (2차 다항식은 포인트가 충분할 때만)
        if (n < 6)
        {
            ComputeLinearRegression();
            return;
        }

        // 2차 다항식 회귀: f(x,y) = a0 + a1*x + a2*y + a3*x^2 + a4*xy + a5*y^2
        // 정규방정식을 통한 최소자승법

        double[,] A = new double[6, 6];
        double[] bX = new double[6];
        double[] bY = new double[6];

        for (int i = 0; i < n; i++)
        {
            float ix = _irisPoints[i].x;
            float iy = _irisPoints[i].y;
            float tx = _targetPoints[i].x;
            float ty = _targetPoints[i].y;

            double[] terms = { 1, ix, iy, ix * ix, ix * iy, iy * iy };

            for (int j = 0; j < 6; j++)
            {
                for (int k = 0; k < 6; k++)
                {
                    A[j, k] += terms[j] * terms[k];
                }
                bX[j] += terms[j] * tx;
                bY[j] += terms[j] * ty;
            }
        }

        // 가우스 소거법으로 해 구하기
        _coefficientsX = SolveLinearSystem(A, bX);

        // A 행렬 재구성 (가우스 소거로 변형되었으므로)
        A = new double[6, 6];
        for (int i = 0; i < n; i++)
        {
            float ix = _irisPoints[i].x;
            float iy = _irisPoints[i].y;
            double[] terms = { 1, ix, iy, ix * ix, ix * iy, iy * iy };

            for (int j = 0; j < 6; j++)
            {
                for (int k = 0; k < 6; k++)
                {
                    A[j, k] += terms[j] * terms[k];
                }
            }
        }
        _coefficientsY = SolveLinearSystem(A, bY);
    }

    /// <summary>
    /// 선형 회귀 (포인트가 적을 때)
    /// </summary>
    private void ComputeLinearRegression()
    {
        int n = _targetPoints.Count;

        // f(x,y) = a0 + a1*x + a2*y
        double[,] A = new double[3, 3];
        double[] bX = new double[3];
        double[] bY = new double[3];

        for (int i = 0; i < n; i++)
        {
            float ix = _irisPoints[i].x;
            float iy = _irisPoints[i].y;
            float tx = _targetPoints[i].x;
            float ty = _targetPoints[i].y;

            double[] terms = { 1, ix, iy };

            for (int j = 0; j < 3; j++)
            {
                for (int k = 0; k < 3; k++)
                {
                    A[j, k] += terms[j] * terms[k];
                }
                bX[j] += terms[j] * tx;
                bY[j] += terms[j] * ty;
            }
        }

        float[] linearX = SolveLinearSystem3x3(A, bX);
        float[] linearY = SolveLinearSystem3x3(A, bY);

        // 선형 계수를 전체 배열에 복사 (나머지는 0)
        _coefficientsX[0] = linearX[0];
        _coefficientsX[1] = linearX[1];
        _coefficientsX[2] = linearX[2];

        _coefficientsY[0] = linearY[0];
        _coefficientsY[1] = linearY[1];
        _coefficientsY[2] = linearY[2];
    }

    /// <summary>
    /// 가우스 소거법으로 선형시스템 풀기
    /// </summary>
    private float[] SolveLinearSystem(double[,] A, double[] b)
    {
        int n = b.Length;
        float[] x = new float[n];

        // Forward elimination
        for (int i = 0; i < n; i++)
        {
            // Find pivot
            int maxRow = i;
            for (int k = i + 1; k < n; k++)
            {
                if (Math.Abs(A[k, i]) > Math.Abs(A[maxRow, i]))
                    maxRow = k;
            }

            // Swap rows
            for (int k = i; k < n; k++)
            {
                double tmp = A[maxRow, k];
                A[maxRow, k] = A[i, k];
                A[i, k] = tmp;
            }
            double tmpB = b[maxRow];
            b[maxRow] = b[i];
            b[i] = tmpB;

            // Eliminate
            for (int k = i + 1; k < n; k++)
            {
                if (Math.Abs(A[i, i]) < 1e-10) continue;
                double factor = A[k, i] / A[i, i];
                for (int j = i; j < n; j++)
                {
                    A[k, j] -= factor * A[i, j];
                }
                b[k] -= factor * b[i];
            }
        }

        // Back substitution
        for (int i = n - 1; i >= 0; i--)
        {
            double sum = b[i];
            for (int j = i + 1; j < n; j++)
            {
                sum -= A[i, j] * x[j];
            }
            x[i] = Math.Abs(A[i, i]) > 1e-10 ? (float)(sum / A[i, i]) : 0;
        }

        return x;
    }

    /// <summary>
    /// 3x3 선형시스템 풀기
    /// </summary>
    private float[] SolveLinearSystem3x3(double[,] A, double[] b)
    {
        double[,] fullA = new double[3, 3];
        for (int i = 0; i < 3; i++)
            for (int j = 0; j < 3; j++)
                fullA[i, j] = A[i, j];

        double[] fullB = new double[3];
        Array.Copy(b, fullB, 3);

        float[] result6 = SolveLinearSystem(fullA, fullB);
        float[] result3 = new float[3];
        Array.Copy(result6, result3, 3);
        return result3;
    }

    /// <summary>
    /// 홍채 위치를 화면 좌표로 변환
    /// </summary>
    public Vector2 MapToScreen(Vector2 irisPosition)
    {
        if (!_isCalibrated)
        {
            return irisPosition;
        }

        float x = irisPosition.x;
        float y = irisPosition.y;

        float screenX = _coefficientsX[0]
            + _coefficientsX[1] * x
            + _coefficientsX[2] * y
            + _coefficientsX[3] * x * x
            + _coefficientsX[4] * x * y
            + _coefficientsX[5] * y * y;

        float screenY = _coefficientsY[0]
            + _coefficientsY[1] * x
            + _coefficientsY[2] * y
            + _coefficientsY[3] * x * x
            + _coefficientsY[4] * x * y
            + _coefficientsY[5] * y * y;

        // 오프셋 보정 적용
        if (_useOffsetCorrection)
        {
            screenX += _offsetCorrection.x;
            screenY += _offsetCorrection.y;
        }

        return new Vector2(
            Mathf.Clamp01(screenX),
            Mathf.Clamp01(screenY)
        );
    }

    /// <summary>
    /// 실시간 오프셋 보정 설정
    /// 사용자가 특정 지점을 바라볼 때 현재 시선 위치와의 차이를 보정합니다.
    /// </summary>
    /// <param name="targetPosition">사용자가 바라보고 있는 화면 위치 (0~1)</param>
    /// <param name="currentGazePosition">현재 시선 추적 결과 (0~1)</param>
    public void SetOffsetCorrection(Vector2 targetPosition, Vector2 currentGazePosition)
    {
        _offsetCorrection = targetPosition - currentGazePosition;
        _useOffsetCorrection = true;
        Debug.Log($"[GazeCalibrator] 오프셋 보정 설정: {_offsetCorrection}");
    }

    /// <summary>
    /// 점진적 오프셋 보정 (부드러운 보정)
    /// </summary>
    /// <param name="targetPosition">사용자가 바라보고 있는 화면 위치 (0~1)</param>
    /// <param name="currentGazePosition">현재 시선 추적 결과 (0~1)</param>
    /// <param name="correctionSpeed">보정 속도 (0~1, 기본값 0.1)</param>
    public void ApplyGradualOffsetCorrection(Vector2 targetPosition, Vector2 currentGazePosition, float correctionSpeed = 0.1f)
    {
        Vector2 newOffset = targetPosition - currentGazePosition;

        if (!_useOffsetCorrection)
        {
            _offsetCorrection = newOffset;
            _useOffsetCorrection = true;
        }
        else
        {
            // 기존 오프셋과 새 오프셋을 보간
            _offsetCorrection = Vector2.Lerp(_offsetCorrection, newOffset, correctionSpeed);
        }
    }

    /// <summary>
    /// 오프셋 보정 초기화
    /// </summary>
    public void ResetOffsetCorrection()
    {
        _offsetCorrection = Vector2.zero;
        _useOffsetCorrection = false;
        Debug.Log("[GazeCalibrator] 오프셋 보정 초기화");
    }

    /// <summary>
    /// 오프셋 보정 활성화/비활성화
    /// </summary>
    public void EnableOffsetCorrection(bool enable)
    {
        _useOffsetCorrection = enable;
    }

    /// <summary>
    /// 캘리브레이션 품질 점수 (0~1)
    /// </summary>
    public float GetQualityScore()
    {
        if (!_isCalibrated || _targetPoints.Count == 0)
            return 0f;

        float totalError = 0f;
        float maxError = 0f;
        
        for (int i = 0; i < _targetPoints.Count; i++)
        {
            Vector2 mapped = MapToScreen(_irisPoints[i]);
            float error = Vector2.Distance(mapped, _targetPoints[i]);
            totalError += error;
            maxError = Mathf.Max(maxError, error);
            
            // 처음 몇 개 샘플의 매핑 결과 출력
            if (i < 5)
            {
                Debug.Log($"[GazeCalibrator] 샘플 {i}: 홍채({_irisPoints[i].x:F3}, {_irisPoints[i].y:F3}) → 매핑({mapped.x:F3}, {mapped.y:F3}) vs 타겟({_targetPoints[i].x:F3}, {_targetPoints[i].y:F3}), 에러: {error:F3}");
            }
        }

        float avgError = totalError / _targetPoints.Count;
        Debug.Log($"[GazeCalibrator] 평균 에러: {avgError:F4}, 최대 에러: {maxError:F4}");
        
        // 평균 에러가 0.3 이하면 좋은 품질 (기준 완화)
        // 0.3 에러 = 0% 품질, 0 에러 = 100% 품질
        return Mathf.Clamp01(1f - avgError / 0.3f);
    }
}
