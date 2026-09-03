using System.Collections.Generic;
using UnityEngine;

public class WaveManager
{
    public int currentWave { get; private set; }
    public bool isWaveActive { get; private set; }
    public int waveTotalEnemies { get; private set; }
    public int waveKilledEnemies { get; private set; }
    public float waveBreakTimer { get; private set; }
    public int enemiesAlive => m_AliveRobots.Count;

    List<AIController> m_AliveRobots = new List<AIController>();
    List<AIController> m_DeadRobots = new List<AIController>();
    DifficultyConfig m_Config;
    Vector3 m_SpawnCenter;
    float m_WaveBreakDuration = 5f;
    float m_AnnouncementTimer;
    System.Action<AIController> m_OnRobotKilled;
    System.Action<AIController, Vector3> m_CreateRobotEntity;

    public WaveManager(DifficultyConfig config, Vector3 spawnCenter, System.Action<AIController> onRobotKilled,
        System.Action<AIController, Vector3> createRobotEntity)
    {
        m_Config = config;
        m_SpawnCenter = spawnCenter;
        m_OnRobotKilled = onRobotKilled;
        m_CreateRobotEntity = createRobotEntity;
        currentWave = 0;
        isWaveActive = false;
        waveBreakTimer = 5f;
    }

    public void Tick(float deltaTime, Vector3 playerPos, System.Action<float> onShootPlayer, GameWorld world)
    {
        if (isWaveActive)
        {
            for (int i = m_AliveRobots.Count - 1; i >= 0; i--)
            {
                var robot = m_AliveRobots[i];
                robot.UpdateEntity(world);
                robot.Tick(deltaTime, playerPos, onShootPlayer);
                robot.ApplyCommand(world, world.worldTime.tick);
               if (!robot.isAlive)
               {
                   m_OnRobotKilled?.Invoke(robot);
                    waveKilledEnemies++;
                   m_DeadRobots.Add(robot);
                    m_AliveRobots.RemoveAt(i);
                }
            }

            if (m_AliveRobots.Count == 0)
            {
               isWaveActive = false;
               waveBreakTimer = m_WaveBreakDuration;
                m_AnnouncementTimer = 0f;
                GameDebug.Log($"Wave {currentWave} cleared!");
            }
        }
       else
       {
           waveBreakTimer -= deltaTime;
           if (waveBreakTimer <= 0)
               StartNextWave();
       }
        m_AnnouncementTimer = Mathf.Max(0f, m_AnnouncementTimer - deltaTime);
    }

   public string GetProgressText()
    {
        if (isWaveActive)
            return "Wave " + currentWave + ": " + waveKilledEnemies + "/" + waveTotalEnemies + " robots destroyed";

        return "Next wave in " + Mathf.CeilToInt(Mathf.Max(0f, waveBreakTimer)) + "s";
    }

    public string GetAnnouncementText()
    {
        return m_AnnouncementTimer > 0f
            ? "WAVE " + currentWave + System.Environment.NewLine + waveTotalEnemies + " Robots"
            : string.Empty;
    }

   void StartNextWave()
    {
       currentWave++;
       isWaveActive = true;
       m_AnnouncementTimer = 3f;
       int count = m_Config.baseEnemiesPerWave + currentWave - 1;
       int a2Count = currentWave >= 3 ? Mathf.FloorToInt(currentWave / 2) : 0;
       int a1Count = count - a2Count;
        waveTotalEnemies = a1Count + a2Count;
        waveKilledEnemies = 0;

        for (int i = 0; i < a1Count; i++)
            SpawnRobot(RobotType.A1_Infantry);

        for (int i = 0; i < a2Count; i++)
            SpawnRobot(RobotType.A2_Hunter);

        GameDebug.Log($"Wave {currentWave} started! A1:{a1Count} A2:{a2Count}");
    }

    void SpawnRobot(RobotType type)
    {
        var angle = Random.Range(0f, Mathf.PI * 2f);
        var dist = Random.Range(6f, 12f);
        var pos = m_SpawnCenter + new Vector3(Mathf.Cos(angle) * dist, 0.1f, Mathf.Sin(angle) * dist);
        var robot = new AIController(type, m_Config, pos);
        m_AliveRobots.Add(robot);
        m_CreateRobotEntity?.Invoke(robot, pos);
    }
}
