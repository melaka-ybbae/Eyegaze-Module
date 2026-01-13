using UnityEngine;

/// <summary>
/// One Euro Filter - 적응형 저역 통과 필터
/// 느린 움직임에서는 많이 스무딩, 빠른 움직임에서는 적게 스무딩
/// Reference: https://cristal.univ-lille.fr/~casiez/1euro/
/// </summary>
public class GazeFilter
{
    private OneEuroFilter2D _gazeFilter;
    private OneEuroFilter2D _angleFilter;

    public GazeFilter(float minCutoff = 1.0f, float beta = 0.007f, float dCutoff = 1.0f)
    {
        _gazeFilter = new OneEuroFilter2D(minCutoff, beta, dCutoff);
        _angleFilter = new OneEuroFilter2D(minCutoff, beta, dCutoff);
    }

    /// <summary>
    /// 화면 좌표 필터링
    /// </summary>
    public Vector2 FilterScreenPosition(Vector2 position, float rate)
    {
        return _gazeFilter.Filter(position, rate);
    }

    /// <summary>
    /// 시선 각도 필터링
    /// </summary>
    public Vector2 FilterAngles(float pitch, float yaw, float rate)
    {
        return _angleFilter.Filter(new Vector2(pitch, yaw), rate);
    }

    /// <summary>
    /// 필터 리셋
    /// </summary>
    public void Reset()
    {
        _gazeFilter.Reset();
        _angleFilter.Reset();
    }
}

/// <summary>
/// One Euro Filter (1D)
/// </summary>
public class OneEuroFilter1D
{
    private float _minCutoff;
    private float _beta;
    private float _dCutoff;
    private LowPassFilter1D _xFilter;
    private LowPassFilter1D _dxFilter;
    private float _lastValue;
    private float _lastTime;
    private bool _firstTime;

    public OneEuroFilter1D(float minCutoff = 1.0f, float beta = 0.007f, float dCutoff = 1.0f)
    {
        _minCutoff = minCutoff;
        _beta = beta;
        _dCutoff = dCutoff;
        _xFilter = new LowPassFilter1D();
        _dxFilter = new LowPassFilter1D();
        _firstTime = true;
    }

    public float Filter(float x, float rate)
    {
        if (_firstTime)
        {
            _firstTime = false;
            _lastValue = x;
            return x;
        }

        // 속도(변화율) 계산
        float dx = (x - _lastValue) * rate;
        _lastValue = x;

        // 속도 필터링
        float edx = _dxFilter.Filter(dx, Alpha(rate, _dCutoff));

        // 적응형 cutoff 계산
        float cutoff = _minCutoff + _beta * Mathf.Abs(edx);

        // 값 필터링
        return _xFilter.Filter(x, Alpha(rate, cutoff));
    }

    public void Reset()
    {
        _xFilter.Reset();
        _dxFilter.Reset();
        _firstTime = true;
    }

    private float Alpha(float rate, float cutoff)
    {
        float tau = 1.0f / (2.0f * Mathf.PI * cutoff);
        float te = 1.0f / rate;
        return 1.0f / (1.0f + tau / te);
    }
}

/// <summary>
/// One Euro Filter (2D)
/// </summary>
public class OneEuroFilter2D
{
    private OneEuroFilter1D _xFilter;
    private OneEuroFilter1D _yFilter;

    public OneEuroFilter2D(float minCutoff = 1.0f, float beta = 0.007f, float dCutoff = 1.0f)
    {
        _xFilter = new OneEuroFilter1D(minCutoff, beta, dCutoff);
        _yFilter = new OneEuroFilter1D(minCutoff, beta, dCutoff);
    }

    public Vector2 Filter(Vector2 value, float rate)
    {
        return new Vector2(
            _xFilter.Filter(value.x, rate),
            _yFilter.Filter(value.y, rate)
        );
    }

    public void Reset()
    {
        _xFilter.Reset();
        _yFilter.Reset();
    }
}

/// <summary>
/// Low Pass Filter (1D)
/// </summary>
public class LowPassFilter1D
{
    private float _lastValue;
    private bool _firstTime;

    public LowPassFilter1D()
    {
        _firstTime = true;
    }

    public float Filter(float x, float alpha)
    {
        if (_firstTime)
        {
            _firstTime = false;
            _lastValue = x;
            return x;
        }

        _lastValue = alpha * x + (1.0f - alpha) * _lastValue;
        return _lastValue;
    }

    public void Reset()
    {
        _firstTime = true;
    }
}

/// <summary>
/// 이동 평균 필터 (간단한 스무딩용)
/// </summary>
public class MovingAverageFilter
{
    private Vector2[] _buffer;
    private int _index;
    private int _count;
    private Vector2 _sum;

    public MovingAverageFilter(int windowSize = 5)
    {
        _buffer = new Vector2[windowSize];
        _index = 0;
        _count = 0;
        _sum = Vector2.zero;
    }

    public Vector2 Filter(Vector2 value)
    {
        // 오래된 값 빼기
        if (_count >= _buffer.Length)
        {
            _sum -= _buffer[_index];
        }

        // 새 값 추가
        _buffer[_index] = value;
        _sum += value;

        _index = (_index + 1) % _buffer.Length;
        _count = Mathf.Min(_count + 1, _buffer.Length);

        return _sum / _count;
    }

    public void Reset()
    {
        _index = 0;
        _count = 0;
        _sum = Vector2.zero;
        for (int i = 0; i < _buffer.Length; i++)
        {
            _buffer[i] = Vector2.zero;
        }
    }
}
