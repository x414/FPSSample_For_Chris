using UnityEngine;
using Unity.Entities;
using Unity.Mathematics;

public enum RobotType
{
    A1_Infantry,
    A2_Hunter
}

public enum AIState
{
    Idle,
    Patrol,
    Chase,
    Attack,
    Dead
}

public class AIController
{
    public RobotType robotType { get; private set; }
    public AIState state { get; private set; }
    public int health { get; private set; }
    public int maxHealth { get; private set; }
    public Entity entity { get; set; }
    public bool isAlive => state != AIState.Dead && health > 0;

    Vector3 m_Position;
    Vector3 m_TargetPosition;
    Quaternion m_Rotation;
    float m_ShootTimer;
    float m_PatrolTimer;
    float m_MoveSpeed;
    float m_ShootInterval;
    float m_HitChance;
    float m_DetectionRadius;
    Vector3 m_PatrolOrigin;
    Vector3 m_PatrolTarget;
    bool m_HasPatrolTarget;
    PlayerState m_PlayerState;
    GameObject m_CharacterObject;
    bool m_EntityHealthInitialized;
    bool m_FireThisTick;

    public float DesiredLookYaw { get; private set; }
    public float DesiredLookPitch { get; private set; }
    public float DesiredMoveMagnitude { get; private set; }
    public bool WantsFire { get; private set; }

    const float attackRange = 20f;
    const float chaseRange = 30f;
    const float loseTargetRange = 40f;
    const float patrolRadius = 8f;

    public AIController(RobotType type, DifficultyConfig config, Vector3 spawnPos)
    {
        robotType = type;
        state = AIState.Idle;
        m_Position = spawnPos;
        m_PatrolOrigin = spawnPos;
        m_MoveSpeed = config.robotMoveSpeed;
        m_ShootInterval = config.shootInterval;
        m_HitChance = config.hitChance;
        m_DetectionRadius = config.detectionRadius;

        maxHealth = type == RobotType.A1_Infantry
            ? Mathf.FloorToInt(50 * config.robotHealthMultiplier)
            : Mathf.FloorToInt(30 * config.robotHealthMultiplier);
        health = maxHealth;

        if (type == RobotType.A2_Hunter)
            m_MoveSpeed *= 1.5f;
    }

    public void Tick(float deltaTime, Vector3 playerPos, System.Action<float> onShootPlayer)
    {
        if (!isAlive) return;

        float distToPlayer = Vector3.Distance(m_Position, playerPos);
        m_FireThisTick = false;
        switch (state)
        {
            case AIState.Idle:
                state = AIState.Patrol;
                break;

            case AIState.Patrol:
                if (distToPlayer < m_DetectionRadius)
                    state = AIState.Chase;
                else
                    PatrolUpdate(deltaTime);
                break;

            case AIState.Chase:
                MoveTowards(playerPos, deltaTime);
                if (distToPlayer < attackRange)
                {
                    state = AIState.Attack;
                    m_ShootTimer = 0;
                }
                else if (distToPlayer > loseTargetRange)
                    state = AIState.Patrol;
                break;

            case AIState.Attack:
                if (distToPlayer > attackRange * 1.2f)
                {
                    state = AIState.Chase;
                }
                else
                {
                    m_ShootTimer += deltaTime;
                    if (m_ShootTimer >= m_ShootInterval)
                    {
                        m_ShootTimer = 0;
                        m_FireThisTick = true;
                        if (UnityEngine.Random.value <= m_HitChance)
                            onShootPlayer?.Invoke(robotType == RobotType.A2_Hunter ? 14 : 8);
                    }
                }
                break;
        }

        UpdateDesiredMovement(playerPos, distToPlayer);
    }

    public void BindCharacter(PlayerState playerState)
    {
        m_PlayerState = playerState;
    }

    public void ResetSpawnPosition(Vector3 position)
    {
        m_Position = position;
        m_PatrolOrigin = position;
        m_PatrolTarget = position;
        m_TargetPosition = position;
        m_Rotation = Quaternion.identity;
        m_HasPatrolTarget = false;
    }

