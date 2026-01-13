using System;
using UnityEngine;

/// <summary>
/// 시선 데이터 구조체
/// </summary>
[Serializable]
public struct GazeData
{
    /// <summary>
    /// 데이터 수집 타임스탬프
    /// </summary>
    public float Timestamp;

    /// <summary>
    /// 화면 좌표상의 시선 위치 (0~1 정규화)
    /// </summary>
    public Vector2 ScreenPosition;

    /// <summary>
    /// 왼쪽 홍채 중심 위치 (정규화된 랜드마크 좌표)
    /// </summary>
    public Vector2 LeftIrisCenter;

    /// <summary>
    /// 오른쪽 홍채 중심 위치 (정규화된 랜드마크 좌표)
    /// </summary>
    public Vector2 RightIrisCenter;

    /// <summary>
    /// 눈 영역 내 홍채 상대 위치 (캘리브레이션용, 양안 평균)
    /// </summary>
    public Vector2 EyeRatio;

    /// <summary>
    /// 시선 추적 유효 여부
    /// </summary>
    public bool IsValid;

    /// <summary>
    /// 신뢰도 (0~1)
    /// </summary>
    public float Confidence;

    public GazeData(float timestamp, Vector2 screenPosition, Vector2 leftIris, Vector2 rightIris, Vector2 eyeRatio, bool isValid, float confidence)
    {
        Timestamp = timestamp;
        ScreenPosition = screenPosition;
        LeftIrisCenter = leftIris;
        RightIrisCenter = rightIris;
        EyeRatio = eyeRatio;
        IsValid = isValid;
        Confidence = confidence;
    }

    public static GazeData Invalid => new GazeData
    {
        Timestamp = Time.time,
        ScreenPosition = Vector2.zero,
        LeftIrisCenter = Vector2.zero,
        RightIrisCenter = Vector2.zero,
        EyeRatio = Vector2.zero,
        IsValid = false,
        Confidence = 0f
    };
}

/// <summary>
/// 캘리브레이션 포인트 데이터
/// </summary>
[Serializable]
public struct CalibrationPoint
{
    /// <summary>
    /// 화면상의 타겟 위치 (0~1 정규화)
    /// </summary>
    public Vector2 TargetPosition;

    /// <summary>
    /// 수집된 홍채 위치들의 평균
    /// </summary>
    public Vector2 AverageIrisPosition;

    /// <summary>
    /// 수집된 샘플 수
    /// </summary>
    public int SampleCount;

    public CalibrationPoint(Vector2 target)
    {
        TargetPosition = target;
        AverageIrisPosition = Vector2.zero;
        SampleCount = 0;
    }
}
