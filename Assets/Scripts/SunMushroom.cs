using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;

[RequireComponent(typeof(BoxCollider2D))]
[RequireComponent(typeof(Rigidbody2D))]
public class SunMushroom : MonoBehaviour
{
    [Header("Tilemap grzybów (wymagana)")]
    public Tilemap mushroomTilemap;
    public TilemapCollider2D mushroomCol;

    [Header("Warunek 'gorącego słońca'")]
    public float minTempC = 25f;
    public float maxPrecipMmH = 0.01f;

    [Header("Sensor")]
    public bool autoFitToTilemapBounds = true;

    [Header("Tint (opcjonalnie)")]
    public Color baseColor = Color.white;
    public Color sunColor  = new Color(0.7f, 0.9f, 1f, 1f);
    public float fadeSpeed = 3f;

    // wewnętrzne
    private readonly HashSet<WeatherPatchLocal> _patches = new();
    private BoxCollider2D _sensor;
    private Rigidbody2D _rb;
    private TilemapRenderer _tmr;
    private bool _wasSunny = false;

    void Awake()
    {
        if (!mushroomTilemap)
        {
            Debug.LogError("[SunMushroomSolidSimple] Brak przypisanej Tilemapy!");
            enabled = false; return;
        }

        if (!mushroomCol)
        {
            mushroomCol = mushroomTilemap.GetComponent<TilemapCollider2D>();
            if (!mushroomCol)
            {
                Debug.LogError("[SunMushroomSolidSimple] Tilemap nie ma TilemapCollider2D.");
                enabled = false; return;
            }
        }

        // sensor
        _sensor = GetComponent<BoxCollider2D>();
        _sensor.isTrigger = true;

        _rb = GetComponent<Rigidbody2D>();
        _rb.bodyType = RigidbodyType2D.Kinematic;
        _rb.gravityScale = 0;

        _tmr = mushroomTilemap.GetComponent<TilemapRenderer>();

        if (autoFitToTilemapBounds && _tmr)
        {
            var wb = _tmr.bounds; // world bounds
            Vector2 size   = transform.InverseTransformVector(wb.size);
            Vector2 center = transform.InverseTransformPoint(wb.center) - transform.position;
            size.x = Mathf.Abs(size.x); size.y = Mathf.Abs(size.y);
            _sensor.size = size;
            _sensor.offset = center;
            Debug.Log($"[SunMushroomSolidSimple] Auto-fit sensor. size={size}, offset={center}");
        }

        // start: kolizja wyłączona (grzyby „miękkie”)
        mushroomCol.enabled = false;
        mushroomCol.isTrigger = false; // kiedy włączymy, ma być solid
        if (mushroomTilemap) mushroomTilemap.color = baseColor;

        // ważne: tile assets grzybów muszą mieć Collider Type ≠ None (Sprite/Grid)
        Debug.Log("[SunMushroomSolidSimple] Upewnij się, że Tile assets mają Collider Type ≠ None.");
    }

    void OnTriggerEnter2D(Collider2D other)
    {
        if (other.TryGetComponent(out WeatherPatchLocal p))
            _patches.Add(p);
    }

    void OnTriggerExit2D(Collider2D other)
    {
        if (other.TryGetComponent(out WeatherPatchLocal p))
            _patches.Remove(p);
    }

    void Update()
    {
        bool sunny = false;

        foreach (var p in _patches)
        {
            if (!p || p.Weather == null) continue;
            var w = p.Weather;
            if (!w.isSnow && w.precipitationMmH <= maxPrecipMmH && w.temperatureC >= minTempC)
            {
                sunny = true;
                break;
            }
        }

        if (sunny != _wasSunny)
        {
            // przełącz jedyny collider
            mushroomCol.enabled = sunny;
            //ForcePhysicsRefresh();
           // Debug.Log($"[SunMushroomSolidSimple] Collider {(sunny ? "ON" : "OFF")}");

            _wasSunny = sunny;
        }

        // tint dla feedbacku (opcjonalnie)
        if (mushroomTilemap)
        {
            var target = sunny ? sunColor : baseColor;
            mushroomTilemap.color = Color.Lerp(mushroomTilemap.color, target, Time.deltaTime * fadeSpeed);
        }

    }

    void ForcePhysicsRefresh()
    {
        if (mushroomTilemap) mushroomTilemap.CompressBounds();
        if (mushroomCol)     mushroomCol.ProcessTilemapChanges();
        var tmrRb = mushroomTilemap.GetComponent<Rigidbody2D>();
        if (tmrRb)
        {
            bool sim = tmrRb.simulated;
            tmrRb.simulated = false;
            tmrRb.simulated = sim;
            tmrRb.WakeUp();
        }
        Physics2D.SyncTransforms();
    }
}
