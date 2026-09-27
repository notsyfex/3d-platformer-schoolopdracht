using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Put this on an object inside your Level Select panel.
/// - Drag your "level 1"/"level 2"/"level 3" GameObjects (the ones shown in
///   the Hierarchy) into the Level Panels list below, in order.
/// - Wire your left/right arrow buttons' OnClick() to Previous()/Next().
/// - Wire your Play button's OnClick() to LoadCurrentLevel().
/// - Wire your Back button's OnClick() to BackToMainMenu().
/// </summary>
public class LevelSelectController : MonoBehaviour
{
    [Tooltip("The level 1 / level 2 / level 3 GameObjects, in order. Only one is shown at a time.")]
    [SerializeField] private GameObject[] levelPanels;

    [Tooltip("Exact scene name to load for each entry above, same order (e.g. \"level 1\", \"level 2\", \"level 3\").")]
    [SerializeField] private string[] levelSceneNames;

    [Tooltip("Scene to return to when Back is pressed.")]
    [SerializeField] private string mainMenuSceneName = "MainMenu";

    private int currentIndex = 0;

    private void OnEnable()
    {
        currentIndex = 0;
        ShowCurrent();
    }

    /// <summary>Wire to your right arrow button's OnClick().</summary>
    public void Next()
    {
        currentIndex = (currentIndex + 1) % levelPanels.Length;
        ShowCurrent();
    }

    /// <summary>Wire to your left arrow button's OnClick().</summary>
    public void Previous()
    {
        currentIndex = (currentIndex - 1 + levelPanels.Length) % levelPanels.Length;
        ShowCurrent();
    }

    /// <summary>Wire to your Play button's OnClick() to load whichever level is currently shown.</summary>
    public void LoadCurrentLevel()
    {
        if (levelSceneNames == null || currentIndex >= levelSceneNames.Length) return;
        SceneManager.LoadScene(levelSceneNames[currentIndex]);
    }

    /// <summary>Call from a level button's OnClick() directly, if you're not using the arrow-cycling flow.</summary>
    public void LoadLevel(string sceneName)
    {
        SceneManager.LoadScene(sceneName);
    }

    /// <summary>Call from the Back button's OnClick().</summary>
    public void BackToMainMenu()
    {
        SceneManager.LoadScene(mainMenuSceneName);
    }

    private void ShowCurrent()
    {
        for (int i = 0; i < levelPanels.Length; i++)
        {
            if (levelPanels[i] != null)
                levelPanels[i].SetActive(i == currentIndex);
        }
    }
}
