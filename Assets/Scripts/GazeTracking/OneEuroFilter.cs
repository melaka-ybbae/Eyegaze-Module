using UnityEngine;

/// <summary>
/// One Euro Filter implementation for smoothing noisy signals while maintaining responsiveness.
/// Reference: https://cristal.univ-lille.fr/~casiez/1euro/
/// </summary>
public class OneEuroFilter
{
    private float _minCutoff;
    private float _beta;
    private float _dCutoff;
    private LowPassFilter _xFilter;
    private LowPassFilter _dxFilter;
    private float _lastTime;
    private bool _initialized;

    /// <summary>
    /// Creates a new One Euro Filter.
    /// </summary>
    /// <param name="minCutoff">Minimum cutoff frequency (Hz). Lower values = more smoothing. Default: 1.0</param>
    /// <param name="beta">Speed coefficient. Higher values = less lag during fast movements. Default: 0.007</param>
    /// <param name="dCutoff">Derivative cutoff frequency. Usually kept at 1.0</param>
    public OneEuroFilter(float minCutoff = 1.0f, float beta = 0.007f, float dCutoff = 1.0f)
    {
        _minCutoff = minCutoff;
        _beta = beta;
        _dCutoff = dCutoff;
        _xFilter = new LowPassFilter();
        _dxFilter = new LowPassFilter();
        _lastTime = 0f;
        _initialized = false;
    }

    /// <summary>
    /// Filters the input value.
    /// </summary>
    /// <param name="x">Input value to filter</param>
    /// <param name="timestamp">Current timestamp in seconds (use Time.time)</param>
    /// <returns>Filtered value</returns>
    public float Filter(float x, float timestamp)
    {
        if (!_initialized)
        {
            _initialized = true;
            _lastTime = timestamp;
            _xFilter.SetAlpha(1.0f);
            _dxFilter.SetAlpha(1.0f);
            _xFilter.Filter(x);
            _dxFilter.Filter(0f);
            return x;
        }

        float dt = timestamp - _lastTime;
        if (dt <= 0f) dt = 1f / 60f; // Fallback to 60 FPS if timestamp didn't change
        _lastTime = timestamp;

        // Calculate derivative
        float dx = (x - _xFilter.LastValue) / dt;

        // Filter derivative
        float edx = _dxFilter.FilterWithAlpha(dx, Alpha(_dCutoff, dt));

        // Calculate cutoff based on derivative (adaptive filtering)
        float cutoff = _minCutoff + _beta * Mathf.Abs(edx);

        // Filter signal
        return _xFilter.FilterWithAlpha(x, Alpha(cutoff, dt));
    }

    /// <summary>
    /// Resets the filter state.
    /// </summary>
    public void Reset()
    {
        _initialized = false;
        _xFilter.Reset();
        _dxFilter.Reset();
    }

    private float Alpha(float cutoff, float dt)
    {
        float tau = 1.0f / (2.0f * Mathf.PI * cutoff);
        return 1.0f / (1.0f + tau / dt);
    }
}

/// <summary>
/// Simple low-pass filter used by One Euro Filter.
/// </summary>
public class LowPassFilter
{
    private float _lastValue;
    private float _alpha;
    private bool _initialized;

    public float LastValue => _lastValue;

    public LowPassFilter()
    {
        _initialized = false;
        _alpha = 1.0f;
    }

    public void SetAlpha(float alpha)
    {
        _alpha = Mathf.Clamp01(alpha);
    }

    public float Filter(float value)
    {
        if (!_initialized)
        {
            _initialized = true;
            _lastValue = value;
            return value;
        }

        _lastValue = _alpha * value + (1.0f - _alpha) * _lastValue;
        return _lastValue;
    }

    public float FilterWithAlpha(float value, float alpha)
    {
        SetAlpha(alpha);
        return Filter(value);
    }

    public void Reset()
    {
        _initialized = false;
    }
}

/// <summary>
/// Vector2 version of One Euro Filter for filtering 2D coordinates.
/// </summary>
public class OneEuroFilterVector2
{
    private OneEuroFilter _filterX;
    private OneEuroFilter _filterY;

    public OneEuroFilterVector2(float minCutoff = 1.0f, float beta = 0.007f, float dCutoff = 1.0f)
    {
        _filterX = new OneEuroFilter(minCutoff, beta, dCutoff);
        _filterY = new OneEuroFilter(minCutoff, beta, dCutoff);
    }

    public Vector2 Filter(Vector2 value, float timestamp)
    {
        return new Vector2(
            _filterX.Filter(value.x, timestamp),
            _filterY.Filter(value.y, timestamp)
        );
    }

    public void Reset()
    {
        _filterX.Reset();
        _filterY.Reset();
    }
}
