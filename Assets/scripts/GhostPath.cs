using System.IO;
using UnityEngine;

public static class GhostPath
{
    public static string Get(string ghostKey) =>
        Path.Combine(Application.persistentDataPath, $"ghost_{ghostKey}.json");
}
