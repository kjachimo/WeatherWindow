using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class MapWeatherUI : MonoBehaviour
{
    [Header("Refs")]
    [SerializeField] private WeatherService weatherService;
    [SerializeField] private WeatherStampSpawner spawner;
    [SerializeField] private PlayerMovement playerMovement;

    [Header("UI")]
    [SerializeField] private GameObject panelRoot;     // cały panel z mapą
    [SerializeField] private Image worldMapImage;      // Image z bitmapą mapy
    [SerializeField] private TMP_Text latLonLabel;     // olabel współrzędnych
    [SerializeField] private Button closeButton;       // (opcjonalny) X zamknij

    [Header("Sterowanie")]
    [SerializeField] private bool enableToggleKey = true;
    [SerializeField] private KeyCode toggleKey = KeyCode.C;
    [SerializeField] private bool autoCloseOnSelect = true;

    [Header("Map bounds (dla obrazka)")]
    [SerializeField] private float minLon = -180f;
    [SerializeField] private float maxLon =  180f;
    [SerializeField] private float minLat =  -90f;
    [SerializeField] private float maxLat =   90f;

    public enum Projection { Equirectangular, WebMercator }
    [SerializeField] private Projection projection = Projection.Equirectangular;

    [Header("Path mark (ostatni klik)")]
    [SerializeField] private RectTransform pathMark;   // mały znacznik na mapie
    [SerializeField] private float pathMarkSize = 2.0f;  

    [Header("Path mark colors")]
    public Color defaultPathColor   = Color.green;
    public Color rainPathColor      = Color.blue;
    public Color sunPathColor       = Color.yellow;
    public Color snowColdPathColor  = Color.cyan;

    [Header("Path mark thresholds")]
    public float pathRainThresholdMmH = 0.01f;
    public float pathHotSunTempC      = 25f;
    public float pathFreezeTempC      = 0f;

    private bool isOpen = false;

    void Awake()
    {
        if (panelRoot) panelRoot.SetActive(false);
        if (closeButton) closeButton.onClick.AddListener(Close);

        if (!worldMapImage)
            Debug.LogWarning("[MapWeatherUI] Nie przypisano worldMapImage (Image z mapą).");

        if (pathMark) pathMark.gameObject.SetActive(false);
    }

    void Update()
    {

        if (enableToggleKey && Input.GetKeyDown(toggleKey))
        {
            if (!isOpen) Open();
            else Close();
        }

        if (!isOpen) return;


        if (Input.GetMouseButtonDown(0))
            TryPickOnMap(Input.mousePosition);

        if (Input.GetKeyDown(KeyCode.Escape))
            Close();
    }

    // API dla przycisku 
    public void OpenFromButton() => Open();

    //Logika UI
    private void Open()
    {
        if (isOpen) return;
        isOpen = true;

        if (panelRoot) panelRoot.SetActive(true);
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
        if (playerMovement) playerMovement.inputBlocked = true;

        if (worldMapImage) worldMapImage.raycastTarget = true;
    }

    private void Close()
    {
        if (!isOpen) return;
        isOpen = false;

        if (panelRoot) panelRoot.SetActive(false);
        if (playerMovement) playerMovement.inputBlocked = false;
        //Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    private void TryPickOnMap(Vector3 screenPos)
    {
        if (!worldMapImage) return;

        RectTransform mapRect = worldMapImage.rectTransform;

        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(mapRect, screenPos, null, out Vector2 local))
            return;

        Rect r = mapRect.rect;
        if (local.x < r.xMin || local.x > r.xMax || local.y < r.yMin || local.y > r.yMax)
            return; // klik poza obrazkiem mapy

        // UV 0..1
        float u = (local.x - r.xMin) / r.width;
        float v = (local.y - r.yMin) / r.height;

        // lat/lon
        float lon, lat;
        if (projection == Projection.Equirectangular)
        {
            lon = Mathf.Lerp(minLon, maxLon, u);
            lat = Mathf.Lerp(minLat, maxLat, v);
        }
        else
        {
            lon = Mathf.Lerp(minLon, maxLon, u);
            lat = MercatorVToLat(Mathf.Clamp01(v));
            lat = Mathf.Clamp(lat, minLat, maxLat);
        }

        if (latLonLabel)
            latLonLabel.text = $"{lat:0.000}°, {lon:0.000}°";

        // zapamiętujemy lokalną pozycję kliknięcia na mapie (dla znacznika)
        var clickLocal = local;

        // Pobierz pogodę i zrób patch
        if (weatherService)
        {
            StartCoroutine(weatherService.FetchByCoords(lat, lon, onDone: (w) =>
            {
                if (w != null)
                {
                    // kolor wg pogody
                    var color = GetPathColor(w);
                    // znacznik "piksela" na mapie w miejscu kliknięcia
                    PlacePathMark(clickLocal, color);

                    if (spawner) spawner.DropPatchHere(w);
                }

                if (autoCloseOnSelect) Close();
            }));
        }
        else
        {
            Debug.LogWarning("[MapWeatherUI] Brak WeatherService.");
            if (autoCloseOnSelect) Close();
        }
    }

    // v in [0..1] → latitude (Web Mercator)
    private float MercatorVToLat(float v)
    {
        double y = System.Math.PI * (1.0 - 2.0 * v);
        double latRad = System.Math.Atan(System.Math.Sinh(y));
        return (float)(latRad * (180.0 / System.Math.PI));
    }

    // Patch mark

    private Color GetPathColor(WeatherModel w)
    {
        if (w == null) return defaultPathColor;

        bool isSnowCold = w.isSnow || w.temperatureC <= pathFreezeTempC;
        bool isRaining  = !w.isSnow && w.precipitationMmH > pathRainThresholdMmH;
        bool isSunnyHot = !w.isSnow && w.precipitationMmH <= pathRainThresholdMmH
                          && w.temperatureC >= pathHotSunTempC;

        if (isSnowCold) return snowColdPathColor;
        if (isRaining)  return rainPathColor;
        if (isSunnyHot) return sunPathColor;
        return defaultPathColor;
    }

    private void PlacePathMark(Vector2 localOnMap, Color color)
    {
        if (!worldMapImage) return;

        //mały znacznik automatycznie
        if (!pathMark)
        {
            var go = new GameObject("PathMark", typeof(RectTransform), typeof(Image));
            var rt = go.GetComponent<RectTransform>();
            rt.SetParent(worldMapImage.rectTransform, false);
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(0.5f*pathMarkSize, pathMarkSize);

            var img = go.GetComponent<Image>();
            img.raycastTarget = false;

            pathMark = rt;
        }

        pathMark.gameObject.SetActive(true);
        pathMark.anchoredPosition = localOnMap;

        var markImg = pathMark.GetComponent<Image>();
        if (markImg) markImg.color = color;
    }
}
