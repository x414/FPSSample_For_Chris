using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

public class SinglePlayerVoiceAnnouncer : MonoBehaviour
{
    public enum VoicePriority
    {
        Normal,
        High,
        Critical
    }

    struct VoiceCueDefinition
    {
        public VoicePriority Priority;
        public float Cooldown;

        public VoiceCueDefinition(VoicePriority priority, float cooldown)
        {
            Priority = priority;
            Cooldown = cooldown;
        }
    }

    static readonly Dictionary<string, VoiceCueDefinition> CueDefinitions = new Dictionary<string, VoiceCueDefinition>
    {
        { "PlayerDamaged", new VoiceCueDefinition(VoicePriority.Normal, 3f) },
        { "HealthCritical", new VoiceCueDefinition(VoicePriority.Critical, 8f) },
        { "PlayerDown", new VoiceCueDefinition(VoicePriority.Critical, 3f) },
        { "LastLife", new VoiceCueDefinition(VoicePriority.Critical, 10f) },
        { "RespawnReady", new VoiceCueDefinition(VoicePriority.Normal, 4f) },
        { "AmmoLow", new VoiceCueDefinition(VoicePriority.Normal, 8f) },
        { "AmmoEmpty", new VoiceCueDefinition(VoicePriority.High, 5f) },
        { "Reloaded", new VoiceCueDefinition(VoicePriority.Normal, 2f) },
        { "SniperScoped", new VoiceCueDefinition(VoicePriority.Normal, 5f) },
        { "RocketReady", new VoiceCueDefinition(VoicePriority.Normal, 5f) },
        { "EnemyDestroyed", new VoiceCueDefinition(VoicePriority.Normal, 2f) },
        { "HunterDestroyed", new VoiceCueDefinition(VoicePriority.Normal, 2f) },
        { "TacticianDestroyed", new VoiceCueDefinition(VoicePriority.Normal, 2f) },
        { "Combo3", new VoiceCueDefinition(VoicePriority.Normal, 2f) },
        { "Combo5", new VoiceCueDefinition(VoicePriority.High, 2f) },
        { "ComboBroken", new VoiceCueDefinition(VoicePriority.Normal, 4f) },
        { "LastEnemy", new VoiceCueDefinition(VoicePriority.High, 10f) },
        { "A2HunterDetected", new VoiceCueDefinition(VoicePriority.Normal, 8f) },
        { "A3Retreating", new VoiceCueDefinition(VoicePriority.Normal, 8f) },
        { "ThreeEnemiesRemaining", new VoiceCueDefinition(VoicePriority.Normal, 5f) },
        { "FiveEnemiesRemaining", new VoiceCueDefinition(VoicePriority.Normal, 5f) },
        { "TwoMinuteWarning", new VoiceCueDefinition(VoicePriority.High, 15f) },
        { "OneMinuteWarning", new VoiceCueDefinition(VoicePriority.High, 15f) },
        { "TenSecondCountdown", new VoiceCueDefinition(VoicePriority.Critical, 15f) },
        { "NextWaveInFive", new VoiceCueDefinition(VoicePriority.Normal, 5f) },
        { "Score1000", new VoiceCueDefinition(VoicePriority.Normal, 30f) },
        { "Score5000", new VoiceCueDefinition(VoicePriority.Normal, 30f) },
        { "Score10000", new VoiceCueDefinition(VoicePriority.Normal, 30f) }
    };

    static readonly string[] NumberNames =
    {
        "Zero", "One", "Two", "Three", "Four", "Five", "Six", "Seven", "Eight",
        "Nine", "Ten", "Eleven", "Twelve", "Thirteen", "Fourteen", "Fifteen",
        "Sixteen", "Seventeen", "Eighteen", "Nineteen", "Twenty"
    };

    readonly List<VoiceCue> m_Cues = new List<VoiceCue>();
    readonly Queue<string> m_PendingCues = new Queue<string>();
    readonly Dictionary<string, float> m_NextAllowedCueTime = new Dictionary<string, float>();
    readonly List<AudioSource> m_Sources = new List<AudioSource>();
    readonly List<RobotVoiceFilter> m_Filters = new List<RobotVoiceFilter>();
    readonly List<bool> m_OutputLogged = new List<bool>();
    readonly float[] m_OutputBuffer = new float[1024];
    bool m_PlayingSequence;
    double m_BusyUntilDspTime;

    public void AnnounceWave(int wave, int robotCount)
    {
        m_PendingCues.Clear();
        m_Cues.Clear();
        Enqueue("Wave", 1f, 0.3f, 0f);
        EnqueueNumber(wave, 0.05f);
        EnqueueNumber(robotCount, 0.15f);
        Enqueue("Robots", 1f, 0.3f, 0f);
        Schedule();
    }

    public void AnnounceCue(string cue)
    {
        VoiceCueDefinition definition;
        if (!CueDefinitions.TryGetValue(cue, out definition))
            definition = new VoiceCueDefinition(VoicePriority.Normal, 1.5f);

        AnnounceCue(cue, definition.Priority, definition.Cooldown);
    }

