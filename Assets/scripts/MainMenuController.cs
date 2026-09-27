using UnityEngine;

/// <summary>
/// Put this on an object in your MainMenu scene (e.g. the same one as
/// LevelSelectController, or its own empty GameObject).
/// Wire your "quit :(" button's OnClick() to QuitGame().
/// </summary>
public class MainMenuController : MonoBehaviour
{
    public void QuitGame()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }
}
