using UnityEngine;
using UnityEngine.SceneManagement;

public class LevelEndTrigger : MonoBehaviour
{
    [Header("Panel UI zakończenia")]
    public GameObject endPanel;

    [Header("Nazwa sceny z demem (do restartu)")]
    public string demoSceneName = "SampleScene";

    [Header("Ustawienia")]
    public bool pauseOnEnd = true;

    [Header("Opcjonalnie")]
    public PlayerMovement playerMovement; // jeśli chcesz zablokować ruch

    private bool _active = false;

    // zapamiętany stan kursora
    private CursorLockMode _prevLock;
    private bool _prevVisible;

    void Start()
    {
        if (endPanel != null)
            endPanel.SetActive(false);
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (_active) return;

        if (other.CompareTag("Player"))
        {
            _active = true;
            ShowEndPanel();
        }
    }

    void ShowEndPanel()
    {
        if (!endPanel)
        {
            Debug.LogError("[LevelEndController] Brak przypisanego panelu UI!");
            return;
        }

        endPanel.SetActive(true);

        if (pauseOnEnd) Time.timeScale = 0f;

        if (playerMovement) playerMovement.inputBlocked = true;

        // zapamiętaj i ustaw kursor
        _prevLock = Cursor.lockState;
        _prevVisible = Cursor.visible;
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }

    //przyciski

    public void OnPlayAgain()
    {
        if (pauseOnEnd) Time.timeScale = 1f;
        if (playerMovement) playerMovement.inputBlocked = false;
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;

        SceneManager.LoadScene(demoSceneName);
    }

    public void OnQuit()
    {
        Cursor.lockState = _prevLock;
        Cursor.visible = _prevVisible;

#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }
    public void OnCloseEndPanel()
    {
        if (!endPanel) return;

        endPanel.SetActive(false);

        if (pauseOnEnd) Time.timeScale = 1f;
        if (playerMovement) playerMovement.inputBlocked = false;

        Cursor.lockState = _prevLock;
        Cursor.visible = _prevVisible;

        _active = false;
    }
}