    public void AnnounceCue(string cue, VoicePriority priority, float cooldown)
    {
        var currentTime = Time.time;
        float nextAllowedTime;
        if (m_NextAllowedCueTime.TryGetValue(cue, out nextAllowedTime) && currentTime < nextAllowedTime)
            return;

        m_NextAllowedCueTime[cue] = currentTime + cooldown;
        if (priority == VoicePriority.Critical)
        {
            m_PendingCues.Clear();
            PlayCue(cue);
            return;
        }

        if (!m_PendingCues.Contains(cue))
            m_PendingCues.Enqueue(cue);
    }

    public void AnnounceGameOver(string reason)
    {
        switch (reason)
        {
            case "Victory!":
                AnnounceCue("Victory");
                break;
            case "Out of lives!":
                AnnounceCue("OutOfLives");
                break;
            case "Time's up!":
                AnnounceCue("TimesUp");
                break;
            default:
                AnnounceCue("GameOver");
                break;
        }
    }

    public void AnnouncePowerup(PowerupType type)
    {
        AnnounceCue("Powerup" + type);
    }

    public void AnnounceTestSequence()
    {
        m_PendingCues.Clear();
        m_Cues.Clear();
        Enqueue("Wave", 1f, 0.3f, 0f);
        Enqueue("One", 1.4f, 0.05f, 0.05f);
        Enqueue("Four", 1.4f, 0.05f, 0.15f);
        Enqueue("Robots", 1f, 0.3f, 0f);
        Enqueue("WaveCleared", 1f, 0.3f, 0.5f);
        Enqueue("AIBattleStart", 1f, 0.3f, 0.5f);
        Enqueue("ExploreStart", 1f, 0.3f, 0.5f);
        Enqueue("TenMinuteWarning", 1f, 0.3f, 0.5f);
        Enqueue("Victory", 1f, 0.3f, 0.5f);
        Enqueue("TimesUp", 1f, 0.3f, 0.5f);
        Enqueue("OutOfLives", 1f, 0.3f, 0.5f);
        Enqueue("GameOver", 1f, 0.3f, 0.5f);
        Enqueue("PowerupHealth", 1f, 0.3f, 0.5f);
        Enqueue("PowerupShield", 1f, 0.3f, 0.5f);
        Enqueue("PowerupRapidFire", 1f, 0.3f, 0.5f);
        Enqueue("PowerupTripleScore", 1f, 0.3f, 0.5f);
        Enqueue("PowerupMagnet", 1f, 0.3f, 0.5f);
        Enqueue("HealthCritical", 1f, 0.3f, 0.5f);
        Enqueue("PlayerDown", 1f, 0.3f, 0.5f);
        Enqueue("LastLife", 1f, 0.3f, 0.5f);
        Enqueue("RespawnReady", 1f, 0.3f, 0.5f);
        Enqueue("AmmoLow", 1f, 0.3f, 0.5f);
        Enqueue("AmmoEmpty", 1f, 0.3f, 0.5f);
        Enqueue("Reloaded", 1f, 0.3f, 0.5f);
        Enqueue("SniperScoped", 1f, 0.3f, 0.5f);
        Enqueue("RocketReady", 1f, 0.3f, 0.5f);
        Enqueue("EnemyDestroyed", 1f, 0.3f, 0.5f);
        Enqueue("HunterDestroyed", 1f, 0.3f, 0.5f);
        Enqueue("TacticianDestroyed", 1f, 0.3f, 0.5f);
        Enqueue("Combo3", 1f, 0.3f, 0.5f);
        Enqueue("Combo5", 1f, 0.3f, 0.5f);
        Enqueue("ComboBroken", 1f, 0.3f, 0.5f);
        Enqueue("LastEnemy", 1f, 0.3f, 0.5f);
        Enqueue("A2HunterDetected", 1f, 0.3f, 0.5f);
        Enqueue("A3Retreating", 1f, 0.3f, 0.5f);
        Enqueue("ThreeEnemiesRemaining", 1f, 0.3f, 0.5f);
        Enqueue("FiveEnemiesRemaining", 1f, 0.3f, 0.5f);
        Enqueue("TwoMinuteWarning", 1f, 0.3f, 0.5f);
        Enqueue("OneMinuteWarning", 1f, 0.3f, 0.5f);
        Enqueue("TenSecondCountdown", 1f, 0.3f, 0.5f);
        Enqueue("NextWaveInFive", 1f, 0.3f, 0.5f);
        Enqueue("Score1000", 1f, 0.3f, 0.5f);
        Enqueue("Score5000", 1f, 0.3f, 0.5f);
        Enqueue("Score10000", 1f, 0.3f, 0.5f);
        Schedule();
    }

    public void Clear()
    {
        StopSources();
        m_Cues.Clear();
        m_PendingCues.Clear();
        m_PlayingSequence = false;
    }

    struct VoiceCue
    {
        public AudioClip Clip;
        public float Volume;
        public float Mix;
        public float Delay;
    }

    void EnqueueNumber(int value, float delay)
    {
        if (value < 0 || value > 20)
            return;

        Enqueue(NumberNames[value], 1.4f, 0.05f, delay);
    }

