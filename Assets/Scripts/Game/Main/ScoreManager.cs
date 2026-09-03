using UnityEngine;

public class ScoreManager
{
    public int totalScore { get; private set; }
    public int killCount { get; private set; }
    public int maxCombo { get; private set; }
    public int currentCombo { get; private set; }
    public float comboTimer { get; private set; }

    const float comboWindow = 5f;

    public void Reset()
    {
        totalScore = 0;
        killCount = 0;
        maxCombo = 0;
        currentCombo = 0;
        comboTimer = 0f;
    }

    public void AddKill(int baseScore, bool isA2 = false)
    {
        killCount++;
        currentCombo++;
        if (currentCombo > maxCombo) maxCombo = currentCombo;
        comboTimer = comboWindow;

        int score = baseScore + (isA2 ? 5 : 0);
        score += (currentCombo - 1) * 5;
        totalScore += score;

        GameDebug.Log($"Kill! +{score} (combo x{currentCombo}) Total: {totalScore}");
    }

    public void AddWaveBonus()
    {
        totalScore += 50;
        GameDebug.Log($"Wave cleared! +50 Total: {totalScore}");
    }

    public void ApplyPenalty(float factor)
    {
        totalScore = Mathf.FloorToInt(totalScore * factor);
    }

    public void Tick(float deltaTime)
    {
        if (comboTimer > 0)
        {
            comboTimer -= deltaTime;
            if (comboTimer <= 0)
                currentCombo = 0;
        }
    }
}
