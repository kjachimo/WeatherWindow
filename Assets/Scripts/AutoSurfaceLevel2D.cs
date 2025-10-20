using UnityEngine;

[RequireComponent(typeof(BuoyancyEffector2D))]
public class AutoSurfaceLevel2D : MonoBehaviour
{
    [Tooltip("Korekta, gdy grafika tafli nie pokrywa się z koliderem.")]
    public float surfaceOffset = 0f;

    BuoyancyEffector2D eff;
    Collider2D col;

    void Awake()
    {
        eff = GetComponent<BuoyancyEffector2D>();
        col = GetComponent<CompositeCollider2D>() ?? GetComponent<Collider2D>();

        if (col != null)
        {
            col.isTrigger = true;
            col.usedByEffector = true;
        }
    }

    void LateUpdate()
    {
        if (col == null) return;
        eff.surfaceLevel = col.bounds.max.y + surfaceOffset;
    }

#if UNITY_EDITOR
    void OnValidate()
    {
        if (!eff) eff = GetComponent<BuoyancyEffector2D>();
        if (!col) col = GetComponent<CompositeCollider2D>() ?? GetComponent<Collider2D>();
        if (col != null) col.usedByEffector = true;
        if (col != null) col.isTrigger = true;
    }
#endif
}
