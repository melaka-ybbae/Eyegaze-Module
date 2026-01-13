using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 웹캠 테스트용 간단한 스크립트
/// RawImage에 직접 연결해서 테스트
/// </summary>
public class WebCamTest : MonoBehaviour
{
    [SerializeField] private RawImage _targetImage;
    private WebCamTexture _webCam;

    private void Start()
    {
        if (_targetImage == null)
        {
            // RawImage가 없으면 자동 생성
            var canvas = FindObjectOfType<Canvas>();
            if (canvas == null)
            {
                var canvasObj = new GameObject("TestCanvas");
                canvas = canvasObj.AddComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                canvasObj.AddComponent<CanvasScaler>();
            }

            var imageObj = new GameObject("WebCamImage");
            imageObj.transform.SetParent(canvas.transform, false);
            _targetImage = imageObj.AddComponent<RawImage>();

            var rect = _targetImage.rectTransform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        StartCoroutine(StartWebCam());
    }

    private System.Collections.IEnumerator StartWebCam()
    {
        Debug.Log("[WebCamTest] 웹캠 테스트 시작");

        if (WebCamTexture.devices.Length == 0)
        {
            Debug.LogError("[WebCamTest] 카메라 없음");
            yield break;
        }

        var device = WebCamTexture.devices[0];
        Debug.Log($"[WebCamTest] 카메라: {device.name}");

        _webCam = new WebCamTexture(device.name, 640, 480, 30);
        _webCam.Play();

        // 준비 대기
        float elapsed = 0f;
        while (_webCam.width <= 16 && elapsed < 5f)
        {
            elapsed += Time.deltaTime;
            yield return null;
        }

        if (_webCam.width <= 16)
        {
            Debug.LogError("[WebCamTest] 카메라 초기화 실패");
            yield break;
        }

        Debug.Log($"[WebCamTest] 카메라 준비됨: {_webCam.width}x{_webCam.height}");
        Debug.Log($"[WebCamTest] isPlaying: {_webCam.isPlaying}");

        // RawImage에 직접 할당
        _targetImage.texture = _webCam;
        _targetImage.color = Color.white;

        Debug.Log("[WebCamTest] 텍스처 할당 완료");
    }

    private void Update()
    {
        if (_webCam != null && _webCam.isPlaying)
        {
            // 매 프레임 상태 확인 (처음 몇 프레임만)
            if (Time.frameCount < 100 && Time.frameCount % 30 == 0)
            {
                Debug.Log($"[WebCamTest] Frame {Time.frameCount}: didUpdate={_webCam.didUpdateThisFrame}, size={_webCam.width}x{_webCam.height}");
            }
        }
    }

    private void OnDestroy()
    {
        if (_webCam != null)
        {
            _webCam.Stop();
            Destroy(_webCam);
        }
    }
}
