using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// Put this on ONE empty GameObject in your first scene (e.g. "SoundManager").
// It survives scene loads. Fill the SFX / Music lists in the Inspector, then
// from any script:
//
//   SoundManager.Instance.PlaySfx("Coin");
//   SoundManager.Instance.PlayMusic("Level1");
//
public class SoundManager : MonoBehaviour
{
    public static SoundManager Instance { get; private set; }

    [Serializable]
    public class Sound
    {
        [Tooltip("Name you use in code, e.g. \"Coin\"")]
        public string name;
        public AudioClip clip;
        [Range(0f, 1f)] public float volume = 1f;
        [Tooltip("Random pitch variation, e.g. 0.1 = +/-10%. Keeps repeated sounds (coins, stomps) from sounding identical.")]
        [Range(0f, 0.5f)] public float pitchVariation = 0f;
    }

    [Header("Sounds")]
    public List<Sound> sfx = new List<Sound>();
    public List<Sound> music = new List<Sound>();

    [Header("Settings")]
    [Tooltip("How many sound effects can play at the same time.")]
    [SerializeField] private int sfxVoices = 10;
    [Range(0f, 1f)] [SerializeField] private float defaultMusicVolume = 0.6f;
    [Range(0f, 1f)] [SerializeField] private float defaultSfxVolume = 1f;

    private const string MusicVolumeKey = "MusicVolume";
    private const string SfxVolumeKey = "SfxVolume";

    private Dictionary<string, Sound> sfxLookup;
    private Dictionary<string, Sound> musicLookup;

    private AudioSource[] sfxSources;
    private int nextSfxSource;

    private AudioSource[] musicSources;
    private int activeMusic;
    private float currentMusicBaseVolume = 1f;
    private Coroutine musicFade;

    public float MusicVolume { get; private set; }
    public float SfxVolume { get; private set; }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);

        sfxLookup = BuildLookup(sfx);
        musicLookup = BuildLookup(music);

        sfxSources = new AudioSource[Mathf.Max(1, sfxVoices)];
        for (int i = 0; i < sfxSources.Length; i++)
            sfxSources[i] = CreateSource("SFX " + i, false);

        // Two music sources so one track can crossfade into the next.
        musicSources = new[] { CreateSource("Music A", true), CreateSource("Music B", true) };

        MusicVolume = PlayerPrefs.GetFloat(MusicVolumeKey, defaultMusicVolume);
        SfxVolume = PlayerPrefs.GetFloat(SfxVolumeKey, defaultSfxVolume);
    }

    private AudioSource CreateSource(string sourceName, bool loop)
    {
        var go = new GameObject(sourceName);
        go.transform.SetParent(transform);
        var source = go.AddComponent<AudioSource>();
        source.playOnAwake = false;
        source.loop = loop;
        source.spatialBlend = 0f; // 2D
        return source;
    }

    private static Dictionary<string, Sound> BuildLookup(List<Sound> list)
    {
        var dict = new Dictionary<string, Sound>();
        foreach (var s in list)
        {
            if (s == null || string.IsNullOrEmpty(s.name)) continue;
            if (dict.ContainsKey(s.name))
                Debug.LogWarning($"SoundManager: duplicate sound name '{s.name}' — the later one is ignored.");
            else
                dict.Add(s.name, s);
        }
        return dict;
    }

    // ---------------------------------------------------------------- SFX

    /// <summary>Plays a sound effect by name (2D, same volume everywhere).</summary>
    public void PlaySfx(string soundName)
    {
        if (!sfxLookup.TryGetValue(soundName, out Sound s) || s.clip == null)
        {
            Debug.LogWarning($"SoundManager: no SFX named '{soundName}' (or it has no clip).");
            return;
        }

        AudioSource source = GetFreeSfxSource();
        source.pitch = 1f + UnityEngine.Random.Range(-s.pitchVariation, s.pitchVariation);
        source.PlayOneShot(s.clip, s.volume * SfxVolume);
    }

    /// <summary>Plays a sound effect at a spot in the world (3D falloff), e.g. an enemy dying.</summary>
    public void PlaySfxAt(string soundName, Vector3 position)
    {
        if (!sfxLookup.TryGetValue(soundName, out Sound s) || s.clip == null)
        {
            Debug.LogWarning($"SoundManager: no SFX named '{soundName}' (or it has no clip).");
            return;
        }

        AudioSource.PlayClipAtPoint(s.clip, position, s.volume * SfxVolume);
    }

    private AudioSource GetFreeSfxSource()
    {
        // Prefer a voice that's idle; if all are busy, steal the next one round-robin.
        for (int i = 0; i < sfxSources.Length; i++)
        {
            int index = (nextSfxSource + i) % sfxSources.Length;
            if (!sfxSources[index].isPlaying)
            {
                nextSfxSource = (index + 1) % sfxSources.Length;
                return sfxSources[index];
            }
        }

        AudioSource stolen = sfxSources[nextSfxSource];
        nextSfxSource = (nextSfxSource + 1) % sfxSources.Length;
        return stolen;
    }

    // -------------------------------------------------------------- Music

    /// <summary>Starts a looping music track, crossfading from whatever is playing.</summary>
    public void PlayMusic(string trackName, float fadeTime = 1f)
    {
        if (!musicLookup.TryGetValue(trackName, out Sound s) || s.clip == null)
        {
            Debug.LogWarning($"SoundManager: no music named '{trackName}' (or it has no clip).");
            return;
        }

        AudioSource current = musicSources[activeMusic];
        if (current.isPlaying && current.clip == s.clip) return; // already playing

        int nextIndex = 1 - activeMusic;
        AudioSource next = musicSources[nextIndex];
        next.clip = s.clip;
        next.volume = 0f;
        next.Play();

        activeMusic = nextIndex;
        currentMusicBaseVolume = s.volume;

        if (musicFade != null) StopCoroutine(musicFade);
        musicFade = StartCoroutine(Crossfade(current, next, fadeTime));
    }

    public void StopMusic(float fadeTime = 1f)
    {
        if (musicFade != null) StopCoroutine(musicFade);
        musicFade = StartCoroutine(Crossfade(musicSources[activeMusic], null, fadeTime));
    }

    private IEnumerator Crossfade(AudioSource from, AudioSource to, float duration)
    {
        float fromStart = from.volume;
        float t = 0f;

        while (t < duration)
        {
            t += Time.unscaledDeltaTime; // keeps working if you pause with timeScale = 0
            float k = Mathf.Clamp01(t / duration);
            from.volume = Mathf.Lerp(fromStart, 0f, k);
            if (to != null) to.volume = Mathf.Lerp(0f, currentMusicBaseVolume * MusicVolume, k);
            yield return null;
        }

        from.Stop();
        from.volume = 0f;
        if (to != null) to.volume = currentMusicBaseVolume * MusicVolume;
        musicFade = null;
    }

    // ------------------------------------------------------------ Volume

    /// <summary>0..1. Saved between sessions. Hook this up to a settings slider.</summary>
    public void SetMusicVolume(float value)
    {
        MusicVolume = Mathf.Clamp01(value);
        PlayerPrefs.SetFloat(MusicVolumeKey, MusicVolume);

        if (musicFade == null)
            musicSources[activeMusic].volume = currentMusicBaseVolume * MusicVolume;
    }

    /// <summary>0..1. Saved between sessions. Applies to sounds played after the change.</summary>
    public void SetSfxVolume(float value)
    {
        SfxVolume = Mathf.Clamp01(value);
        PlayerPrefs.SetFloat(SfxVolumeKey, SfxVolume);
    }
}
