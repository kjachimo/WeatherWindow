using System;
using System.Collections;
using UnityEngine;
using UnityEngine.Networking;

#region Model

[Serializable]
public class WeatherModel
{
    public float temperatureC;      // °C
    public float windSpeedMs;       // m/s
    public float precipitationMmH;  // mm/h (rain1h + snow1h)
    public float cloudiness01;      // 0..1
    public bool isSnow;             // czy traktujemy to jako śnieg
    public string sourceCity;       // nazwa źródła (miasto/test)
}

#endregion

public class WeatherService : MonoBehaviour
{
    [Header("OpenWeatherMap")]
    [SerializeField] private string apiBase = "https://api.openweathermap.org/data/2.5/weather";
    [SerializeField] private string apiKey = "";             
    [SerializeField] private string defaultCity = "Warszawa";
    [Tooltip("Cache w minutach (0 = zawsze pobieraj)")]
    [SerializeField] private int cacheMinutes = 0;

    public event Action<WeatherModel> OnWeatherUpdated;

    private WeatherModel _cache;
    private DateTime _cacheTimeUtc;

    private void Start()
    {
        if (!string.IsNullOrWhiteSpace(defaultCity))
            StartCoroutine(FetchByCity(defaultCity));
    }

    public WeatherModel GetCached() => _cache;

    private bool HasFreshCache =>
        _cache != null && cacheMinutes > 0 &&
        (DateTime.UtcNow - _cacheTimeUtc).TotalMinutes < cacheMinutes;

    /// <summary>
    /// Pobierz pogodę dla miasta. Jeśli cache jest świeży – użyje cache.
    /// </summary>
    public IEnumerator FetchByCity(string city) => FetchByCity(city, false);

    /// <summary>
    /// Pobierz pogodę dla miasta z opcją pominięcia cache.
    /// </summary>
    public IEnumerator FetchByCity(string city, bool forceRefresh)
    {
        city = (city ?? "").Trim();
        if (string.IsNullOrEmpty(city))
        {
            Debug.LogWarning("[WeatherService] Puste miasto.");
            yield break;
        }

        if (!forceRefresh && HasFreshCache)
        {
            Debug.Log("[WeatherService] Używam świeżego cache.");
            OnWeatherUpdated?.Invoke(_cache);
            yield break;
        }

        if (string.IsNullOrEmpty(apiKey))
        {
            Debug.LogError("[WeatherService] Brak API key.");
            yield break;
        }

        var url = $"{apiBase}?q={UnityWebRequest.EscapeURL(city)}&units=metric&appid={apiKey}";
        using var req = UnityWebRequest.Get(url);
        req.timeout = 10; // sekundy

        yield return req.SendWebRequest();

        if (req.result != UnityWebRequest.Result.Success)
        {
            Debug.LogWarning($"[WeatherService] Błąd pobierania: {req.error}");
            if (_cache != null) OnWeatherUpdated?.Invoke(_cache);
            yield break;
        }

        var json = req.downloadHandler.text;
        if (string.IsNullOrEmpty(json))
        {
            Debug.LogWarning("[WeatherService] Pusty JSON.");
            if (_cache != null) OnWeatherUpdated?.Invoke(_cache);
            yield break;
        }

        // JsonUtility nie wspiera kluczy zaczynających się od cyfry → zamiana
        json = json.Replace("\"1h\"", "\"_1h\"").Replace("\"3h\"", "\"_3h\"");

        try
        {
            var root = JsonUtility.FromJson<OWMRoot>(json);
            if (root == null)
            {
                Debug.LogWarning("[WeatherService] Null root po deserializacji.");
                yield break;
            }

            var model = Map(root);
            model.sourceCity = city;

            // LOG: pokaż co naprawdę wyliczyliśmy
            Debug.Log($"[WeatherService] city={city} | tempC={model.temperatureC:F1}°C, wind={model.windSpeedMs:F1} m/s, " +
                      $"precip={model.precipitationMmH:F2} mm/h, clouds={model.cloudiness01:P0}, isSnow={model.isSnow}");

            // Cache + event
            _cache = model;
            _cacheTimeUtc = DateTime.UtcNow;
            OnWeatherUpdated?.Invoke(model);
        }
        catch (Exception e)
        {
            Debug.LogError($"[WeatherService] Błąd JSON: {e.Message}");
        }
    }
    public IEnumerator FetchByCoords(float lat, float lon, System.Action<WeatherModel> onDone = null)
{
    if (string.IsNullOrEmpty(apiKey))
    {
        Debug.LogError("[WeatherService] Brak API key.");
        yield break;
    }

    string url = $"{apiBase}?lat={lat.ToString(System.Globalization.CultureInfo.InvariantCulture)}&lon={lon.ToString(System.Globalization.CultureInfo.InvariantCulture)}&units=metric&appid={apiKey}";
    using var req = UnityEngine.Networking.UnityWebRequest.Get(url);
    yield return req.SendWebRequest();

    if (req.result != UnityEngine.Networking.UnityWebRequest.Result.Success)
    {
        Debug.LogWarning($"[WeatherService] Błąd pobierania: {req.error}");
        if (_cache != null) OnWeatherUpdated?.Invoke(_cache);
        onDone?.Invoke(_cache);
        yield break;
    }

    // fix kluczy 1h/3h
    var json = req.downloadHandler.text;
    json = json.Replace("\"1h\"", "\"_1h\"").Replace("\"3h\"", "\"_3h\"");

    try
    {
        var root = JsonUtility.FromJson<OWMRoot>(json);
        var model = Map(root);
        model.sourceCity = $"@{lat:0.000},{lon:0.000}";

        _cache = model;
        _cacheTimeUtc = System.DateTime.UtcNow;

        OnWeatherUpdated?.Invoke(model);
        onDone?.Invoke(model);
    }
    catch (System.Exception e)
    {
        Debug.LogError($"[WeatherService] Błąd JSON: {e.Message}");
    }
}


