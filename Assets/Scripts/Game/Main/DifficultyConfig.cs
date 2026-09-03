using UnityEngine;

public class DifficultyConfig
{
    [Header("Wave Mode")]
    public float robotMoveSpeed = 2.5f;
    public float shootInterval = 1.2f;
    [Range(0f, 1f)] public float hitChance = 0.4f;
    public int baseEnemiesPerWave = 3;
    public float robotHealthMultiplier = 1.0f;

    [Header("Explore Mode")]
    public int totalRobots = 8;
    public float detectionRadius = 30f;
    public float playerHealthRegen = 5f;

    [Header("Shared")]
    public int playerMaxHealth = 100;
    public float powerupSpawnInterval = 40f;
    public int maxLives = 4;

    public static DifficultyConfig GetConfig(string difficulty)
    {
        // Create configs at runtime for simplicity
        var config = new DifficultyConfig();
        switch (difficulty.ToLower())
        {
            case "easy":
                config.robotMoveSpeed = 1.5f;
                config.shootInterval = 2.0f;
                config.hitChance = 0.2f;
                config.baseEnemiesPerWave = 2;
                config.robotHealthMultiplier = 0.5f;
                config.totalRobots = 5;
                config.detectionRadius = 30f;
                config.playerHealthRegen = 10f;
                config.playerMaxHealth = 200;
                config.powerupSpawnInterval = 30f;
                config.maxLives = 5;
                break;
            case "hard":
                config.robotMoveSpeed = 3.5f;
                config.shootInterval = 0.8f;
                config.hitChance = 0.6f;
                config.baseEnemiesPerWave = 4;
                config.robotHealthMultiplier = 1.5f;
                config.totalRobots = 12;
                config.detectionRadius = 40f;
                config.playerHealthRegen = 2f;
                config.playerMaxHealth = 100;
                config.powerupSpawnInterval = 50f;
                config.maxLives = 3;
                break;
            default: // normal
                break;
        }
        return config;
    }
}
