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

    [Header("Tiles (sprite'y)")]
    [Tooltip("Tile używany normalnie (bez słońca). Jeśli puste, zostanie pobrany z istniejącej tilemapy.")]
    public TileBase normalTile;

    [Tooltip("Tile używany, gdy grzyby są w 'gorącym słońcu'.")]
    public TileBase sunTile;

    // wewnętrzne
    private readonly HashSet<WeatherPatchLocal> _patches = new();
    private BoxCollider2D _sensor;
    private Rigidbody2D _rb;
    private TilemapRenderer _tmr;
    private bool _wasSunny = false;

    // przechowujemy oryginalny układ tile'i, żeby móc go przywrócić
    private readonly List<Vector3Int> _mushroomCells = new();
    private readonly Dictionary<Vector3Int, TileBase> _originalTiles = new();

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
            var wb = _tmr.bounds;
            Vector2 size   = transform.InverseTransformVector(wb.size);
            Vector2 center = transform.InverseTransformPoint(wb.center) - transform.position;
            size.x = Mathf.Abs(size.x); size.y = Mathf.Abs(size.y);
            _sensor.size = size;
            _sensor.offset = center;
            Debug.Log($"[SunMushroomSolidSimple] Auto-fit sensor. size={size}, offset={center}");
        }

        CacheOriginalTiles();

        if (normalTile == null && _mushroomCells.Count > 0)
        {
            normalTile = _originalTiles[_mushroomCells[0]];
        }

        // start kolizja wyłączona
        mushroomCol.enabled = false;
        mushroomCol.isTrigger = false; // kiedy włączymy, ma być solid

        Debug.Log("[SunMushroomSolidSimple] Upewnij się, że Tile assets mają Collider Type ≠ None.");
    }

    void CacheOriginalTiles()
    {
        _mushroomCells.Clear();
        _originalTiles.Clear();

        if (!mushroomTilemap) return;

        var bounds = mushroomTilemap.cellBounds;
        foreach (var pos in bounds.allPositionsWithin)
        {
            var t = mushroomTilemap.GetTile(pos);
            if (t != null)
            {
                _mushroomCells.Add(pos);
                _originalTiles[pos] = t;
            }
        }
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
            // przełącz collider
            mushroomCol.enabled = sunny;
            //ForcePhysicsRefresh();
            //Debug.Log($"[SunMushroomSolidSimple] Collider {(sunny ? "ON" : "OFF")}");

            // NOWE: przełącz sprite'y (tile'e) zamiast tintu
            ApplySunTiles(sunny);

            _wasSunny = sunny;
        }

    }

    void ApplySunTiles(bool sunny)
    {
        if (!mushroomTilemap) return;

        if (sunny)
        {
            if (sunTile == null)
            {
                Debug.LogWarning("[SunMushroomSolidSimple] sunTile nie ustawiony – nie mogę zmienić sprite'a.");
                return;
            }

            foreach (var pos in _mushroomCells)
            {
                mushroomTilemap.SetTile(pos, sunTile);
            }
        }
        else
        {
            // przywróć oryginalny układ kafelków
            foreach (var kv in _originalTiles)
            {
                mushroomTilemap.SetTile(kv.Key, kv.Value);
            }
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