    // --- Mapowanie danych OWM -> WeatherModel ---
    private WeatherModel Map(OWMRoot r)
    {
        float tempC = r?.main?.temp ?? 0f;
        float wind = r?.wind?.speed ?? 0f;

        float rain1h = r?.rain?._1h ?? 0f;
        float rain3h = r?.rain?._3h ?? 0f;
        float snow1h = r?.snow?._1h ?? 0f;
        float snow3h = r?.snow?._3h ?? 0f; // nieużywane, ale może się przydać

        // jeśli nie ma 1h, a jest 3h – przelicz na godzinę
        if (rain1h <= 0f && rain3h > 0f) rain1h = rain3h / 3f;
        if (snow1h <= 0f && snow3h > 0f) snow1h = snow3h / 3f;

        float precip = Mathf.Max(0f, rain1h) + Mathf.Max(0f, snow1h);

        // "Game-friendly": jeśli zimno (<=0°C) i są opady → traktuj jako śnieg,
        // albo jeśli OWM podał snow1h>0 → też śnieg.
        bool isSnow = (snow1h > 0f) || (tempC <= 0f && precip > 0f);

        float clouds01 = Mathf.Clamp01((r?.clouds?.all ?? 0) / 100f);

        return new WeatherModel
        {
            temperatureC = tempC,
            windSpeedMs = wind,
            precipitationMmH = precip,
            cloudiness01 = clouds01,
            isSnow = isSnow
        };
    }

    #region DTO dla JsonUtility

    [Serializable] private class OWMRoot
    {
        public OWMMain main;
        public OWMWind wind;
        public OWMClouds clouds;
        public OWMRain rain;
        public OWMSnow snow;
    }

    [Serializable] private class OWMMain { public float temp; }
    [Serializable] private class OWMWind { public float speed; public int deg; }
    [Serializable] private class OWMClouds { public int all; }
    [Serializable] private class OWMRain { public float _1h; public float _3h; }
    [Serializable] private class OWMSnow { public float _1h; public float _3h; }

    #endregion

    #region Testy kontekstowe (Inspector -> menu komponentu)

    [ContextMenu("TEST: Freeze (-5C, no precip)")]
    private void TestFreeze()
    {
        var m = new WeatherModel
        {
            temperatureC = -5f,
            windSpeedMs = 0f,
            precipitationMmH = 0f,
            cloudiness01 = 0.2f,
            isSnow = false,
            sourceCity = "TEST_FREEZE"
        };
        _cache = m; _cacheTimeUtc = DateTime.UtcNow;
        Debug.Log("[WeatherService] TEST_FREEZE pushed");
        OnWeatherUpdated?.Invoke(m);
    }

    [ContextMenu("TEST: Rain +5C (2 mm/h)")]
    private void TestRainWarm()
    {
        var m = new WeatherModel
        {
            temperatureC = 5f,
            windSpeedMs = 0f,
            precipitationMmH = 2f,
            cloudiness01 = 0.8f,
            isSnow = false,
            sourceCity = "TEST_RAIN_WARM"
        };
        _cache = m; _cacheTimeUtc = DateTime.UtcNow;
        Debug.Log("[WeatherService] TEST_RAIN_WARM pushed");
        OnWeatherUpdated?.Invoke(m);
    }

    [ContextMenu("TEST: Snow -2C (1 mm/h snow)")]
    private void TestSnowCold()
    {
        var m = new WeatherModel
        {
            temperatureC = -2f,
            windSpeedMs = 1.5f,
            precipitationMmH = 1f, // potraktujemy jako śnieg (isSnow=true)
            cloudiness01 = 1f,
            isSnow = true,
            sourceCity = "TEST_SNOW"
        };
        _cache = m; _cacheTimeUtc = DateTime.UtcNow;
        Debug.Log("[WeatherService] TEST_SNOW pushed");
        OnWeatherUpdated?.Invoke(m);
    }

    #endregion
}
