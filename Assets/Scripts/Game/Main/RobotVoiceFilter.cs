using UnityEngine;

public class RobotVoiceFilter : MonoBehaviour
{
    public float RingFrequency = 90f;
    public float QuantizationSteps = 128f;
    public float Mix = 0.3f;
    public float LowPassCoefficient = 0.85f;

    float m_RingPhase;
    float[] m_LowPassStates;

    void OnAudioFilterRead(float[] data, int channels)
    {
        if (m_LowPassStates == null || m_LowPassStates.Length != channels)
            m_LowPassStates = new float[channels];

        var ringIncrement = RingFrequency / AudioSettings.outputSampleRate;
        for (int sampleIndex = 0; sampleIndex < data.Length; sampleIndex += channels)
        {
            m_RingPhase += ringIncrement;
            if (m_RingPhase >= 1f)
                m_RingPhase -= 1f;

            for (int channel = 0; channel < channels; ++channel)
            {
                var input = data[sampleIndex + channel];
                var carrier = Mathf.Sin(2f * Mathf.PI * m_RingPhase);
                var robotic = input * carrier;
                robotic = Mathf.Clamp(robotic * 1.25f, -1f, 1f);
                robotic = Mathf.Round(robotic * QuantizationSteps) / QuantizationSteps;
                robotic = m_LowPassStates[channel] + LowPassCoefficient * (robotic - m_LowPassStates[channel]);
                m_LowPassStates[channel] = robotic;
                data[sampleIndex + channel] = Mathf.Lerp(input, robotic, Mix);
            }
        }
    }
}
