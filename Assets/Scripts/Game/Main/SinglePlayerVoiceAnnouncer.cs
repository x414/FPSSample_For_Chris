using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

public class SinglePlayerVoiceAnnouncer : MonoBehaviour
{
    static readonly string[] NumberNames =
    {
        "Zero", "One", "Two", "Three", "Four", "Five", "Six", "Seven", "Eight",
        "Nine", "Ten", "Eleven", "Twelve", "Thirteen", "Fourteen", "Fifteen",
        "Sixteen", "Seventeen", "Eighteen", "Nineteen", "Twenty"
    };

    readonly List<VoiceCue> m_Cues = new List<VoiceCue>();
    readonly List<AudioSource> m_Sources = new List<AudioSource>();
    readonly List<RobotVoiceFilter> m_Filters = new List<RobotVoiceFilter>();
    readonly List<bool> m_OutputLogged = new List<bool>();
    readonly float[] m_OutputBuffer = new float[1024];

    public void AnnounceWave(int wave, int robotCount)
    {
        m_Cues.Clear();
        Enqueue("Wave", 1f, 0.3f, 0f);
        EnqueueNumber(wave, 0.05f);
        EnqueueNumber(robotCount, 0.15f);
        Enqueue("Robots", 1f, 0.3f, 0f);
        Schedule();
    }

    public void AnnounceCue(string cue)
    {
        m_Cues.Clear();
        Enqueue(cue, 1f, 0.3f, 0f);
        Schedule();
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
        Schedule();
    }

    public void Clear()
    {
        StopSources();
        m_Cues.Clear();
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
    }
}
