using UnityEngine;
 // tylko jeśli używasz Light2D (może być null)

public class EnvironmentController : MonoBehaviour {
    [Header("Źródło pogody")]
    [SerializeField] private WeatherService weatherService;

    [Header("Elementy sceny")]
    [SerializeField] private PhysicsMaterial2D groundMaterial; // ten sam, co przypięty do colliderów podłoża
    [SerializeField] private ParticleSystem rainFx;
    [SerializeField] private ParticleSystem snowFx;
    [SerializeField] private UnityEngine.Rendering.Universal.Light2D globalLight2D;
    [SerializeField] private Rigidbody2D playerRb;             // gracz (Dynamic)

    [Header("Parametry reagowania")]
    [SerializeField, Tooltip("Spadek tarcia na 1 mm/h opadu")]
    private float frictionPerMmH = 0.15f;
    //[SerializeField, Tooltip("Mnożnik siły wiatru -> siła w grze")]
    //private float windToForce = 3.0f;
    [SerializeField, Tooltip("Minimalny opad, który włącza VFX")]
    private float precipThreshold = 0.01f;

    private void OnEnable() {
        if (weatherService != null) weatherService.OnWeatherUpdated += Apply;
    }
    private void OnDisable() {
        if (weatherService != null) weatherService.OnWeatherUpdated -= Apply;
    }
    private void Start() {
        var cached = weatherService ? weatherService.GetCached() : null;
        if (cached != null) Apply(cached);
    }

    public void Apply(WeatherModel w) {
        float intensity = Mathf.Max(0.1f, w.precipitationMmH);

        // VFX
        ToggleFx(rainFx, w.precipitationMmH > precipThreshold && !w.isSnow, intensity);
        ToggleFx(snowFx, w.precipitationMmH > precipThreshold &&  w.isSnow, intensity);

        // Tarcie podłoża
        if (groundMaterial != null) {
            float targetFriction = Mathf.Clamp01(1f - w.precipitationMmH * frictionPerMmH);
            groundMaterial.friction = Mathf.Clamp(targetFriction, 0.05f, 1f);
        }

        // Wiatr
        /*
        if (playerRb != null) {
            float forceX = w.windSpeedMs * windToForce;
            playerRb.AddForce(new Vector2(forceX, 0f), ForceMode2D.Force);
        }

        // Zachmurzenie → światło (opcjonalne)
        if (globalLight2D != null) {
            globalLight2D.intensity = Mathf.Lerp(0.6f, 1.0f, 1f - w.cloudiness01);
        }*/

        Debug.Log($"[Environment] apply: city={w.sourceCity}, precip={w.precipitationMmH:F2}, snow={w.isSnow}, wind={w.windSpeedMs:F1}, clouds={w.cloudiness01:F2}");
    }

    private void ToggleFx(ParticleSystem fx, bool on, float intensity) {
        if (!fx) return;
        var emission = fx.emission;
        emission.enabled = on;

        // gęstość = ~50 cząstek na 1 mm/h (min 10, max 600)
        var rate = emission.rateOverTime;
        rate.constant = Mathf.Clamp(50f * intensity, 10f, 600f);
        emission.rateOverTime = rate;

        if (on) {
            if (!fx.isPlaying) fx.Play(true);
        } else if (fx.isPlaying) {
            fx.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        }
    }

    
}
