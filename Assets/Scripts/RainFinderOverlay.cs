using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI;

[DisallowMultipleComponent]
public class RainFinderOverlay : MonoBehaviour
{
    [Header("OpenWeather")]
    [Tooltip("API key, bez &appid= itd.")]
    public string apiKey = "";

    [Tooltip("Nazwa warstwy – np. precipitation_new.")]
    public string layer = "precipitation_new";

    [Tooltip("Zoom tile’i. 0 = cały świat w jednym tile.")]
    public int zoom = 0;

    [Tooltip("Odświeżanie w minutach.")]
    public float refreshIntervalMinutes = 10f;

    [Header("UI")]
    [Tooltip("JEDEN RawImage jako wzorzec (child). Ten obiekt NIE może mieć tego skryptu.")]
    public RawImage overlayPrefab;

    [Tooltip("Ile warstw ma się nakładać.")]
    public int overlayCount = 5;

    [Range(0f, 1f)]
    [Tooltip("Alfa pojedynczej warstwy (5 warstw => efekt mocniejszy).")]
    public float singleLayerAlpha = 0.25f;

    [Header("UV remap (dopasowanie do mapy)")]
    [Tooltip("Skala próbkowania po X/Y na teksturze źródłowej (0..1). Np. (1, 0.75).")]
    public Vector2 uvScale = new Vector2(1f, 1f);

    [Tooltip("Przesunięcie próbkowania po X/Y (0..1). Np. (0, 0.125).")]
    public Vector2 uvOffset = new Vector2(0f, 0f);

    private readonly List<RawImage> _overlays = new List<RawImage>();
    private Texture2D _currentTexture;
    private bool _visible = false;
    private Coroutine _loop;

    // CanvasGroup blokujący widoczność całości
    private CanvasGroup _canvasGroup;

    void Awake()
    {
        if (!overlayPrefab)
        {
            Debug.LogError("[RainFinderOverlay] overlayPrefab nie ustawiony. Ustaw child RawImage w Inspectorze.");
            enabled = false;
            return;
        }

        // bezpieczeństwo — prefab nie może mieć tego samego skryptu
        if (overlayPrefab.GetComponent<RainFinderOverlay>())
        {
            Debug.LogError("[RainFinderOverlay] overlayPrefab nie może mieć RainFinderOverlay. Daj czyste RawImage.");
            enabled = false;
            return;
        }

        _overlays.Clear();
        _overlays.Add(overlayPrefab);

        // tworzymy dodatkowe warstwy
        for (int i = 1; i < overlayCount; i++)
        {
            var cloneGO = Instantiate(overlayPrefab.gameObject, overlayPrefab.transform.parent);
            cloneGO.name = overlayPrefab.gameObject.name + "_x" + (i + 1);

            var img = cloneGO.GetComponent<RawImage>();
            _overlays.Add(img);
        }

        // na starcie wygłuszamy wszystkie warstwy
        SetOverlaysAlpha(0f);

        _canvasGroup = GetComponent<CanvasGroup>();
        if (_canvasGroup == null)
            _canvasGroup = gameObject.AddComponent<CanvasGroup>();

        _canvasGroup.alpha = 0f;
        _canvasGroup.interactable = false;
        _canvasGroup.blocksRaycasts = false;

        // ustaw UV na wszystkich warstwach
        ApplyUvToOverlays();
    }

    void Start()
    {
        if (_loop != null) StopCoroutine(_loop);
        _loop = StartCoroutine(RefreshLoop());

        // na starcie upewnij się, że nic nie świeci
        UpdateVisibility();
    }

    private void OnValidate()
    {
        if (overlayPrefab)
        {
            overlayPrefab.uvRect = new Rect(uvOffset.x, uvOffset.y, uvScale.x, uvScale.y);
        }
    }

    private IEnumerator RefreshLoop()
    {
        // pierwszy load od razu
        yield return DownloadAndApply();

        // potem w pętli co X minut
        while (true)
        {
            yield return new WaitForSeconds(refreshIntervalMinutes * 60f);
            yield return DownloadAndApply();
        }
    }

    private IEnumerator DownloadAndApply()
    {
        if (string.IsNullOrEmpty(apiKey))
        {
            Debug.LogError("[RainFinderOverlay] Brak apiKey.");
            yield break;
        }

        string url = $"https://tile.openweathermap.org/map/{layer}/{zoom}/0/0.png?appid={apiKey}";
        Debug.Log("[RainFinderOverlay] Downloading: " + url);

        using (var req = UnityWebRequestTexture.GetTexture(url))
        {
            yield return req.SendWebRequest();

#if UNITY_2020_2_OR_NEWER
            if (req.result != UnityWebRequest.Result.Success)
#else
            if (req.isHttpError || req.isNetworkError)
#endif
            {
                Debug.LogWarning("[RainFinderOverlay] HTTP error: " + req.error);
                yield break;
            }

            _currentTexture = DownloadHandlerTexture.GetContent(req);
        }

        if (_currentTexture == null)
        {
            Debug.LogWarning("[RainFinderOverlay] Null texture.");
            yield break;
        }

        // podmień teksturę na każdej warstwie
        foreach (var img in _overlays)
        {
            if (!img) continue;
            img.texture = _currentTexture;
            img.raycastTarget = false;
        }

        ApplyUvToOverlays();
        UpdateVisibility();
    }

    private void SetOverlaysAlpha(float a)
    {
        foreach (var img in _overlays)
        {
            if (!img) continue;
            var c = img.color;
            c.a = a;
            img.color = c;
        }
    }

    private void ApplyUvToOverlays()
    {
        var rect = new Rect(uvOffset.x, uvOffset.y, uvScale.x, uvScale.y);
        foreach (var img in _overlays)
        {
            if (!img) continue;
            img.uvRect = rect;
        }
    }

    private void UpdateVisibility()
    {
        if (_visible)
        {
            // pokaz
            SetOverlaysAlpha(singleLayerAlpha);
            if (_canvasGroup) _canvasGroup.alpha = 1f;
        }
        else
        {
            // ukryj
            SetOverlaysAlpha(0f);
            if (_canvasGroup) _canvasGroup.alpha = 0f;
        }
    }


    //po zebraniu kryształka overlay jest widoczny

    public void EnableRainFinder()
    {
        _visible = true;
        UpdateVisibility();
        Debug.Log("[RainFinderOverlay] RainFinder activated!");
    }
}
