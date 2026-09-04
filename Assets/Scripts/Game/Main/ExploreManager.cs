using System.Collections.Generic;
using UnityEngine;

public class ExploreManager
{
    struct RobotSpawnRequest
    {
        public RobotType type;
        public float angle;
        public float entryDist;
        public float spawnDist;
    }

    public int remainingRobots
    {
        get
        {
            int count = 0;
            foreach (var robot in m_Robots)
                if (robot.isAlive)
                    count++;
            return count + m_PendingRobots.Count;
        }
   }

    public int totalRobots { get { return m_TotalRobots; } }

   List<AIController> m_Robots = new List<AIController>();
   List<RobotSpawnRequest> m_PendingRobots = new List<RobotSpawnRequest>();
   List<AIController> m_DeadRobots = new List<AIController>();
    int m_TotalRobots;
    DifficultyConfig m_Config;
    Vector3 m_SpawnCenter;
    Vector3 m_SpawnForward;
    bool m_AllDead => remainingRobots == 0;
    float m_SpawnCooldown;
    System.Action<AIController> m_OnRobotKilled;
    System.Action<AIController, Vector3> m_CreateRobotEntity;

    public ExploreManager(DifficultyConfig config, Vector3 spawnCenter, Vector3 spawnForward, System.Action<AIController> onRobotKilled,
        System.Action<AIController, Vector3> createRobotEntity)
    {
        m_Config = config;
        m_SpawnCenter = spawnCenter;
        m_SpawnForward = spawnForward;
        m_OnRobotKilled = onRobotKilled;
        m_CreateRobotEntity = createRobotEntity;
        m_SpawnCooldown = 0f;
        SpawnRobots();
    }

    void SpawnRobots()
    {
        int a1Count = Mathf.FloorToInt(m_Config.totalRobots * 0.6f);
       int a2Count = m_Config.totalRobots - a1Count;
        m_TotalRobots = a1Count + a2Count;
       m_PendingRobots.Clear();

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

        GameDebug.Log($"Explore mode: {a1Count} A1 + {a2Count} A2 spawned");
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
        m_Robots.Add(robot);
        m_CreateRobotEntity?.Invoke(robot, pos);
        robot.BeginEntry(entryTarget);
    }

    void SpawnQueuedRobots()
    {
        if (m_SpawnCooldown > 0f) return;

        while (m_PendingRobots.Count > 0 && m_Robots.Count < Mathf.Max(1, m_Config.maxActiveRobots))
        {
            var request = m_PendingRobots[0];
            m_PendingRobots.RemoveAt(0);
            SpawnRobot(request);
        }

        m_SpawnCooldown = m_Config.robotSpawnCooldown;
    }

    public void Tick(float deltaTime, Vector3 playerPos, System.Action<float> onShootPlayer, GameWorld world)
    {
        for (var i = m_Robots.Count - 1; i >= 0; i--)
        {
            var robot = m_Robots[i];
            robot.UpdateEntity(world);
            robot.Tick(deltaTime, playerPos, onShootPlayer);
            robot.ApplyCommand(world, world.worldTime.tick);
            if (!robot.isAlive)
            {
                m_OnRobotKilled?.Invoke(robot);
                m_DeadRobots.Add(robot);
                m_Robots.RemoveAt(i);
            }
        }

        if (m_SpawnCooldown > 0f)
            m_SpawnCooldown -= deltaTime;

        SpawnQueuedRobots();
    }

   public bool IsVictory()
   {
       return m_AllDead;
   }

    public string GetProgressText()
    {
        return "Robots: " + (m_TotalRobots - remainingRobots) + "/" + m_TotalRobots + " destroyed";
    }
}
