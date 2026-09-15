using System.Collections.Generic;
using UnityEngine;

[System.Serializable]
public struct GhostFrame
{
    public float time;
    public Vector3 position;
    public Quaternion rotation;
}

[System.Serializable]
public class GhostRun
{
    public List<GhostFrame> frames = new List<GhostFrame>();
}
