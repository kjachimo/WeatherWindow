using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
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


    [Header("Smart Cache")]
    [SerializeField, Tooltip("Nazwa pliku cache w persistentDataPath.")]
    private string cacheFileName = "weather_cache_v2.json";
    [SerializeField, Tooltip("Czas ważności wpisu w godzinach.")]
    private double ttlHours = 12.0;
    [SerializeField, Tooltip("Limit zapytań HTTP na dobę.")]
    private int dailyRequestLimit = 1000;
    [SerializeField, Tooltip("Krok siatki kwantyzacji współrzędnych w stopniach.")]
    private double coordGrid = 1.0;

    private string CachePath => Path.Combine(Application.persistentDataPath, cacheFileName);

    private CacheDB _pCache = new CacheDB();

    private readonly HashSet<string> _inFlight = new HashSet<string>();

    public event Action<WeatherModel> OnWeatherUpdated;


    private WeatherModel _cache;
    private DateTime _cacheTimeUtc;

    private void Start()
    {
        LoadPersistentCache();
        if (!string.IsNullOrWhiteSpace(defaultCity))
            StartCoroutine(FetchByCity(defaultCity));
    }

    public WeatherModel GetCached() => _cache;

    private bool HasFreshCache =>
        _cache != null && cacheMinutes > 0 &&
        (DateTime.UtcNow - _cacheTimeUtc).TotalMinutes < cacheMinutes;

    public IEnumerator FetchByCity(string city) => FetchByCity(city, false);


    public IEnumerator FetchByCity(string city, bool forceRefresh)
    {
        city = (city ?? "").Trim();
        if (string.IsNullOrEmpty(city))
        {
            Debug.LogWarning("[WeatherService] Puste miasto.");
            yield break;
        }
        if (string.IsNullOrEmpty(apiKey))
        {
            Debug.LogError("[WeatherService] Brak API key.");
            yield break;
        }

        if (!forceRefresh && HasFreshCache)
        {
            Debug.Log("[WeatherService] Używam świeżego cache (legacy single-entry).");
            OnWeatherUpdated?.Invoke(_cache);
            yield break;
        }


        string key = CacheKeyForCity(city);
        string url = $"{apiBase}?q={UnityWebRequest.EscapeURL(city)}&units=metric&appid={apiKey}";


        if (forceRefresh && _pCache.dict.ContainsKey(key))
            _pCache.dict.Remove(key);

        yield return FetchCore(key, url, city, null);
    }


    public IEnumerator FetchByCoords(float lat, float lon, System.Action<WeatherModel> onDone = null)
    {
        if (string.IsNullOrEmpty(apiKey))
        {
            Debug.LogError("[WeatherService] Brak API key.");
            yield break;
        }

        double qLat = Quantize(lat, coordGrid);
        double qLon = Quantize(lon, coordGrid);

        string key = CacheKeyForCoords(qLat, qLon);
        string url = $"{apiBase}?lat={qLat.ToString(System.Globalization.CultureInfo.InvariantCulture)}&lon={qLon.ToString(System.Globalization.CultureInfo.InvariantCulture)}&units=metric&appid={apiKey}";
        string label = $"@{qLat:0.000},{qLon:0.000}";

        yield return FetchCore(key, url, label, onDone);
    }

    private IEnumerator FetchCore(string key, string url, string sourceLabel, System.Action<WeatherModel> onDone = null)
    {

        if (_inFlight.Contains(key))
        {
            yield return new WaitUntil(() => !_inFlight.Contains(key));
            if (_pCache.TryGetAny(key, out var got))
            {
                OnWeatherUpdated?.Invoke(got.payload);
                onDone?.Invoke(got.payload);
            }
            yield break;
        }
        _inFlight.Add(key);

        ResetDailyCounterIfNeeded();

        if (_pCache.TryGetFresh(key, TimeSpan.FromHours(ttlHours), out var fresh))
        {
            Debug.Log($"[WeatherService] CACHE HIT (fresh) key={key}");
            _cache = fresh.payload;                    // aktualizuj cache
            _cacheTimeUtc = DateTime.UtcNow;
            OnWeatherUpdated?.Invoke(fresh.payload);
            onDone?.Invoke(fresh.payload);
            _inFlight.Remove(key);
            yield break;
        }

        bool canCallApi = _pCache.requestsToday < dailyRequestLimit;

        //jeśli limit wybity, zwróć stare
        if (!canCallApi && _pCache.TryGetAny(key, out var stale))
        {
            Debug.Log($"[WeatherService] CACHE HIT (stale; limit reached) key={key}");
            _cache = stale.payload;
            _cacheTimeUtc = DateTime.UtcNow;
            OnWeatherUpdated?.Invoke(stale.payload);
            onDone?.Invoke(stale.payload);
            _inFlight.Remove(key);
            yield break;
        }

        //fetch z API
        using (var req = UnityWebRequest.Get(url))
        {
            req.timeout = 10;
            yield return req.SendWebRequest();

#if UNITY_2020_2_OR_NEWER
            bool ok = req.result == UnityWebRequest.Result.Success;
#else
            bool ok = !(req.isHttpError || req.isNetworkError);
#endif

            if (!ok)
            {
                Debug.LogWarning($"[WeatherService] HTTP error: {req.error}");
                if (_pCache.TryGetAny(key, out var any))
                {
                    _cache = any.payload;
                    _cacheTimeUtc = DateTime.UtcNow;
                    OnWeatherUpdated?.Invoke(any.payload);
                    onDone?.Invoke(any.payload);
                }
                _inFlight.Remove(key);
                yield break;
            }

            var json = req.downloadHandler.text;
            if (string.IsNullOrEmpty(json))
            {
                Debug.LogWarning("[WeatherService] Empty JSON.");
                if (_pCache.TryGetAny(key, out var any2))
                {
                    _cache = any2.payload;
                    _cacheTimeUtc = DateTime.UtcNow;
                    OnWeatherUpdated?.Invoke(any2.payload);
                    onDone?.Invoke(any2.payload);
                }
                _inFlight.Remove(key);
                yield break;
            }

            json = json.Replace("\"1h\"", "\"_1h\"").Replace("\"3h\"", "\"_3h\"");

            try
            {
                var root = JsonUtility.FromJson<OWMRoot>(json);
                if (root == null) throw new Exception("Null root");

                var model = Map(root);
                model.sourceCity = sourceLabel;


                Debug.Log($"[WeatherService] API USED key={key} | tempC={model.temperatureC:F1}°C, wind={model.windSpeedMs:F1} m/s, " +
                          $"precip={model.precipitationMmH:F2} mm/h, clouds={model.cloudiness01:P0}, isSnow={model.isSnow}");

                _pCache.Upsert(key, model, DateTime.UtcNow);
                _pCache.requestsToday++;
                SavePersistentCache();

                _cache = model;
                _cacheTimeUtc = DateTime.UtcNow;

                OnWeatherUpdated?.Invoke(model);
                onDone?.Invoke(model);
            }
            catch (Exception e)
            {
                Debug.LogError($"[WeatherService] JSON error: {e.Message}");
            }
            finally
            {
                _inFlight.Remove(key);
            }
        }
    }

    // Mapowanie danych OWM 
    private WeatherModel Map(OWMRoot r)
    {
        float tempC = r?.main?.temp ?? 0f;
        float wind = r?.wind?.speed ?? 0f;

        float rain1h = r?.rain?._1h ?? 0f;
        float rain3h = r?.rain?._3h ?? 0f;
        float snow1h = r?.snow?._1h ?? 0f;
        float snow3h = r?.snow?._3h ?? 0f; 

   
        if (rain1h <= 0f && rain3h > 0f) rain1h = rain3h / 3f;
        if (snow1h <= 0f && snow3h > 0f) snow1h = snow3h / 3f;

        float precip = Mathf.Max(0f, rain1h) + Mathf.Max(0f, snow1h);

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

    #region Persistencja Smart Cache + helpery

    private static string CacheKeyForCity(string city) => $"city:{(city ?? "").Trim().ToLowerInvariant()}";
    private static string CacheKeyForCoords(double lat, double lon) => $"geo:{lat:F4},{lon:F4}";

    private static double Quantize(double v, double step)
    {
        if (step <= 0) return v;
        double q = Math.Round(v / step) * step;
        return Math.Round(q, 4);
    }

    private void ResetDailyCounterIfNeeded()
    {
        var today = DateTime.UtcNow.Date;
        if (_pCache.dayStamp != today)
        {
            _pCache.dayStamp = today;
            _pCache.requestsToday = 0;
            SavePersistentCache();
        }
    }

    [Serializable]
    private class CacheFile
    {
        public string dayStampIso;
        public int requestsToday;
        public List<Entry> entries = new List<Entry>();

        [Serializable]
        public class Entry
        {
            public string key;
            public string utcIso;
            public string payloadJson;
        }
    }

    private class CacheDB
    {
        public DateTime dayStamp = DateTime.UtcNow.Date;
        public int requestsToday = 0;
        public readonly Dictionary<string, Item> dict = new Dictionary<string, Item>();

        public struct Item
        {
            public WeatherModel payload;
            public DateTime savedUtc;
            public Item(WeatherModel p, DateTime when) { payload = p; savedUtc = when; }
        }

        public bool TryGetFresh(string key, TimeSpan ttl, out Item item)
        {
            if (dict.TryGetValue(key, out item))
                return (DateTime.UtcNow - item.savedUtc) <= ttl;
            return false;
        }

        public bool TryGetAny(string key, out Item item) => dict.TryGetValue(key, out item);

        public void Upsert(string key, WeatherModel payload, DateTime whenUtc)
        {
            dict[key] = new Item(payload, whenUtc);
        }
    }

    private void LoadPersistentCache()
    {
        try
        {
            if (!File.Exists(CachePath))
            {
                SavePersistentCache();
                return;
            }

            var json = File.ReadAllText(CachePath);
            var file = JsonUtility.FromJson<CacheFile>(json) ?? new CacheFile();
            _pCache.dayStamp = TryParseDate(file.dayStampIso) ?? DateTime.UtcNow.Date;
            _pCache.requestsToday = file.requestsToday;
            _pCache.dict.Clear();

            foreach (var e in file.entries)
            {
                var when = TryParseIso(e.utcIso) ?? DateTime.UtcNow.AddHours(-1000);
                var payload = JsonUtility.FromJson<WeatherModel>(e.payloadJson);
                if (payload != null)
                    _pCache.Upsert(e.key, payload, when);
            }
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[WeatherService] LoadPersistentCache error: {ex.Message}");
            _pCache = new CacheDB();
        }
    }

    private void SavePersistentCache()
    {
        try
        {
            var file = new CacheFile
            {
                dayStampIso = _pCache.dayStamp.ToString("yyyy-MM-dd"),
                requestsToday = _pCache.requestsToday,
                entries = new List<CacheFile.Entry>()
            };

            foreach (var kv in _pCache.dict)
            {
                file.entries.Add(new CacheFile.Entry
                {
                    key = kv.Key,
                    utcIso = kv.Value.savedUtc.ToUniversalTime().ToString("o"),
                    payloadJson = JsonUtility.ToJson(kv.Value.payload)
                });
            }

            var json = JsonUtility.ToJson(file, prettyPrint: false);
            File.WriteAllText(CachePath, json);
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[WeatherService] SavePersistentCache error: {ex.Message}");
        }
    }

    private static DateTime? TryParseDate(string s)
    {
        if (string.IsNullOrEmpty(s)) return null;
        if (DateTime.TryParse(s, out var dt)) return dt.Date;
        return null;
    }

    private static DateTime? TryParseIso(string s)
    {
        if (string.IsNullOrEmpty(s)) return null;
        if (DateTime.TryParse(s, null, System.Globalization.DateTimeStyles.AdjustToUniversal, out var dt)) return dt;
        return null;
    }

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
            precipitationMmH = 1f,
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
