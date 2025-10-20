using UnityEngine;
using UnityEngine.Rendering.Universal;
using System.Collections.Generic;

public class WeatherPatchLocal : MonoBehaviour
{
    [Header("Area")]
    public Vector2 size = new(8, 8);

    [Header("FX (optional)")]
    public ParticleSystem rainFx;
    public ParticleSystem snowFx;
    public Light2D sunFx;
   // public Light2D localLight;

    [Header("Tuning")]
    [SerializeField] private float frictionPerMmH = 0.15f;
    [SerializeField] private float windToForce = 3.0f;
    [SerializeField] private float precipThreshold = 0.01f;
    [SerializeField] private bool enableWind = false;

    [Header("Lifetime")]
    public float lifetime = -1f;

    [Header("Fade-out (last seconds)")]
    [Tooltip("Czas miękkiego wygaszania przed końcem życia (s). 0 = brak fade-out.")]
    public float fadeOutSeconds = 0.35f;

    [Header("Singleton")]
    [SerializeField] private bool enforceSingleActive = true;

    public static WeatherPatchLocal Active { get; private set; }

    //stan
    public WeatherModel Weather { get; private set; }
    private bool _ready = false;

    private readonly HashSet<Collider2D> _inside = new();
    private readonly Dictionary<Collider2D, PhysicsMaterial2D> _origMat = new();

    // deadline i fade
    private float _dieAt = -1f;
    private float _fadeStartAt = -1f;
    private bool _fading = false;
    private float _rainBaseRate = 0f, _snowBaseRate = 0f;

    void Awake()
    {
        var box = GetComponent<BoxCollider2D>();
        box.isTrigger = true;
        box.size = size;
        _ready = false;

        // zapamiętaj bazowe wartości (jeśli są)
        if (rainFx)
        {
            var em = rainFx.emission;
            _rainBaseRate = em.rateOverTime.constant;
        }
        if (snowFx)
        {
            var em = snowFx.emission;
            _snowBaseRate = em.rateOverTime.constant;
        }
         if (sunFx)
         {
             sunFx.enabled = false;
             //sunFx.gameObject.SetActive(false);

        }
        //if (localLight) _lightBase = localLight.intensity;
    }

    void OnEnable()
    {
        _dieAt = (lifetime > 0f) ? Time.time + lifetime : -1f;

        if (_dieAt > 0f && fadeOutSeconds > 0f)
            _fadeStartAt = _dieAt - fadeOutSeconds;
        else
            _fadeStartAt = -1f;

        _fading = false;
        if (enforceSingleActive)
        {
            if (Active && Active != this)
            {
                Destroy(Active.gameObject);   // usuń poprzedni patch
            }
            Active = this;                     // ten jest teraz aktywny
        }
    }
    void OnDestroy()
    {
        if (Active == this) Active = null;     // posprzątaj referencję gdy zniknie (w tym przy lifetime)
    }
    public void Init(WeatherModel w)
    {
        if (w == null) return;
        Weather = w;
        _ready = true;
        ApplyVisuals(w);
        if (rainFx)
        {
            var em = rainFx.emission;
            _rainBaseRate = em.rateOverTime.constant;
        }
        if (snowFx)
        {
            var em = snowFx.emission;
            _snowBaseRate = em.rateOverTime.constant;
        }
        //if (localLight) _lightBase = localLight.intensity;

        Debug.Log($"[Patch] Init done for {w.sourceCity}, temp={w.temperatureC}, rain={w.precipitationMmH}, snow={w.isSnow}");
    }

    void FixedUpdate()
    {
        if (!_ready) return;
        if (!enableWind) return;
        if (Mathf.Abs(Weather.windSpeedMs) > 0.01f)
        {
            float forceX = Weather.windSpeedMs * windToForce;
            foreach (var col in _inside)
            {
                if (!col) continue;
                var rb = col.attachedRigidbody;
                if (rb) rb.AddForce(new Vector2(forceX, 0f), ForceMode2D.Force);
            }
        }
    }

    void OnTriggerEnter2D(Collider2D col)
    {
        if (!_ready || !col) return;
        _inside.Add(col);

        if (col.sharedMaterial != null)
        {
            if (!_origMat.ContainsKey(col)) _origMat[col] = col.sharedMaterial;
            var clone = new PhysicsMaterial2D($"{col.sharedMaterial.name}_patchClone");
            clone.friction   = Mathf.Clamp01(1f - Weather.precipitationMmH * frictionPerMmH);
            clone.bounciness = col.sharedMaterial.bounciness;
            col.sharedMaterial = clone;
        }
    }

    void OnTriggerExit2D(Collider2D col)
    {
        if (!col) return;
        if (_origMat.TryGetValue(col, out var orig))
        {
            col.sharedMaterial = orig;
            _origMat.Remove(col);
        }
        _inside.Remove(col);
    }

    void Update()
    {
        // --- miękkie wygaszanie przed końcem życia ---
        if (_dieAt > 0f && fadeOutSeconds > 0f && !_fading)
        {
            if (Time.time >= _fadeStartAt)
            {
                _fading = true; // zaczynamy interpolację w dół
            }
        }

        if (_fading)
        {
            float t = Mathf.InverseLerp(_fadeStartAt, _dieAt, Time.time);   // 0 → 1
            float k = 1f - Mathf.Clamp01(t);                                // 1 → 0

            // zmniejszaj emisję cząstek
            if (rainFx)
            {
                var em = rainFx.emission;
                var r = em.rateOverTime;
                r.constant = _rainBaseRate * k;
                em.rateOverTime = r;
            }
            if (snowFx)
            {
                var em = snowFx.emission;
                var r = em.rateOverTime;
                r.constant = _snowBaseRate * k;
                em.rateOverTime = r;
            }
            // ściemniaj światło lokalne
            //if (localLight) localLight.intensity = _lightBase * k;
        }

        // --- twardy koniec życia ---
        if (_dieAt > 0f && Time.time >= _dieAt)
        {
            foreach (var kv in _origMat)
                if (kv.Key) kv.Key.sharedMaterial = kv.Value;
            _origMat.Clear();
            _inside.Clear();
            Destroy(gameObject);
            return;
        }
    }

    void ApplyVisuals(WeatherModel w)
    {
        bool rainOn = w.precipitationMmH > precipThreshold && !w.isSnow;
        bool snowOn = w.precipitationMmH > precipThreshold &&  w.isSnow;
        bool sunOn = w.temperatureC >= 28f;
        ToggleFx(rainFx, rainOn, w.precipitationMmH);
        ToggleFx(snowFx, snowOn, w.precipitationMmH);
        if (sunOn) sunFx.enabled = sunOn;
        //if () localLight.intensity = Mathf.Lerp(0.6f, 1.0f, 1f - w.cloudiness01);
    }

    void ToggleFx(ParticleSystem fx, bool on, float intensity)
    {
        if (!fx) return;
        var emission = fx.emission;
        emission.enabled = on;
        var rate = emission.rateOverTime;
        rate.constant = Mathf.Clamp(50f * Mathf.Max(0.1f, intensity), 10f, 600f);
        emission.rateOverTime = rate;
        if (on) { if (!fx.isPlaying) fx.Play(true); }
        else if (fx.isPlaying) fx.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
    }
}