    public void UpdateEntity(GameWorld world)
    {
        if (m_PlayerState == null || !isAlive) return;

        var entityManager = world.GetEntityManager();
        var entity = m_PlayerState.controlledEntity;
        if (entity == Entity.Null || !entityManager.Exists(entity))
        {
            health = 0;
            state = AIState.Dead;
            GameDebug.Log($"Removed missing {robotType} entity");
            return;
        }

       if (m_CharacterObject == null && entityManager.HasComponent<Character>(entity))
       {
           m_CharacterObject = entityManager.GetComponentObject<Character>(entity).gameObject;
           ApplyVariantColor();
       }

        if (!m_EntityHealthInitialized && entityManager.HasComponent<Character>(entity))
        {
            var character = entityManager.GetComponentObject<Character>(entity);
            if (character != null && character.heroTypeData != null)
            {
                var healthState = entityManager.GetComponentData<HealthStateData>(entity);
                healthState.SetMaxHealth(maxHealth);
                entityManager.SetComponentData(entity, healthState);
                m_EntityHealthInitialized = true;
                GameDebug.Log(robotType + " health set to " + maxHealth);
            }
        }

        if (entityManager.HasComponent<CharacterInterpolatedData>(entity))
            m_Position = entityManager.GetComponentData<CharacterInterpolatedData>(entity).position;

        if (entityManager.HasComponent<CharacterPredictedData>(entity))
            m_Position = entityManager.GetComponentData<CharacterPredictedData>(entity).position;

        if (m_Position.y < m_PatrolOrigin.y - 15f)
        {
            var recoveredPosition = m_PatrolOrigin + Vector3.up * 0.2f;
            if (entityManager.HasComponent<CharacterPredictedData>(entity))
            {
                var predictedState = entityManager.GetComponentData<CharacterPredictedData>(entity);
                predictedState.velocity = Vector3.zero;
                entityManager.SetComponentData(entity, predictedState);
            }

            if (entityManager.HasComponent<Character>(entity))
            {
                var character = entityManager.GetComponentObject<Character>(entity);
                if (!character.m_TeleportPending)
                    character.TeleportTo(recoveredPosition, Quaternion.identity);
            }

            m_Position = recoveredPosition;
            m_TargetPosition = recoveredPosition;
            state = AIState.Patrol;
            GameDebug.Log($"Recovered fallen {robotType} at {recoveredPosition}");
        }

       if (entityManager.HasComponent<HealthStateData>(entity))
       {
           var healthState = entityManager.GetComponentData<HealthStateData>(entity);
           if (healthState.health <= 0)
           {
               health = 0;
               state = AIState.Dead;
               GameDebug.Log($"Robot {robotType} destroyed!");
           }
       }

        if (entityManager.HasComponent<RagdollStateData>(entity))
        {
            var ragdollState = entityManager.GetComponentData<RagdollStateData>(entity);
            if (ragdollState.ragdollActive != 0)
            {
                health = 0;
                state = AIState.Dead;
                GameDebug.Log($"Robot {robotType} ragdoll detected!");
            }
        }
    }

    public void ApplyCommand(GameWorld world, int tick)
    {
        if (m_PlayerState == null || !isAlive) return;

        var entityManager = world.GetEntityManager();
        var entity = m_PlayerState.controlledEntity;
        if (entity == Entity.Null || !entityManager.Exists(entity) || !entityManager.HasComponent<UserCommandComponentData>(entity)) return;

        EnsureServerEntity(entityManager, entity);
        var commandComponent = entityManager.GetComponentData<UserCommandComponentData>(entity);
        commandComponent.command.checkTick = tick;
        commandComponent.command.renderTick = tick;
        commandComponent.command.lookYaw = DesiredLookYaw;
        commandComponent.command.lookPitch = DesiredLookPitch;
        commandComponent.command.moveYaw = 0f;
        commandComponent.command.moveMagnitude = DesiredMoveMagnitude;
        commandComponent.command.buttons.Set(UserCommand.Button.PrimaryFire, m_FireThisTick);
        commandComponent.command.emote = CharacterEmote.None;
        entityManager.SetComponentData(entity, commandComponent);
    }

