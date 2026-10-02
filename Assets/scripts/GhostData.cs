using System.Collections.Generic;
using UnityEngine;

[System.Serializable]
public struct GhostFrame
{
    public float time;
    public Vector3 position;
    public Quaternion rotation;

    // Animator inputs. Old saved ghosts don't have these (they load as zeros:
    // idle, grounded) until a new best time overwrites them.
    public float animSpeed;
    public float noInputs;
    public bool airborne;
    public int triggers;   // bit flags: 1 = Jump, 2 = LongJump, 4 = Backflip
}

[System.Serializable]
public class GhostRun
{
    public List<GhostFrame> frames = new List<GhostFrame>();
}
