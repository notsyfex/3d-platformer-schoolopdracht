using System.IO;
using UnityEngine;

// Attach to a separate, visual-only ghost car (no player-driven physics —
// this script sets its transform directly). Disable/remove its colliders
// or make them triggers so it doesn't block the real car.
public class GhostPlayer : MonoBehaviour
{
    [Tooltip("Must match the key used by the GhostRecorder for the same track.")]
    public string ghostKey = "Track1";

    GhostRun run;
    bool hasGhost;
    int frameIndex;
    bool wasRunning;
    bool pendingReload;

    void Start()
    {
        LoadGhost();
        if (GameManager.Instance != null)
            GameManager.Instance.OnNewBestTime += RequestReload;
    }

    void OnDestroy()
    {
        if (GameManager.Instance != null)
            GameManager.Instance.OnNewBestTime -= RequestReload;
    }

    // Just flags the reload; the actual file read happens in LateUpdate so it
    // always runs after GhostRecorder's OnNewBestTime handler has written the
    // new file, regardless of Script Execution Order between the two.
    void RequestReload() => pendingReload = true;

    void LateUpdate()
    {
        if (pendingReload)
        {
            pendingReload = false;
            LoadGhost();
        }
    }

    void LoadGhost()
    {
        string path = GhostPath.Get(ghostKey);
        hasGhost = File.Exists(path);
        if (hasGhost)
        {
            run = JsonUtility.FromJson<GhostRun>(File.ReadAllText(path));
            hasGhost = run != null && run.frames.Count > 1;
        }
        gameObject.SetActive(hasGhost);
    }

    void Update()
    {
        if (!hasGhost) return;

        if (GameManager.Instance == null) return;
        bool running = GameManager.Instance.IsRunning;
        if (running && !wasRunning)
            frameIndex = 0;
        wasRunning = running;

        if (!running) return;

        float t = GameManager.Instance.ElapsedTime;
        var frames = run.frames;

        while (frameIndex < frames.Count - 2 && frames[frameIndex + 1].time < t)
            frameIndex++;

        GhostFrame a = frames[frameIndex];
        GhostFrame b = frames[Mathf.Min(frameIndex + 1, frames.Count - 1)];
        float span = Mathf.Max(0.0001f, b.time - a.time);
        float lerp = Mathf.Clamp01((t - a.time) / span);

        transform.position = Vector3.Lerp(a.position, b.position, lerp);
        transform.rotation = Quaternion.Slerp(a.rotation, b.rotation, lerp);
    }
}
