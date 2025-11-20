using UnityEngine;

[RequireComponent(typeof(Collider2D))]
public class RFPickup : MonoBehaviour
{
    [Tooltip("Referencja do RainFinderOverlay. Jeśli puste, skrypt spróbuje znaleźć go w scenie.")]
    public RainFinderOverlay rainFinder;

    [Tooltip("Tag obiektu gracza, który może podnieść kryształek.")]
    public string playerTag = "Player";



    private void Reset()
    {
        // collider jako trigger
        var col = GetComponent<Collider2D>();
        col.isTrigger = true;
    }

    private void Awake()
    {
        if (!rainFinder)
        {
            rainFinder = FindObjectOfType<RainFinderOverlay>();
            if (!rainFinder)
            {
                Debug.LogWarning("[RainCrystalPickup] RainFinderOverlay not found in scene in Awake(). Ustaw referencję w Inspectorze.");
            }
        }
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        Debug.Log($"[RainCrystalPickup] OnTriggerEnter2D with {other.name}, tag={other.tag}");

        if (!string.IsNullOrEmpty(playerTag) && !other.CompareTag(playerTag))
        {
            Debug.Log("[RainCrystalPickup] Obiekt nie ma tagu gracza, ignoruję.");
            return;
        }

        // referencja do overlayu
        if (!rainFinder)
        {
            rainFinder = FindObjectOfType<RainFinderOverlay>();
        }

        if (rainFinder != null)
        {
            Debug.Log("[RainCrystalPickup] Activating RainFinderOverlay via EnableRainFinder().");
            rainFinder.EnableRainFinder();
        }
        else
        {
            Debug.LogWarning("[RainCrystalPickup] Brak RainFinderOverlay w scenie, nie mogę aktywować RainFinder.");
        }

        // Usuń kryształek po podniesieniu
        Destroy(gameObject);
    }
}
