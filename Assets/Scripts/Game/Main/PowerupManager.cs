using System.Collections.Generic;
using UnityEngine;

public enum PowerupType
{
    Health,
    Shield,
    RapidFire,
    TripleScore,
    Magnet
}

public class ActivePowerup
{
    public PowerupType type;
    public float remainingTime;
}

public class PowerupManager
{
    List<ActivePowerup> m_ActivePowerups = new List<ActivePowerup>();
    float m_SpawnTimer;
    float m_SpawnInterval;
    Vector3 m_SpawnCenter;
    DifficultyConfig m_Config;

    public List<ActivePowerup> activePowerups => m_ActivePowerups;
    public bool HasPowerup(PowerupType type)
    {
        return m_ActivePowerups.Exists(p => p.type == type);
    }

    public PowerupManager(DifficultyConfig config, Vector3 spawnCenter)
    {
        m_Config = config;
        m_SpawnInterval = config.powerupSpawnInterval;
        m_SpawnCenter = spawnCenter;
        m_SpawnTimer = m_SpawnInterval;
    }

    public void Tick(float deltaTime)
    {
        // Tick active powerups
        for (int i = m_ActivePowerups.Count - 1; i >= 0; i--)
        {
            m_ActivePowerups[i].remainingTime -= deltaTime;
            if (m_ActivePowerups[i].remainingTime <= 0)
            {
                GameDebug.Log($"Powerup expired: {m_ActivePowerups[i].type}");
                m_ActivePowerups.RemoveAt(i);
            }
        }

        // Spawn powerups
        m_SpawnTimer -= deltaTime;
        if (m_SpawnTimer <= 0)
        {
            m_SpawnTimer = m_SpawnInterval;
            SpawnRandomPowerup();
        }
    }

    void SpawnRandomPowerup()
    {
        var types = System.Enum.GetValues(typeof(PowerupType));
        var type = (PowerupType)types.GetValue(Random.Range(0, types.Length));
        GameDebug.Log($"Powerup spawned: {type}");
    }

    public void Activate(PowerupType type)
    {
        float duration = type == PowerupType.Health ? 0f : 15f;
        var existing = m_ActivePowerups.Find(p => p.type == type);
        if (existing != null)
            existing.remainingTime = duration;
        else
            m_ActivePowerups.Add(new ActivePowerup { type = type, remainingTime = duration });

        GameDebug.Log($"Powerup activated: {type}");
    }
}
