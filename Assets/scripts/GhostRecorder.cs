using System.Collections.Generic;
using System.IO;
using UnityEngine;

// Attach to the real player (the object with Movement). Records the run and,
// whenever GameManager reports a new best time, saves it to disk for
// GhostPlayer to load.
public class GhostRecorder : MonoBehaviour
{
    [Tooltip("Must match the key used by the GhostPlayer for the same track.")]
    public string ghostKey = "Track1";

    [Tooltip("Seconds between recorded samples. Lower = smoother ghost, bigger file.")]
    public float recordInterval = 0.05f;

    List<GhostFrame> frames = new List<GhostFrame>();
    float recordTimer;
    bool wasRunning;
    Movement movement;

    void Start()
    {
        movement = GetComponent<Movement>();
        if (movement == null) movement = GetComponentInParent<Movement>();

        if (GameManager.Instance != null)
            GameManager.Instance.OnNewBestTime += SaveCurrentRun;
    }

    void OnDestroy()
    {
        if (GameManager.Instance != null)
            GameManager.Instance.OnNewBestTime -= SaveCurrentRun;
    }

    void Update()
    {
        if (GameManager.Instance == null) return;
        bool running = GameManager.Instance.IsRunning;

        if (running && !wasRunning)
        {
            frames.Clear();
            recordTimer = 0f;
            if (movement != null) movement.AnimTriggerBits = 0;
        }

        if (running)
        {
            recordTimer -= Time.deltaTime;
            if (recordTimer <= 0f)
            {
                var frame = new GhostFrame
                {
                    time = GameManager.Instance.ElapsedTime,
                    position = transform.position,
                    rotation = transform.rotation
                };

                if (movement != null)
                {
                    frame.animSpeed = movement.AnimSpeed;
                    frame.noInputs = movement.AnimNoInputs;
                    frame.airborne = !movement.AnimGrounded;
                    frame.triggers = movement.AnimTriggerBits;
                    movement.AnimTriggerBits = 0; // consumed by this sample
                }

                frames.Add(frame);
                recordTimer = recordInterval;
            }
        }

        wasRunning = running;
    }

    void SaveCurrentRun()
    {
        var run = new GhostRun { frames = new List<GhostFrame>(frames) };
        File.WriteAllText(GhostPath.Get(ghostKey), JsonUtility.ToJson(run));
    }
}
