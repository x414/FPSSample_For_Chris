using System.Collections.Generic;
using UnityEngine;

public class ExploreManager
{
    struct RobotSpawnRequest
    {
        public RobotType type;
        public Vector3 patrolAnchor;
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
    List<Vector3> m_PatrolAnchors;
    bool m_AllDead => remainingRobots == 0;
    float m_SpawnCooldown;
    System.Action<AIController> m_OnRobotKilled;
    System.Action<AIController, Vector3> m_CreateRobotEntity;

    public ExploreManager(DifficultyConfig config, Vector3 spawnCenter, Vector3 spawnForward, System.Action<AIController> onRobotKilled,
        System.Action<AIController, Vector3> createRobotEntity, List<Vector3> patrolAnchors = null)
    {
        m_Config = config;
        m_SpawnCenter = spawnCenter;
        m_SpawnForward = spawnForward;
        m_PatrolAnchors = patrolAnchors;
        m_OnRobotKilled = onRobotKilled;
        m_CreateRobotEntity = createRobotEntity;
        m_SpawnCooldown = 0f;
        SpawnRobots();
    }

    void SpawnRobots()
    {
        int a1Count = Mathf.FloorToInt(m_Config.exploreTotalRobots * 0.6f);
       int a2Count = m_Config.exploreTotalRobots - a1Count;
        m_TotalRobots = a1Count + a2Count;
       m_PendingRobots.Clear();

        var anchors = GetPatrolAnchors();
        var anchorIndex = 0;

       for (int i = 0; i < a1Count; i++)
            m_PendingRobots.Add(new RobotSpawnRequest
            {
                type = RobotType.A1_Infantry,
                patrolAnchor = anchors[anchorIndex++ % anchors.Count]
            });
        for (int i = 0; i < a2Count; i++)
            m_PendingRobots.Add(new RobotSpawnRequest
            {
                type = RobotType.A2_Hunter,
                patrolAnchor = anchors[anchorIndex++ % anchors.Count]
            });

        SpawnQueuedRobots();

        GameDebug.Log($"Explore mode: {a1Count} A1 + {a2Count} A2 spawned");
    }

    void SpawnRobot(RobotSpawnRequest request)
    {
        var anchor = request.patrolAnchor;
        var offset = Random.insideUnitCircle.normalized * Random.Range(2f, 4f);
        var entryTarget = anchor + new Vector3(offset.x, 0f, offset.y);
        var jitter = Random.insideUnitCircle * 0.5f;
        var pos = anchor + new Vector3(offset.x + jitter.x, 0f, offset.y + jitter.y);
        pos.y = anchor.y + 0.1f;
        var robot = new AIController(request.type, m_Config, pos);
        m_Robots.Add(robot);
        m_CreateRobotEntity?.Invoke(robot, pos);
        robot.BeginEntry(entryTarget);
    }

    List<Vector3> GetPatrolAnchors()
    {
        if (m_PatrolAnchors != null && m_PatrolAnchors.Count > 0)
        {
            var anchors = new List<Vector3>(m_PatrolAnchors);
            for (var i = anchors.Count - 1; i > 0; i--)
            {
                var swapIndex = Random.Range(0, i + 1);
                var temporary = anchors[i];
                anchors[i] = anchors[swapIndex];
                anchors[swapIndex] = temporary;
            }
            return SelectSpreadAnchors(anchors);
        }

        var fallbackAnchors = new List<Vector3>();
        for (var i = 0; i < 12; i++)
        {
            var angle = Mathf.PI * 2f * i / 12f;
            var radius = i % 2 == 0 ? 35f : 70f;
            fallbackAnchors.Add(m_SpawnCenter + new Vector3(
                Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * radius));
        }
        return fallbackAnchors;
    }

    List<Vector3> SelectSpreadAnchors(List<Vector3> source)
    {
        var selected = new List<Vector3>();
        var minimumDistances = new[] { 35f, 20f, 0f };

        foreach (var minimumDistance in minimumDistances)
        {
            foreach (var candidate in source)
            {
                var isSpaced = true;
                foreach (var existing in selected)
                {
                    if (Vector3.SqrMagnitude(candidate - existing) < minimumDistance * minimumDistance)
                    {
                        isSpaced = false;
                        break;
                    }
                }

                if (isSpaced)
                    selected.Add(candidate);
            }
        }

        return selected;
    }

    void SpawnQueuedRobots()
    {
        if (m_SpawnCooldown > 0f) return;

        while (m_PendingRobots.Count > 0 && m_Robots.Count < Mathf.Max(1, m_Config.exploreMaxActiveRobots))
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
