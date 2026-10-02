using System.IO;
using UnityEngine;

// Attach to a separate, visual-only ghost (no player-driven physics —
// this script sets its transform directly). The ghost must NOT have the
// Movement script, a CharacterController, or input components. Give it the
// same model + Animator Controller as the player, with Apply Root Motion off.
public class GhostPlayer : MonoBehaviour
{
    [Tooltip("Must match the key used by the GhostRecorder for the same track.")]
    public string ghostKey = "Track1";

    GhostRun run;
    bool hasGhost;
    int frameIndex;
    int triggerIndex;
    bool wasRunning;
    bool pendingReload;
    Animator anim;

    // Same parameter names the player's Animator Controller uses.
    static readonly int SpeedHash = Animator.StringToHash("Speed");
    static readonly int GroundedHash = Animator.StringToHash("Grounded");
    static readonly int NoInputsHash = Animator.StringToHash("NoInputs");
    static readonly int JumpHash = Animator.StringToHash("Jump");
    static readonly int LongJumpHash = Animator.StringToHash("LongJump");
    static readonly int BackflipHash = Animator.StringToHash("Backflip");

    void Start()
    {
        anim = GetComponentInChildren<Animator>();
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
        frameIndex = 0;
        triggerIndex = 0;
        gameObject.SetActive(hasGhost);
    }

    void Update()
    {
        if (!hasGhost) return;

        if (GameManager.Instance == null) return;
        bool running = GameManager.Instance.IsRunning;
        if (running && !wasRunning)
        {
            frameIndex = 0;
            triggerIndex = 0;
        }
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

        if (anim == null) return;

        anim.SetFloat(SpeedHash, Mathf.Lerp(a.animSpeed, b.animSpeed, lerp));
        anim.SetFloat(NoInputsHash, Mathf.Lerp(a.noInputs, b.noInputs, lerp));
        anim.SetBool(GroundedHash, !a.airborne);

        // Fire each recorded trigger exactly once, even if several frames
        // were skipped since the last Update.
        while (triggerIndex < frames.Count && frames[triggerIndex].time <= t)
        {
            FireTriggers(frames[triggerIndex].triggers);
            triggerIndex++;
        }
    }

    void FireTriggers(int bits)
    {
        if (bits == 0) return;
        if ((bits & 1) != 0) anim.SetTrigger(JumpHash);
        if ((bits & 2) != 0) anim.SetTrigger(LongJumpHash);
        if ((bits & 4) != 0) anim.SetTrigger(BackflipHash);
    }
}
