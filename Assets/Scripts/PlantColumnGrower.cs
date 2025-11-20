using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;

[RequireComponent(typeof(BoxCollider2D))]
[RequireComponent(typeof(Rigidbody2D))]
public class PlantColumnGrower : MonoBehaviour
{
    [Header("Tilemap docelowa (gdzie rysujemy roślinę)")]
    public Tilemap plantTilemap;
    public TileBase plantTile;

    [Header("Wzrost")]
    [Tooltip("Ile komórek na sekundę rośnie, gdy pada")]
    public float growCellsPerSecond = 1.0f;

    [Tooltip("Startowa liczba rzędów (np. 0 = nasiono)")]
    public int initialRows = 0;

    [Tooltip("Maksymalna wysokość w komórkach (wyliczana z BoxCollidera, ale można nadpisać >0)")]
    public int overrideMaxHeight = 0;

    [Tooltip("Czy obszar wzrostu ma być czytany z szerokości BoxCollidera")]
    public bool useColliderWidth = true;

    [Tooltip("Jeśli false, rosnij tylko 1 kolumnę; jeśli true, rośnij na całą szerokość kolajdera")]
    public bool growFullWidth = true;

    [Header("Aktualizacja kafli")]
    [Tooltip("Co ile sekund aktualizować Tilemap (optymalizacja)")]
    public float updateInterval = 0.05f;

    // stan
    [SerializeField] int currentRows = 0;
    [SerializeField] int maxHeightCells = 1;
    [SerializeField] int widthCells = 1;
    Vector3Int bottomLeftCell;

    // refs
    BoxCollider2D trigger;
    Grid grid;

    //akumulatory
    float growAccumulatorRows = 0f;
    float updAccum = 0f;

    readonly HashSet<WeatherPatchLocal> patches = new();

    void Awake()
    {
        trigger = GetComponent<BoxCollider2D>();
        trigger.isTrigger = true;

        var rb = GetComponent<Rigidbody2D>();
        rb.bodyType = RigidbodyType2D.Kinematic;
        rb.gravityScale = 0;

        if (!plantTilemap)
        {
            plantTilemap = GetComponentInChildren<Tilemap>();
        }
        grid = plantTilemap ? plantTilemap.layoutGrid : GetComponentInChildren<Grid>();

        if (!grid || !plantTilemap || !plantTile)
        {
            Debug.LogError("[Plant] Brakuje Grid/plantTilemap/plantTile.");
            enabled = false; return;
        }

        ComputeCellsFromCollider();

        currentRows = Mathf.Clamp(initialRows, 0, maxHeightCells);
        if (currentRows > 0) DrawRowsAdditive(currentRows);
    }

    void ComputeCellsFromCollider()
    {
        var b = trigger.bounds;          
        var cs = grid.cellSize;          

        maxHeightCells = overrideMaxHeight > 0
            ? overrideMaxHeight
            : Mathf.Max(1, Mathf.RoundToInt(b.size.y / Mathf.Max(0.0001f, cs.y)));

        widthCells = useColliderWidth
            ? Mathf.Max(1, Mathf.RoundToInt(b.size.x / Mathf.Max(0.0001f, cs.x)))
            : 1;

        bottomLeftCell = plantTilemap.WorldToCell(new Vector3(b.min.x + 0.001f, b.min.y + 0.001f, 0));

        // Debug.Log($"[Plant] BL={bottomLeftCell}, size={widthCells}x{maxHeightCells}");
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
        // Czy pada w obszarze
        bool hasRain = false;
        foreach (var p in patches)
        {
            if (p == null || p.Weather == null) continue;
            // rośliny reagują na deszcz, nie na śnieg
            if (!p.Weather.isSnow && p.Weather.precipitationMmH > 0.01f)
            {
                hasRain = true;
                break;
            }
        }

        if (hasRain && currentRows < maxHeightCells)
        {
            growAccumulatorRows += growCellsPerSecond * Time.deltaTime;

            
            int deltaWholeRows = Mathf.FloorToInt(growAccumulatorRows);
            if (deltaWholeRows > 0)
            {
                int target = Mathf.Min(maxHeightCells, currentRows + deltaWholeRows);
                
                updAccum += Time.deltaTime;
                if (updAccum >= updateInterval || target >= maxHeightCells)
                {
                    DrawRowsAdditive(target);
                    currentRows = target;
                    growAccumulatorRows -= deltaWholeRows;
                    updAccum = 0f;
                }
            }
        }
        else
        {
            
            updAccum = 0f;
        }
    }

    void DrawRowsAdditive(int targetRows)
    {
        if (!plantTilemap || !plantTile) return;

        for (int y = currentRows; y < targetRows; y++)
        {
            int rowY = bottomLeftCell.y + y;
            if (growFullWidth)
            {
                for (int x = 0; x < widthCells; x++)
                {
                    plantTilemap.SetTile(new Vector3Int(bottomLeftCell.x + x, rowY, 0), plantTile);
                }
            }
            else
            {
                // tylko jedna kolumna
                int midX = bottomLeftCell.x + Mathf.Max(0, (widthCells - 1) / 2);
                plantTilemap.SetTile(new Vector3Int(midX, rowY, 0), plantTile);
            }
        }
    }
}
