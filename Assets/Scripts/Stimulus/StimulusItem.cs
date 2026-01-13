using System;
using UnityEngine;

/// <summary>
/// 자극 유형
/// </summary>
public enum StimulusType
{
    Image,
    Video,
    Animation
}

/// <summary>
/// 개별 자극 아이템 데이터
/// </summary>
[Serializable]
public class StimulusItem
{
    [Header("Basic Info")]
    public string Id;
    public string Name;
    public StimulusType Type;

    [Header("Content")]
    public Sprite ImageContent;
    public string VideoPath;

    [Header("Display Settings")]
    public float DisplayDuration = 5f;
    public Vector2 Position = new Vector2(0.5f, 0.5f); // 정규화된 화면 좌표
    public Vector2 Size = new Vector2(0.5f, 0.5f); // 정규화된 크기

    [Header("AOI (Area of Interest)")]
    public Rect[] AreasOfInterest; // 관심 영역들

    public StimulusItem()
    {
        Id = Guid.NewGuid().ToString();
        Name = "Unnamed Stimulus";
        Type = StimulusType.Image;
        DisplayDuration = 5f;
        Position = new Vector2(0.5f, 0.5f);
        Size = new Vector2(0.5f, 0.5f);
    }

    public StimulusItem(string name, Sprite image, float duration = 5f)
    {
        Id = Guid.NewGuid().ToString();
        Name = name;
        Type = StimulusType.Image;
        ImageContent = image;
        DisplayDuration = duration;
        Position = new Vector2(0.5f, 0.5f);
        Size = new Vector2(0.5f, 0.5f);
    }

    /// <summary>
    /// 시선이 AOI 내에 있는지 확인
    /// </summary>
    public int GetAOIIndex(Vector2 gazePosition)
    {
        if (AreasOfInterest == null) return -1;

        for (int i = 0; i < AreasOfInterest.Length; i++)
        {
            if (AreasOfInterest[i].Contains(gazePosition))
            {
                return i;
            }
        }
        return -1;
    }

    /// <summary>
    /// 시선이 자극 영역 내에 있는지 확인
    /// </summary>
    public bool IsGazeOnStimulus(Vector2 gazePosition)
    {
        Rect stimulusRect = new Rect(
            Position.x - Size.x / 2f,
            Position.y - Size.y / 2f,
            Size.x,
            Size.y
        );
        return stimulusRect.Contains(gazePosition);
    }
}

/// <summary>
/// 자극 세트 (ScriptableObject)
/// </summary>
[CreateAssetMenu(fileName = "StimulusSet", menuName = "GazeTracking/Stimulus Set")]
public class StimulusSet : ScriptableObject
{
    public string SetName;
    public string Description;
    public StimulusItem[] Items;

    [Header("Set Settings")]
    public bool RandomizeOrder = false;
    public float FixationDuration = 1f; // 자극 간 fixation cross 표시 시간

    /// <summary>
    /// 랜덤화된 순서 인덱스 배열 반환
    /// </summary>
    public int[] GetRandomizedOrder()
    {
        int[] order = new int[Items.Length];
        for (int i = 0; i < order.Length; i++)
            order[i] = i;

        if (RandomizeOrder)
        {
            // Fisher-Yates shuffle
            for (int i = order.Length - 1; i > 0; i--)
            {
                int j = UnityEngine.Random.Range(0, i + 1);
                int temp = order[i];
                order[i] = order[j];
                order[j] = temp;
            }
        }

        return order;
    }
}