    void UpdateDesiredMovement(Vector3 playerPos, float distToPlayer)
    {
        Vector3 target = state == AIState.Attack ? playerPos : m_PatrolTarget;
        Vector3 direction = target - m_Position;
        direction.y = 0f;

        if (state == AIState.Attack || state == AIState.Chase)
            target = playerPos;

        direction = target - m_Position;
        direction.y = 0f;

        if (direction.sqrMagnitude > 0.0001f)
            DesiredLookYaw = Mathf.Atan2(direction.x, direction.z) * Mathf.Rad2Deg;

        Vector3 aimDirection = playerPos + Vector3.up * 1.2f - (m_Position + Vector3.up * 1.5f);
        DesiredLookPitch = Mathf.Clamp(90f + Mathf.Atan2(aimDirection.y, new Vector2(aimDirection.x, aimDirection.z).magnitude) * Mathf.Rad2Deg, 0f, 180f);

        DesiredMoveMagnitude = state == AIState.Attack || state == AIState.Idle ? 0f : 1f;
        WantsFire = m_FireThisTick;
    }

    void ApplyVariantColor()
    {
        if (m_CharacterObject == null) return;

        var color = robotType == RobotType.A1_Infantry
            ? new Color(0.85f, 0.18f, 0.18f)
            : new Color(0.58f, 0.18f, 0.85f);

        foreach (var renderer in m_CharacterObject.GetComponentsInChildren<Renderer>())
        {
            foreach (var material in renderer.materials)
            {
                if (material.HasProperty("_BaseColor"))
                    material.SetColor("_BaseColor", color);

                if (material.HasProperty("_Color"))
                    material.SetColor("_Color", color);
            }
        }

        var character = m_CharacterObject.GetComponent<Character>();
        if (character != null)
        {
            foreach (var presentation in character.presentations)
            {
                if (presentation == null)
                    continue;

                foreach (var renderer in presentation.GetComponentsInChildren<Renderer>())
                {
                    foreach (var material in renderer.materials)
                    {
                        if (material.HasProperty("_BaseColor"))
                            material.SetColor("_BaseColor", color);

                        if (material.HasProperty("_Color"))
                            material.SetColor("_Color", color);
                    }
                }
            }
        }
    }

    static void EnsureServerEntity(EntityManager entityManager, Entity entity)
    {
        if (!entityManager.HasComponent<ServerEntity>(entity))
            entityManager.AddComponentData(entity, new ServerEntity());

        if (!entityManager.HasComponent<EntityGroupChildren>(entity)) return;

        var buffer = entityManager.GetBuffer<EntityGroupChildren>(entity);
        for (var i = 0; i < buffer.Length; i++)
        {
            var child = buffer[i].entity;
            if (entityManager.Exists(child) && !entityManager.HasComponent<ServerEntity>(child))
                entityManager.AddComponentData(child, new ServerEntity());
        }
    }

    void PatrolUpdate(float deltaTime)
    {
        if (!m_HasPatrolTarget || Vector3.Distance(m_Position, m_PatrolTarget) < 1f)
        {
            var random = UnityEngine.Random.insideUnitCircle * patrolRadius;
            m_PatrolTarget = m_PatrolOrigin + new Vector3(random.x, 0, random.y);
            m_HasPatrolTarget = true;
        }
        MoveTowards(m_PatrolTarget, deltaTime);
    }

    void MoveTowards(Vector3 target, float deltaTime)
    {
        var dir = (target - m_Position).normalized;
        m_Position += dir * m_MoveSpeed * deltaTime;
        m_Rotation = Quaternion.LookRotation(dir);
    }

    public void TakeDamage(int damage)
    {
        health -= damage;
        if (health <= 0)
        {
            health = 0;
            state = AIState.Dead;
            GameDebug.Log($"Robot {robotType} destroyed!");
        }
    }

    public Vector3 GetPosition() => m_Position;
    public Quaternion GetRotation() => m_Rotation;
}
