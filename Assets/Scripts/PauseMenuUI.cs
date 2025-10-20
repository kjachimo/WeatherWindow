using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public class PauseMenuUI : MonoBehaviour
{
    [Header("UI")]
    [SerializeField] private GameObject panelRoot;
    [SerializeField] private Button resumeButton;
    [SerializeField] private Button restartButton;
    [SerializeField] private Button quitButton;

    [Header("Opcje")]
    [SerializeField] private bool pauseTime = true;
    [SerializeField] private KeyCode toggleKey = KeyCode.Escape;

    [Header("Opcjonalnie")]
    [SerializeField] private PlayerMovement playerMovement;

    private bool isPaused = false;

    private CursorLockMode _prevLock;
    private bool _prevVisible;

    void Awake()
    {
        if (panelRoot) panelRoot.SetActive(false);
        if (resumeButton)  resumeButton.onClick.AddListener(Resume);
        if (restartButton) restartButton.onClick.AddListener(Restart);
        if (quitButton)    quitButton.onClick.AddListener(QuitGame);
    }

    void Update()
    {
        if (Input.GetKeyDown(toggleKey))
        {
            if (isPaused) Resume();
            else Pause();
        }
    }

    public void Pause()
    {
        if (isPaused) return;

        isPaused = true;
        if (panelRoot) panelRoot.SetActive(true);

        if (pauseTime) Time.timeScale = 0f;
        if (playerMovement) playerMovement.inputBlocked = true;

        // zapamiętaj stan kursora i pokaż go
        _prevLock = Cursor.lockState;
        _prevVisible = Cursor.visible;
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }

    public void Resume()
    {
        if (!isPaused) return;

        isPaused = false;
        if (panelRoot) panelRoot.SetActive(false);

        if (pauseTime) Time.timeScale = 1f;
        if (playerMovement) playerMovement.inputBlocked = false;

        // przywróć poprzedni stan kursora
        Cursor.lockState = _prevLock;
        Cursor.visible = _prevVisible;
    }

    public void Restart()
    {
        if (pauseTime) Time.timeScale = 1f;
        if (playerMovement) playerMovement.inputBlocked = false;

        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;

        SceneManager.LoadScene(SceneManager.GetActiveScene().name);
    }

    public void QuitGame()
    {
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;

#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }
}
