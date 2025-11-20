using System;
using System.Collections;
using System.IO;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI;

[RequireComponent(typeof(RawImage))]
public class MapWeatherOverlay : MonoBehaviour
{
    [Header("OpenWeather Tile API")]
    [Tooltip("Tylko sam klucz, bez &appid= itd.")]
    [SerializeField] private string apiKey = "0fba094e1151464baacdc95c0aae264f";

    [Tooltip("Bazowy URL do tile API (nie zmieniaj, chyba że wiesz co robisz).")]
    [SerializeField] private string tilesBaseUrl = "https://tile.openweathermap.org/map";

    [Tooltip("Nazwa warstwy – np. precipitation_new, temp_new, clouds_new itd.")]
    [SerializeField] private string layer = "precipitation_new";

    [Tooltip("Zoom tile’i. 0 = cały świat w jednym tile (x=0,y=0).")]
    [SerializeField] private int zoom = 0;

    [Tooltip("Ile godzin trzymać tile w cache na dysku.")]
    [SerializeField] private float cacheHours = 1f;

    [Header("UI")]
    [SerializeField] private RawImage targetImage;

    [Range(0f, 1f)]
    public float overlayOpacity = 0.45f;

    private Coroutine _loadRoutine;

    private string CachePath =>
        Path.Combine(Application.persistentDataPath,
            $"owm_tile_{layer}_z{zoom}_x0_y0.png");

    private void Awake()
    {
        if (!targetImage)
            targetImage = GetComponent<RawImage>();

        ApplyOpacity();
    }

    private void OnValidate()
    {
        if (!targetImage)
            targetImage = GetComponent<RawImage>();
        ApplyOpacity();
    }

    //z przycisku
    public void RefreshOverlay(bool forceDownload = false)
    {
        if (_loadRoutine != null)
            StopCoroutine(_loadRoutine);

        _loadRoutine = StartCoroutine(LoadTileCoroutine(forceDownload));
    }

    public void SetLayer(string newLayer, bool refreshNow = true)
    {
        if (string.IsNullOrWhiteSpace(newLayer)) return;
        layer = newLayer.Trim();
        if (refreshNow)
            RefreshOverlay(true);
    }

    private IEnumerator LoadTileCoroutine(bool forceDownload)
    {
        if (string.IsNullOrEmpty(apiKey))
        {
            Debug.LogError("[MapWeatherOverlay] Brak API key.");
            yield break;
        }

        Texture2D tex = null;

        //Cache na dysku
        if (!forceDownload && File.Exists(CachePath))
        {
            var info = new FileInfo(CachePath);
            var age = DateTime.UtcNow - info.LastWriteTimeUtc;
            if (age.TotalHours <= cacheHours)
            {
                try
                {
                    byte[] bytes = File.ReadAllBytes(CachePath);
                    tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                    tex.wrapMode = TextureWrapMode.Clamp;
                    tex.filterMode = FilterMode.Bilinear;
                    tex.LoadImage(bytes);
                    Debug.Log($"[MapWeatherOverlay] Loaded tile from cache: {CachePath}");
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[MapWeatherOverlay] Cache read error, fallback to HTTP: {ex.Message}");
                    tex = null;
                }
            }
        }

        // HTTP, jeśli brak/stary cache
        if (tex == null)
        {
            // TU budujemy pełny URL
            string url = $"{tilesBaseUrl}/{layer}/{zoom}/0/0.png?appid={apiKey}";
            Debug.Log($"[MapWeatherOverlay] Downloading tile from URL: {url}");

            using (var req = UnityWebRequestTexture.GetTexture(url))
            {
                yield return req.SendWebRequest();

#if UNITY_2020_2_OR_NEWER
                bool ok = req.result == UnityWebRequest.Result.Success;
#else
                bool ok = !(req.isHttpError || req.isNetworkError);
#endif

                if (!ok)
                {
                    string body = req.downloadHandler != null
                        ? req.downloadHandler.text
                        : "(no body)";
                    Debug.LogWarning(
                        $"[MapWeatherOverlay] HTTP error: {req.error}, code={(int)req.responseCode}, body={body}");
                    yield break;
                }

                tex = DownloadHandlerTexture.GetContent(req);
                tex.wrapMode = TextureWrapMode.Clamp;
                tex.filterMode = FilterMode.Bilinear;

                // zapis do cache
                try
                {
                    byte[] png = tex.EncodeToPNG();
                    File.WriteAllBytes(CachePath, png);
                    Debug.Log($"[MapWeatherOverlay] Saved tile to cache: {CachePath}");
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[MapWeatherOverlay] Cache write error: {ex.Message}");
                }
            }
        }

        // Nałożenie na UI
        if (targetImage)
        {
            targetImage.texture = tex;
            ApplyOpacity();
        }
    }

    private void ApplyOpacity()
    {
        if (!targetImage) return;
        var c = targetImage.color;
        c.a = overlayOpacity;
        targetImage.color = c;
    }
}
