using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class MapWeatherUI : MonoBehaviour
{
    [Header("Refs")]
    [SerializeField] private WeatherService weatherService;
    [SerializeField] private WeatherStampSpawner spawner;
    [SerializeField] private PlayerMovement playerMovement; // opcjonalnie: blokada ruchu

    [Header("UI")]
    [SerializeField] private GameObject panelRoot;     // cały panel z mapą (NA START = INACTIVE)
    [SerializeField] private Image worldMapImage;      // Image z bitmapą mapy
    [SerializeField] private RectTransform pin;        // UI pinezka (dziecko panelu)
    [SerializeField] private TMP_Text latLonLabel;     // opcjonalny label współrzędnych
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

   // [Header("Projekcja")]
    public enum Projection { Equirectangular, WebMercator }
    [SerializeField] private Projection projection = Projection.Equirectangular;

    private bool isOpen = false;

    void Awake()
    {
        if (panelRoot) panelRoot.SetActive(false);
        if (pin) pin.gameObject.SetActive(false);
        if (closeButton) closeButton.onClick.AddListener(Close);

        if (!worldMapImage)
            Debug.LogWarning("[MapWeatherUI] Nie przypisano worldMapImage (Image z mapą).");
    }

    void Update()
    {
        // Toggle z klawisza (opcjonalnie)
        if (enableToggleKey && Input.GetKeyDown(toggleKey))
        {
            if (!isOpen) Open();
            else Close();
        }

        if (!isOpen) return;

        // Klik tylko gdy panel otwarty
        if (Input.GetMouseButtonDown(0))
            TryPickOnMap(Input.mousePosition);

        if (Input.GetKeyDown(KeyCode.Escape))
            Close();
    }

    // ====== API dla przycisku ======
    public void OpenFromButton() => Open();

    // ====== Logika UI ======
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
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    private void TryPickOnMap(Vector3 screenPos)
    {
        if (!worldMapImage) return;

        RectTransform mapRect = worldMapImage.rectTransform;

        // Canvas Screen Space - Overlay -> camera = null ok
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

        // Pin dla feedbacku
        if (pin)
        {
            pin.gameObject.SetActive(true);
            pin.anchoredPosition = local;
        }
        if (latLonLabel)
            latLonLabel.text = $"{lat:0.000}°, {lon:0.000}°";

        // Pobierz pogodę i zrób patch
        if (weatherService)
        {
            StartCoroutine(weatherService.FetchByCoords(lat, lon, onDone: (w) =>
            {
                if (spawner && w != null) spawner.DropPatchHere(w);
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
}
