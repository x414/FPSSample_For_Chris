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
    int maxActiveRobots => Mathf.Max(1, m_Config.maxActiveRobots);

    struct RobotSpawnRequest
    {
        public RobotType type;
        public float angle;
        public float entryDist;
        public float spawnDist;
    }

    List<AIController> m_AliveRobots = new List<AIController>();
    List<RobotSpawnRequest> m_PendingRobots = new List<RobotSpawnRequest>();
    List<AIController> m_DeadRobots = new List<AIController>();
    DifficultyConfig m_Config;
    Vector3 m_SpawnCenter;
    Vector3 m_SpawnForward;
    float m_WaveBreakDuration = 5f;
    float m_SpawnCooldown;
    float m_AnnouncementTimer;
    System.Action<AIController> m_OnRobotKilled;
    System.Action<AIController, Vector3> m_CreateRobotEntity;

    public WaveManager(DifficultyConfig config, Vector3 spawnCenter, Vector3 spawnForward, System.Action<AIController> onRobotKilled,
        System.Action<AIController, Vector3> createRobotEntity)
    {
        m_Config = config;
        m_SpawnCenter = spawnCenter;
        m_SpawnForward = spawnForward;
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

            if (m_SpawnCooldown > 0f)
                m_SpawnCooldown -= deltaTime;

            SpawnQueuedRobots();

            if (m_AliveRobots.Count == 0 && m_PendingRobots.Count == 0)
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
       int baseCount = m_Config.baseEnemiesPerWave + currentWave - 1;
       int baseA2Count = currentWave >= 3 ? Mathf.FloorToInt(currentWave / 2) : 0;
       int a2Count = Mathf.Min(baseCount - 1, Mathf.CeilToInt(baseA2Count * m_Config.enemyCountMultiplier));
       int a1Count = Mathf.CeilToInt((baseCount - baseA2Count) * m_Config.enemyCountMultiplier);
        waveTotalEnemies = a1Count + a2Count;
        waveKilledEnemies = 0;
       m_PendingRobots.Clear();
       m_SpawnCooldown = 0f;

        for (int i = 0; i < a1Count; i++)
            m_PendingRobots.Add(new RobotSpawnRequest
            {
                type = RobotType.A1_Infantry,
                angle = Random.Range(0f, Mathf.PI * 2f) + i * 2.4f,
                entryDist = Random.Range(4f, 8f),
                spawnDist = Random.Range(12f, 20f)
            });

        for (int i = 0; i < a2Count; i++)
            m_PendingRobots.Add(new RobotSpawnRequest
            {
                type = RobotType.A2_Hunter,
                angle = Random.Range(0f, Mathf.PI * 2f) + (a1Count + i) * 2.4f,
                entryDist = Random.Range(4f, 8f),
                spawnDist = Random.Range(12f, 20f)
            });

        SpawnQueuedRobots();

        GameDebug.Log($"Wave {currentWave} started! A1:{a1Count} A2:{a2Count}");
    }

    void SpawnRobot(RobotSpawnRequest request)
    {
        var angle = request.angle;
        var entryDist = request.entryDist;
        var spawnDist = request.spawnDist;
        var direction = m_SpawnForward.sqrMagnitude > 0.01f
            ? Quaternion.AngleAxis(Random.Range(-20f, 20f), Vector3.up) * m_SpawnForward
            : new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
        direction.y = 0f;
        direction.Normalize();
        var entryTarget = m_SpawnCenter + direction * entryDist;
        var pos = m_SpawnCenter + direction * spawnDist;
        pos.y = m_SpawnCenter.y + 0.1f;
        var robot = new AIController(request.type, m_Config, pos);
        m_AliveRobots.Add(robot);
        m_CreateRobotEntity?.Invoke(robot, pos);
        robot.BeginEntry(entryTarget);
    }

    void SpawnQueuedRobots()
    {
        if (m_SpawnCooldown > 0f) return;

        while (m_PendingRobots.Count > 0 && m_AliveRobots.Count < maxActiveRobots)
        {
            var request = m_PendingRobots[0];
            m_PendingRobots.RemoveAt(0);
            SpawnRobot(request);
        }

        m_SpawnCooldown = m_Config.robotSpawnCooldown;
    }
}
