using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;

[RequireComponent(typeof(BoxCollider2D))]
[RequireComponent(typeof(Rigidbody2D))]
public class RainContainer : MonoBehaviour
{
    [Header("Tilemap (assign)")]
    public Tilemap waterTilemap;
    public TileBase waterTile;

    [Tooltip("Tile używany, gdy woda jest zamarznięta (opcjonalny).")]
    public TileBase frozenWaterTile;

    [Header("Symmetric Fill/Dry")]
    [Tooltip("Tempo zmiany poziomu (0..1) na sekundę. Deszcz -> +, Słońce -> -.")]
    public float changePerSecond = 0.08f;

    [Range(0, 1f)] public float initialFill01 = 0f;

    [Header("Thresholds")]
    [Tooltip("Opad uznany za 'brak' (mm/h)")]
    public float rainThresholdMmH = 0.01f;
    [Tooltip("Temp (°C) od której słońce 'suszy'")]
    public float hotSunTempC = 28f;
    [Tooltip("Temp (°C) poniżej/na której woda zamarza")]
    public float freezeTempC = 0f;

    [Header("Tint (prosto)")]
    public Color frozenTint = new Color(0.90f, 0.95f, 1f, 1f);
    public Color sunTint    = new Color(1.00f, 0.96f, 0.88f, 1f);

    [Header("Debug state")]
    [Range(0, 1f)] public float fill01 = 0f;
    [SerializeField] int currentRows = 0;
    [SerializeField] bool isFrozen = false;

    Vector3Int bottomLeftCell;
    int widthCells, heightCells;
    BoxCollider2D trigger;
    Grid grid;
    TilemapCollider2D waterCol;
    CompositeCollider2D compCol;
    BuoyancyEffector2D effector;

    readonly HashSet<WeatherPatchLocal> patches = new();

    void Awake()
    {
        // Root trigger + kinematic RB
        trigger = GetComponent<BoxCollider2D>();
        trigger.isTrigger = true;

        var rb = GetComponent<Rigidbody2D>();
        rb.bodyType = RigidbodyType2D.Kinematic;
        rb.gravityScale = 0;

        // Tilemap
        if (!waterTilemap) waterTilemap = GetComponentInChildren<Tilemap>();
        grid     = waterTilemap ? waterTilemap.layoutGrid : GetComponentInChildren<Grid>();
        waterCol = waterTilemap ? waterTilemap.GetComponent<TilemapCollider2D>() : null;
        compCol  = waterTilemap ? waterTilemap.GetComponent<CompositeCollider2D>() : null;
        effector = waterTilemap ? waterTilemap.GetComponent<BuoyancyEffector2D>() : null;

        if (!grid || !waterTilemap || !waterTile || !waterCol)
        {
            Debug.LogError("[Lake] Missing Grid/WaterTilemap/WaterTile/TilemapCollider2D.");
            enabled = false; return;
        }

        if (compCol)
        {
            compCol.usedByEffector = true;
            compCol.geometryType = CompositeCollider2D.GeometryType.Polygons;
        }
        if (waterCol) waterCol.isTrigger = true;
        if (effector) effector.enabled = true;

        ComputeCellsFromCollider();

        // startowy stan
        fill01 = Mathf.Clamp01(initialFill01);
        waterTilemap.ClearAllTiles();

        // narysuj wodę wg fill01
        ApplyRows(GetRowsFromFill());

        SetFrozen(false, force: true);
    }

    int GetRowsFromFill()
    {
        return Mathf.RoundToInt(fill01 * heightCells);
    }

    void ComputeCellsFromCollider()
    {
        var b  = trigger.bounds;
        var cs = grid.cellSize;

        widthCells  = Mathf.Max(1, Mathf.RoundToInt(b.size.x / Mathf.Max(0.0001f, cs.x)));
        heightCells = Mathf.Max(1, Mathf.RoundToInt(b.size.y / Mathf.Max(0.0001f, cs.y)));
        bottomLeftCell = waterTilemap.WorldToCell(new Vector3(b.min.x + 0.001f, b.min.y + 0.001f, 0));
    }

    void OnTriggerEnter2D(Collider2D col)
    {
        if (col.TryGetComponent<WeatherPatchLocal>(out var p)) patches.Add(p);
    }
    void OnTriggerExit2D(Collider2D col)
    {
        if (col.TryGetComponent<WeatherPatchLocal>(out var p)) patches.Remove(p);
    }

    void Update()
    {
        bool hasRain   = false;
        bool hotSun    = false;
        bool freezeNow = false;

        foreach (var p in patches)
        {
            if (p == null || p.Weather == null) continue;

            float precip = p.Weather.precipitationMmH;
            float tempC  = p.Weather.temperatureC;

            // deszcz: tylko deszcz i opad powyżej progu
            if (!p.Weather.isSnow && precip > rainThresholdMmH)
                hasRain = true;

            // gorące słońce: brak opadu + wysoka temp + nie-śnieg
            if (!p.Weather.isSnow && precip <= rainThresholdMmH && tempC >= hotSunTempC)
                hotSun = true;

            // mróz (śnieg lub temp <= 0)
            if (p.Weather.isSnow || tempC <= freezeTempC)
                freezeNow = true;

            if (hasRain && hotSun && freezeNow) break;
        }

        // zmiana poziomu
        float delta = 0f;
        if (hasRain) delta = +changePerSecond * Time.deltaTime;
        else if (hotSun) delta = -changePerSecond * Time.deltaTime;

        if (delta != 0f)
        {
            fill01 = Mathf.Clamp01(fill01 + delta);
            int targetRows = GetRowsFromFill();
            if (targetRows != currentRows)
            {
                ApplyRows(targetRows);
            }
        }

        SetFrozen(freezeNow);
    }

    void ApplyRows(int rows)
    {
        rows = Mathf.Clamp(rows, 0, heightCells);
        currentRows = rows;

        waterTilemap.ClearAllTiles();

        // tile zależnie od stanu zamarznięcia
        TileBase tileToUse = (isFrozen && frozenWaterTile != null) ? frozenWaterTile : waterTile;

        for (int y = 0; y < currentRows; y++)
        {
            for (int x = 0; x < widthCells; x++)
            {
                waterTilemap.SetTile(
                    new Vector3Int(bottomLeftCell.x + x, bottomLeftCell.y + y, 0),
                    tileToUse
                );
            }
        }

        // wypór na powierzchnie
        if (effector && grid)
        {
            float worldSurfaceY = (bottomLeftCell.y + currentRows) * grid.cellSize.y;
            float localSurfaceY = waterTilemap.transform
                .InverseTransformPoint(new Vector3(0f, worldSurfaceY, 0f)).y;
            effector.surfaceLevel = localSurfaceY;
        }
    }

    void SetFrozen(bool frozen, bool force = false)
    {
        if (!force && frozen == isFrozen) return;
        isFrozen = frozen;

        if (compCol) compCol.isTrigger = !frozen;
        if (waterCol) waterCol.isTrigger = !frozen; // lód = solid, woda = trigger
        if (effector) effector.enabled = !frozen;   // wypór tylko dla wody

        // przy zmianie stanu w runtime przeładuj kafelki
        if (!force)
        {
            ApplyRows(currentRows);
        }
    }
}
