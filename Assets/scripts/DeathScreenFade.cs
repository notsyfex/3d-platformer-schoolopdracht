using System;
using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Standalone death-screen fade. Put it on a full-screen UI panel (black Image) that has a CanvasGroup.
/// Call RestartScene() on death: fades to black, reloads the current scene, and the new scene's
/// copy of this object starts black and fades back in. No GameManager involved.
/// </summary>
[RequireComponent(typeof(CanvasGroup))]
public class DeathScreenFade : MonoBehaviour
{
    [SerializeField] float fadeInTime = 0.6f;    // to black
    [SerializeField] float holdTime = 0.4f;      // stay black
    [SerializeField] float fadeOutTime = 0.6f;   // back to gameplay (after reload)

    // Survives the scene reload so the new scene knows to start black.
    static bool startBlackOnLoad;

    CanvasGroup group;
    Coroutine running;

    public bool IsPlaying => running != null;

    void Awake()
    {
        group = GetComponent<CanvasGroup>();
        group.interactable = false;

        if (startBlackOnLoad)
        {
            startBlackOnLoad = false;
            group.alpha = 1f;
            group.blocksRaycasts = true;
            running = StartCoroutine(FadeOutOnly());
        }
        else
        {
            group.alpha = 0f;
            group.blocksRaycasts = false;
        }
    }

    /// <summary>Fade to black, restart the current scene, fade back in.</summary>
    public void RestartScene()
    {
        if (running != null) return;                 // ignore repeated death triggers
        running = StartCoroutine(RestartSequence());
    }

    IEnumerator RestartSequence()
    {
        group.blocksRaycasts = true;
        yield return Fade(0f, 1f, fadeInTime);
        yield return new WaitForSecondsRealtime(holdTime);

        startBlackOnLoad = true;
        Time.timeScale = 1f;                         // in case death paused the game
        SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
    }

    IEnumerator FadeOutOnly()
    {
        yield return Fade(1f, 0f, fadeOutTime);
        group.blocksRaycasts = false;
        running = null;
    }

    IEnumerator Fade(float from, float to, float duration)
    {
        if (duration <= 0f) { group.alpha = to; yield break; }

        float t = 0f;
        while (t < duration)
        {
            t += Time.unscaledDeltaTime;             // works even if timeScale is 0
            group.alpha = Mathf.Lerp(from, to, t / duration);
            yield return null;
        }
        group.alpha = to;
    }
}
