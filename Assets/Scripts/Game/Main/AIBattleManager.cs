using System.Collections.Generic;
using UnityEngine;

public class AIBattleManager
{
    List<AIController> m_Robots = new List<AIController>();
    DifficultyConfig m_Config;
    Vector3 m_SpawnCenter;
    Vector3 m_SpawnForward;
    System.Action<AIController> m_OnRobotKilled;
    System.Action<AIController, Vector3> m_CreateRobotEntity;
    bool m_AllDead;
    float m_ElapsedTime;
    const float EntitySpawnGracePeriod = 3f;

    public int robotHealth => m_Robots.Count > 0 && m_Robots[0].isAlive ? m_Robots[0].health : 0;
    public int robotMaxHealth => m_Robots.Count > 0 ? m_Robots[0].maxHealth : 0;
    public bool isRobotAlive => m_Robots.Count > 0 && m_Robots[0].isAlive;
    public string robotName => m_Robots.Count > 0 ? m_Robots[0].robotType.ToString() : "A3";

    public AIBattleManager(DifficultyConfig config, Vector3 spawnCenter, Vector3 spawnForward,
        System.Action<AIController> onRobotKilled, System.Action<AIController, Vector3> createRobotEntity)
    {
        m_Config = config;
        m_SpawnCenter = spawnCenter;
        m_SpawnForward = spawnForward;
        m_OnRobotKilled = onRobotKilled;
        m_CreateRobotEntity = createRobotEntity;

        SpawnRobot();
    }

    void SpawnRobot()
    {
        var direction = m_SpawnForward.sqrMagnitude > 0.01f
            ? Quaternion.AngleAxis(UnityEngine.Random.Range(-20f, 20f), Vector3.up) * m_SpawnForward
            : new Vector3(Mathf.Cos(UnityEngine.Random.Range(0f, Mathf.PI * 2f)), 0f, Mathf.Sin(UnityEngine.Random.Range(0f, Mathf.PI * 2f)));
        direction.y = 0f;
        direction.Normalize();

        var spawnDist = UnityEngine.Random.Range(12f, 20f);
        var entryDist = UnityEngine.Random.Range(4f, 8f);
        var entryTarget = m_SpawnCenter + direction * entryDist;
        var position = m_SpawnCenter + direction * spawnDist;
        position.y = m_SpawnCenter.y + 0.1f;

        var robot = new AIController(RobotType.A3_Tactician, m_Config, position);
        m_Robots.Add(robot);
        m_CreateRobotEntity?.Invoke(robot, position);
        robot.BeginEntry(entryTarget);

        GameDebug.Log($"AIBattle: Spawned A3_Tactician at {position}");
    }

    public void Tick(float deltaTime, Vector3 playerPos, System.Action<float> onShootPlayer, GameWorld world)
    {
        m_ElapsedTime += deltaTime;
        var inGracePeriod = m_ElapsedTime < EntitySpawnGracePeriod;

        for (var i = m_Robots.Count - 1; i >= 0; i--)
        {
            var robot = m_Robots[i];
            robot.UpdateEntity(world);
            robot.Tick(deltaTime, playerPos, onShootPlayer);
            robot.ApplyCommand(world, world.worldTime.tick);

            if (inGracePeriod)
                continue;

            if (!robot.isAlive)
            {
                m_OnRobotKilled?.Invoke(robot);
                m_Robots.RemoveAt(i);
                m_AllDead = true;
                GameDebug.Log("AIBattle: Robot destroyed! Player wins!");
            }
        }
    }

    public bool IsVictory()
    {
        return m_AllDead;
    }

    public string GetProgressText()
    {
        if (m_Robots.Count > 0 && m_Robots[0].isAlive)
            return $"{m_Robots[0].robotType}: {m_Robots[0].health}/{m_Robots[0].maxHealth} HP";
        return "Enemy destroyed!";
    }
}
