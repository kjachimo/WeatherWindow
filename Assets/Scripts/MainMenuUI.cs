using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using TMPro;

public class MainMenuUI : MonoBehaviour
{
    [Header("Scenes")]
    [Tooltip("Nazwa sceny gry, którą wczytamy po Play")]
    public string gameSceneName = "Game";

    [Header("Panels")]
    public GameObject mainPanel;     
    public GameObject creditsPanel;  

    [Header("Buttons")]
    public Button playButton;
    public Button creditsButton;
    public Button quitButton;
    public Button backFromCreditsButton;

    void Start()
    {
        
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;

        if (creditsPanel) creditsPanel.SetActive(false);
        if (mainPanel) mainPanel.SetActive(true);

        
        if (playButton) playButton.onClick.AddListener(OnPlay);
        if (creditsButton) creditsButton.onClick.AddListener(OnCredits);
        if (quitButton) quitButton.onClick.AddListener(OnQuit);
        if (backFromCreditsButton) backFromCreditsButton.onClick.AddListener(OnBackFromCredits);
    }

    public void OnPlay()
    {
        if (string.IsNullOrEmpty(gameSceneName))
        {
            Debug.LogError("[MainMenu] Brak nazwy sceny gry!");
            return;
        }
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
        SceneManager.LoadScene(gameSceneName);
        
    }

    public void OnCredits()
    {
        if (mainPanel) mainPanel.SetActive(false);
        if (creditsPanel) creditsPanel.SetActive(true);
    }

    public void OnBackFromCredits()
    {
        if (creditsPanel) creditsPanel.SetActive(false);
        if (mainPanel) mainPanel.SetActive(true);
    }

    public void OnQuit()
    {
        Debug.Log("[MainMenu] Quit pressed");
        #if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
        #else
        Application.Quit();
        #endif
    }
}
