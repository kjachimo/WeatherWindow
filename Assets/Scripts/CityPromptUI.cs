using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class CityPromptUI : MonoBehaviour
{
    [Header("Refs")]
    public WeatherService weatherService;
    public WeatherStampSpawner spawner;
    public PlayerMovement playerMovement;

    [Header("UI")]
    public GameObject panel;
    public TMP_InputField cityInput;
    public Button applyButton;

    [Header("UX")]
    public KeyCode toggleKey = KeyCode.C;
    public string defaultCity = "Warszawa";

    bool isOpen;
    bool waitingForFetch;

    void Awake()
    {
        if (panel) panel.SetActive(false);
        if (applyButton) applyButton.onClick.AddListener(OnApply);
        /*
        // obsługa Entera
        if (cityInput)
        {
            cityInput.onSubmit.AddListener(_ => OnApply());
            cityInput.onEndEdit.AddListener(OnInputEndEdit);
        }*/
    }

    void OnEnable()
    {
        if (weatherService) weatherService.OnWeatherUpdated += OnWeatherUpdated;
    }

    void OnDisable()
    {
        if (weatherService) weatherService.OnWeatherUpdated -= OnWeatherUpdated;
    }

    void Update()
    {
        if (Input.GetKeyDown(toggleKey))
        {
            if (!isOpen) Open();
            else Close();
        }

        if (isOpen && Input.GetKeyDown(KeyCode.Escape)) Close();
    }

    void Open()
    {
        isOpen = true;
        if (panel) panel.SetActive(true);
        if (cityInput && string.IsNullOrWhiteSpace(cityInput.text)) cityInput.text = defaultCity;
        if (playerMovement) playerMovement.inputBlocked = true;
        Cursor.lockState = CursorLockMode.None; Cursor.visible = true;
        cityInput?.ActivateInputField(); cityInput?.Select();
    }

    void Close()
    {
        isOpen = false; waitingForFetch = false;
        if (panel) panel.SetActive(false);
        if (playerMovement) playerMovement.inputBlocked = false;
        Cursor.lockState = CursorLockMode.Locked; Cursor.visible = false;
    }

    void OnApply()
    {
        var txt = cityInput ? cityInput.text.Trim() : "";
        if (string.IsNullOrEmpty(txt)) return;

        var preset = BuildPreset(txt);
        if (preset != null)
        {
            Debug.Log("[CityPromptUI] Preset '" + txt + "' → spawn local patch");
            if (spawner) spawner.DropPatchHere(preset);
            Close();
            return;
        }

        if (!weatherService)
        {
            Debug.LogWarning("[CityPromptUI] Brak WeatherService");
            return;
        }
        Debug.Log("[CityPromptUI] Fetching city: " + txt);
        waitingForFetch = true;
        StartCoroutine(weatherService.FetchByCity(txt));
    }

    void OnInputEndEdit(string text)
    {
        if (isOpen && Input.GetKeyDown(KeyCode.Return))
        {
            OnApply();
        }
    }

    void OnWeatherUpdated(WeatherModel w)
    {
        if (!waitingForFetch) return;
        waitingForFetch = false;
        Debug.Log("[CityPromptUI] Weather ready: " + w.sourceCity);
        if (spawner) spawner.DropPatchHere();
        Close();
    }

    WeatherModel BuildPreset(string input)
    {
        var key = input.ToLowerInvariant();
        switch (key)
        {
            case "rain":
                return new WeatherModel {
                    temperatureC = 6f, windSpeedMs = 0f,
                    precipitationMmH = 2f, cloudiness01 = 1f,
                    isSnow = false, sourceCity = "PRESET_RAIN"
                };
            case "snow":
                return new WeatherModel {
                    temperatureC = -2f, windSpeedMs = 0f,
                    precipitationMmH = 2f, cloudiness01 = 1f,
                    isSnow = true, sourceCity = "PRESET_SNOW"
                };
            case "freeze":
                return new WeatherModel {
                    temperatureC = -5f, windSpeedMs = 0f,
                    precipitationMmH = 0f, cloudiness01 = 0.2f,
                    isSnow = false, sourceCity = "PRESET_FREEZE"
                };
            case "sun":
                return new WeatherModel {
                    temperatureC = 32f, windSpeedMs = 0f,
                    precipitationMmH = 0f, cloudiness01 = 0.05f,
                    isSnow = false, sourceCity = "PRESET_SUN"
                };
            default:
                return null;
        }
    }
}
