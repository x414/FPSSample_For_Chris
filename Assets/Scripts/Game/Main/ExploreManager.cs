using System.Collections.Generic;
using UnityEngine;

public class ExploreManager
{
    public int remainingRobots
    {
        get { int c = 0; foreach (var r in m_Robots) if (r.isAlive) c++; return c; }
   }

    public int totalRobots { get { return m_TotalRobots; } }

   List<AIController> m_Robots = new List<AIController>();
   List<AIController> m_DeadRobots = new List<AIController>();
    int m_TotalRobots;
    DifficultyConfig m_Config;
    Vector3 m_SpawnCenter;
    bool m_AllDead => remainingRobots == 0;
    System.Action<AIController> m_OnRobotKilled;
    System.Action<AIController, Vector3> m_CreateRobotEntity;

    public ExploreManager(DifficultyConfig config, Vector3 spawnCenter, System.Action<AIController> onRobotKilled,
        System.Action<AIController, Vector3> createRobotEntity)
    {
        m_Config = config;
        m_SpawnCenter = spawnCenter;
        m_OnRobotKilled = onRobotKilled;
        m_CreateRobotEntity = createRobotEntity;
        SpawnRobots();
    }

    void SpawnRobots()
    {
        int a1Count = Mathf.FloorToInt(m_Config.totalRobots * 0.6f);
       int a2Count = m_Config.totalRobots - a1Count;
        m_TotalRobots = a1Count + a2Count;

       for (int i = 0; i < a1Count; i++)
            SpawnRobot(RobotType.A1_Infantry);
        for (int i = 0; i < a2Count; i++)
            SpawnRobot(RobotType.A2_Hunter);

        GameDebug.Log($"Explore mode: {a1Count} A1 + {a2Count} A2 spawned");
    }

    void SpawnRobot(RobotType type)
    {
        var angle = Random.Range(0f, Mathf.PI * 2f);
        var dist = Random.Range(5f, 12f);
        var pos = m_SpawnCenter + new Vector3(Mathf.Cos(angle) * dist, 0.1f, Mathf.Sin(angle) * dist);
        var robot = new AIController(type, m_Config, pos);
        m_Robots.Add(robot);
        m_CreateRobotEntity?.Invoke(robot, pos);
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