    void Enqueue(string name, float volume, float mix, float delay)
    {
        var clip = LoadClip(name);
        if (clip == null)
        {
            GameDebug.LogWarning($"Voice clip missing: {name}");
            return;
        }

        if (clip != null)
            m_Cues.Add(new VoiceCue { Clip = clip, Volume = volume, Mix = mix, Delay = delay });
    }

    AudioClip LoadClip(string name)
    {
        var path = Path.Combine(Application.streamingAssetsPath, Path.Combine("Voice", Path.Combine("Announcements", name + ".wav")));
        if (!File.Exists(path))
            return null;

        var bytes = File.ReadAllBytes(path);
        var channels = BitConverter.ToUInt16(bytes, 22);
        var sampleRate = (int)BitConverter.ToUInt32(bytes, 24);
        var bitsPerSample = BitConverter.ToUInt16(bytes, 34);
        var hasDataChunk = bytes.Length >= 46 &&
            bytes[38] == (byte)'d' && bytes[39] == (byte)'a' &&
            bytes[40] == (byte)'t' && bytes[41] == (byte)'a';
        var dataOffset = hasDataChunk ? 46 : 44;
        var dataSize = hasDataChunk ? BitConverter.ToInt32(bytes, 42) : bytes.Length - dataOffset;

        if (dataSize <= 0 || bitsPerSample != 16)
            return null;

        var frameCount = dataSize / (sizeof(short) * channels);
        var samples = new float[frameCount * channels];
        for (var index = 0; index < samples.Length; ++index)
            samples[index] = BitConverter.ToInt16(bytes, dataOffset + index * sizeof(short)) / 32768f;

        var clip = AudioClip.Create(name, frameCount, channels, sampleRate, false);
        clip.SetData(samples, 0);
        return clip;
    }

    void EnsureSourceCount(int count)
    {
        while (m_Sources.Count < count)
        {
            var sourceObject = new GameObject("VoiceSource" + m_Sources.Count);
            sourceObject.transform.SetParent(transform, false);
            var source = sourceObject.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.loop = false;
            source.spatialBlend = 0f;
            m_Sources.Add(source);
            m_Filters.Add(sourceObject.AddComponent<RobotVoiceFilter>());
        }

        for (var index = m_Sources.Count - 1; index >= count; --index)
        {
            Destroy(m_Sources[index].gameObject);
            m_Sources.RemoveAt(index);
            m_Filters.RemoveAt(index);
        }
    }

    void StopSources()
    {
        for (var index = 0; index < m_Sources.Count; ++index)
            m_Sources[index].Stop();
    }

    void Schedule()
    {
        StopSources();
        EnsureSourceCount(m_Cues.Count);
        m_OutputLogged.Clear();
        for (var index = 0; index < m_Cues.Count; ++index)
            m_OutputLogged.Add(false);

        var scheduledTime = AudioSettings.dspTime + 0.03f;
        for (var index = 0; index < m_Cues.Count; ++index)
        {
            var cue = m_Cues[index];
            var source = m_Sources[index];
            var filter = m_Filters[index];

            scheduledTime += cue.Delay;
            source.volume = cue.Volume;
            source.pitch = cue.Mix < 0.1f ? 1f : 0.85f;
            filter.Mix = cue.Mix;
            source.clip = cue.Clip;
            source.PlayScheduled(scheduledTime);
            GameDebug.Log($"Voice scheduled {cue.Clip.name} at {scheduledTime:0.000}");

            scheduledTime += cue.Clip.length / source.pitch;
        }

        m_PlayingSequence = true;
        m_BusyUntilDspTime = scheduledTime;
    }

    void Update()
    {
        for (var index = 0; index < m_Cues.Count && index < m_Sources.Count; ++index)
        {
            if (m_OutputLogged[index] || !m_Sources[index].isPlaying)
                continue;

            m_Sources[index].GetOutputData(m_OutputBuffer, 0);
            var peak = 0f;
            for (var sampleIndex = 0; sampleIndex < m_OutputBuffer.Length; ++sampleIndex)
                peak = Mathf.Max(peak, Mathf.Abs(m_OutputBuffer[sampleIndex]));

            if (peak > 0.01f)
            {
                GameDebug.Log($"Voice output {m_Cues[index].Clip.name} peak={peak:0.000}");
                m_OutputLogged[index] = true;
        }
        }

        if (!m_PlayingSequence || AudioSettings.dspTime < m_BusyUntilDspTime)
            return;

        var anyPlaying = false;
        for (var index = 0; index < m_Sources.Count; index++)
        {
            if (m_Sources[index].isPlaying)
            {
                anyPlaying = true;
                break;
            }
        }

        if (!anyPlaying)
        {
            m_Cues.Clear();
            m_PlayingSequence = false;
        }
    }

    void LateUpdate()
    {
        if (m_PlayingSequence || m_PendingCues.Count == 0)
            return;

        PlayCue(m_PendingCues.Dequeue());
    }

    void PlayCue(string cue)
    {
        m_Cues.Clear();
        Enqueue(cue, 1f, 0.3f, 0f);
        if (m_Cues.Count > 0)
            Schedule();
    }
}
