using UnityEngine;

public class WeatherStampSpawner : MonoBehaviour
{
    [Header("Refs")]
    [SerializeField] private WeatherService weatherService;
    [SerializeField] private WeatherPatchLocal patchPrefab;

    [Header("Patch params")]
    [SerializeField] private Vector2 patchSize = new Vector2(8f, 8f);
    [SerializeField] private float patchLifetime = -1f; // -1 = nieskończony

    /// <summary>
    /// Stawia patch w miejscu gracza na podstawie aktualnego cache'u z WeatherService.
    /// </summary>
    public void DropPatchHere()
    {
        var cached = weatherService ? weatherService.GetCached() : null;
        if (cached == null)
        {
            Debug.LogWarning("[Spawner] No cached weather");
            return;
        }
        DropPatchHere(cached);
    }

    /// <summary>
    /// Stawia patch w miejscu gracza na podstawie podanego modelu (np. preset Rain/Snow/Freeze).
    /// </summary>
    public void DropPatchHere(WeatherModel model)
    {
        if (patchPrefab == null)
        {
            Debug.LogError("[Spawner] Patch Prefab NOT assigned");
            return;
        }
        if (model == null)
        {
            Debug.LogError("[Spawner] Weather model is null");
            return;
        }

        var patch = Instantiate(patchPrefab, transform.position, Quaternion.identity);
        patch.size = patchSize;
        patch.lifetime = patchLifetime;
        patch.Init(model);

        Debug.Log($"[Spawner] Patch spawned @ {transform.position} | {model.sourceCity} (T={model.temperatureC}°C, rain={model.precipitationMmH}, snow={model.isSnow})");
    }
}
