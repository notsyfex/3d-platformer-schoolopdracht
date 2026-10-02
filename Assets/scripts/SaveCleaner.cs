using System.Collections;
using System.IO;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Full save wipe: destroys the persistent GameManager (which still holds the old data in memory),
/// deletes PlayerPrefs + everything under Application.persistentDataPath, then reloads the scene
/// so a fresh GameManager loads the now-empty saves. Requires two clicks to avoid accidents.
/// </summary>
public class SaveCleaner : MonoBehaviour
{
    [SerializeField] Button cleanButton;
    [SerializeField] TMP_Text buttonLabel;               // optional (use TMP_Text instead if you use TextMeshPro)
    [SerializeField] float confirmWindow = 3f;       // seconds to confirm
    [SerializeField] UnityEvent onCleared;           // optional extra hooks
    [SerializeField] bool reloadSceneAfterClean = true;
    [SerializeField] GameObject[] destroyBeforeReload; // any OTHER DontDestroyOnLoad objects holding save data (GameManager is handled automatically)

    const string IdleText = "Clean Save Data";
    const string ConfirmText = "Are you sure? Click again";

    bool armed;
    bool cleaning;
    Coroutine disarmRoutine;

    void Awake()
    {
        if (cleanButton == null) cleanButton = GetComponent<Button>();
        cleanButton.onClick.AddListener(OnClick);
        SetLabel(IdleText);
    }

    void OnClick()
    {
        if (!armed)
        {
            armed = true;
            SetLabel(ConfirmText);
            disarmRoutine = StartCoroutine(Disarm());
            return;
        }

        if (disarmRoutine != null) StopCoroutine(disarmRoutine);
        armed = false;
        SetLabel(IdleText);
        CleanAll();
    }

    IEnumerator Disarm()
    {
        yield return new WaitForSecondsRealtime(confirmWindow);
        armed = false;
        SetLabel(IdleText);
    }

    public void CleanAll()
    {
        if (!cleaning) StartCoroutine(CleanRoutine());
    }

    IEnumerator CleanRoutine()
    {
        cleaning = true;

        // 1. Destroy the objects that hold the OLD data in memory. The persistent GameManager
        //    survives scene reloads, which is why the menu kept showing old stats.
        if (GameManager.Instance != null) Destroy(GameManager.Instance.gameObject);
        foreach (GameObject go in destroyBeforeReload)
            if (go != null) Destroy(go);

        // Wait one frame so their OnDestroy/OnDisable run BEFORE we wipe,
        // otherwise anything that saves on destroy would just rewrite the old data.
        yield return null;

        // 2. PlayerPrefs
        PlayerPrefs.DeleteAll();
        PlayerPrefs.Save();

        // 3. Files on disk (ghost JSONs, custom save files)
        string root = Application.persistentDataPath;
        try
        {
            foreach (string file in Directory.GetFiles(root))
                File.Delete(file);
            foreach (string dir in Directory.GetDirectories(root))
                Directory.Delete(dir, true);
        }
        catch (IOException e)
        {
            Debug.LogWarning("SaveCleaner: could not delete everything: " + e.Message);
        }

        Debug.Log("SaveCleaner: all save data deleted from " + root);
        onCleared?.Invoke();

        // 4. Reload so a fresh GameManager loads the empty saves
        if (reloadSceneAfterClean)
            SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);

        cleaning = false;
    }

    void SetLabel(string text)
    {
        if (buttonLabel != null) buttonLabel.text = text;
    }
}
