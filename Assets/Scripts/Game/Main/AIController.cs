using UnityEngine;
using Unity.Entities;
using Unity.Mathematics;

public enum RobotType
{
    A1_Infantry,
    A2_Hunter,
    A3_Tactician
}

public enum AIState
{
    Idle,
    Enter,
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
    float m_EntryTimer;
    float m_PreviousHealth;
    Vector3 m_EntryTarget;
    float m_MoveSpeed;
    float m_ShootInterval;
    float m_HitChance;
    float m_DetectionRadius;
    Vector3 m_PatrolOrigin;
    Vector3 m_PatrolTarget;
    bool m_HasPatrolTarget;
    Vector3 m_DetourTarget;
    bool m_HasDetourTarget;
    float m_DetourTimer;
    Vector3 m_LastPosition;
    float m_StuckTimer;
    PlayerState m_PlayerState;
    GameObject m_CharacterObject;
    bool m_EntityHealthInitialized;
    bool m_FireThisTick;
    bool m_HasPlannedAim;
    Vector3 m_PlannedAimDirection;
    Vector3 m_PreviousPlayerPosition;
    bool m_HasPreviousPlayerPosition;
    float m_PlayerStationaryTime;
    int m_TacticianMissedShotStreak;

    // A3 Tactician behavior fields
    float m_StrafeTimer;
    float m_StrafeDirection = 1f;
    int m_BurstShotsRemaining;
    float m_BurstPauseTimer;
    float m_ReactionDelayTimer;
    bool m_HasReacted;
    bool m_IsFlanking;
    float m_FlankTimer;
    Vector3 m_FlankTarget;
    bool m_IsRetreating;
    float m_RetreatTimer;
    float m_LostSightTime;
    float m_RecoveryCooldown;
    Vector3 m_RecoveryPosition;
    bool m_HasRecoveryPosition;

    public float DesiredLookYaw { get; private set; }
    public float DesiredLookPitch { get; private set; }
    public float DesiredMoveMagnitude { get; private set; }
    public float DesiredMoveYaw { get; private set; }
    public bool WantsFire { get; private set; }

    public void Despawn(GameWorld world)
    {
        var playerState = m_PlayerState;
        var characterObject = m_CharacterObject;
        var entityManager = world.GetEntityManager();

        if (characterObject == null && playerState != null &&
            playerState.controlledEntity != Entity.Null &&
            entityManager.Exists(playerState.controlledEntity) &&
            entityManager.HasComponent<Character>(playerState.controlledEntity))
        {
            characterObject = entityManager
                .GetComponentObject<Character>(playerState.controlledEntity)
                .gameObject;
        }

        m_PlayerState = null;
        m_CharacterObject = null;

        if (characterObject != null && characterObject != playerState?.gameObject)
        {
            GameDebug.Log($"Despawn robot character:{characterObject.name}");
            world.RequestDespawn(characterObject);
        }

        if (playerState != null && playerState.gameObject != null)
        {
            GameDebug.Log($"Despawn robot player:{playerState.gameObject.name}");
            world.RequestDespawn(playerState.gameObject);
        }
    }

    public void BeginFadeOut(float duration)
    {
        if (m_CharacterObject == null)
            return;

        var fadeOut = m_CharacterObject.GetComponent<RobotFadeOut>();
        if (fadeOut == null)
            fadeOut = m_CharacterObject.AddComponent<RobotFadeOut>();

        var character = m_CharacterObject.GetComponent<Character>();
        fadeOut.Begin(duration, character != null ? character.presentations : null);
    }

    const float attackRange = 20f;
    const float chaseRange = 30f;
    const float loseTargetRange = 40f;
    const float patrolRadius = 20f;
    const float patrolMinWaypointDistance = 10f;
    const float patrolSpeedFactor = 0.2f;

    public AIController(RobotType type, DifficultyConfig config, Vector3 spawnPos)
    {
        robotType = type;
        state = AIState.Enter;
        m_Position = spawnPos;
        m_PatrolOrigin = spawnPos;
        m_MoveSpeed = config.robotMoveSpeed;
        m_ShootInterval = config.shootInterval;
        m_HitChance = config.hitChance;
        m_DetectionRadius = config.detectionRadius;

        maxHealth = type == RobotType.A1_Infantry
            ? Mathf.FloorToInt(50 * config.robotHealthMultiplier)
            : type == RobotType.A3_Tactician
                ? Mathf.FloorToInt(150 * config.robotHealthMultiplier)
                : Mathf.FloorToInt(30 * config.robotHealthMultiplier);
        health = maxHealth;
        m_PreviousHealth = maxHealth;
        m_LastPosition = spawnPos;

        if (type == RobotType.A2_Hunter)
            m_MoveSpeed *= 1.5f;

        if (type == RobotType.A3_Tactician)
        {
            m_StrafeTimer = 0f;
            m_ReactionDelayTimer = UnityEngine.Random.Range(0.3f, 0.8f);
            m_HasReacted = false;
        }
    }

    public void Tick(float deltaTime, Vector3 playerPos, System.Action<float> onShootPlayer)
    {
        if (!isAlive) return;

        float distToPlayer = Vector3.Distance(m_Position, playerPos);
        m_FireThisTick = false;
        m_HasPlannedAim = false;
        UpdatePlayerMovementProfile(playerPos, deltaTime);
        var canSeePlayer = CanSeePlayer(playerPos, distToPlayer);
        UpdateStuckState(deltaTime, playerPos, distToPlayer, canSeePlayer);

        if (robotType == RobotType.A3_Tactician && !m_HasReacted && distToPlayer < m_DetectionRadius)
        {
            m_ReactionDelayTimer -= deltaTime;
            if (m_ReactionDelayTimer <= 0f)
                m_HasReacted = true;
        }

        if (robotType == RobotType.A3_Tactician && health < maxHealth * 0.3f && !m_IsRetreating && state != AIState.Patrol)
        {
            m_IsRetreating = true;
            m_RetreatTimer = UnityEngine.Random.Range(3f, 6f);
            GameDebug.Log($"A3 Tactician retreating! HP:{health}/{maxHealth}");
        }

        if (m_IsRetreating)
        {
            m_RetreatTimer -= deltaTime;
            if (m_RetreatTimer <= 0f || distToPlayer > loseTargetRange)
            {
                m_IsRetreating = false;
                if (distToPlayer > loseTargetRange)
                    state = AIState.Patrol;
            }
        }

        switch (state)
        {
            case AIState.Enter:
                m_EntryTimer += deltaTime;
                MoveTowards(m_EntryTarget, deltaTime);
                var shouldDetect = distToPlayer < m_DetectionRadius && CanSeePlayer(playerPos, distToPlayer);
                if (robotType == RobotType.A3_Tactician && !m_HasReacted)
                    shouldDetect = false;

                if (shouldDetect)
                {
                    state = AIState.Chase;
                    m_ShootTimer = 0f;
                    ClearDetour();
                }
                else if (Vector3.Distance(m_Position, m_EntryTarget) < 1.2f || m_EntryTimer >= 12f)
                {
                    state = AIState.Patrol;
                    m_PatrolOrigin = m_EntryTarget;
                    m_HasPatrolTarget = false;
                }
                break;

            case AIState.Idle:
                state = AIState.Patrol;
                break;

            case AIState.Patrol:
                if (robotType == RobotType.A3_Tactician && !m_HasReacted)
                    PatrolUpdate(deltaTime);
                else
                {
                    if (distToPlayer < m_DetectionRadius && canSeePlayer)
                        state = AIState.Chase;
                    else
                        PatrolUpdate(deltaTime);
                }
                break;

            case AIState.Chase:
                if (robotType == RobotType.A3_Tactician && !m_IsRetreating)
                {
                    UpdateFlank(deltaTime, playerPos, distToPlayer);
                    if (m_IsFlanking)
                        MoveTowards(m_FlankTarget, deltaTime);
                    else
                        MoveTowards(playerPos, deltaTime);
                }
                else if (m_IsRetreating)
                {
                    var retreatDirection = (m_Position - playerPos).normalized;
                    var retreatTarget = m_Position + retreatDirection * 10f;
                    MoveTowards(retreatTarget, deltaTime);
                }
                else
                {
                    MoveTowards(playerPos, deltaTime);
                }

                var effectiveAttackRange = attackRange;
                if (robotType == RobotType.A3_Tactician)
                    effectiveAttackRange = 25f;

                if (distToPlayer < effectiveAttackRange && !m_IsRetreating && canSeePlayer)
                {
                    state = AIState.Attack;
                    m_ShootTimer = 0;
                    m_BurstShotsRemaining = 0;
                    m_BurstPauseTimer = 0f;
                    if (robotType == RobotType.A3_Tactician)
                        StartStrafe();
                }
                else if (distToPlayer > loseTargetRange && !m_IsRetreating)
                    state = AIState.Patrol;
                break;

            case AIState.Attack:
                var loseAttackRange = attackRange * 1.2f;
                if (robotType == RobotType.A3_Tactician)
                    loseAttackRange = 25f * 1.2f;

                if (distToPlayer > loseAttackRange || !canSeePlayer ||
                    (robotType == RobotType.A3_Tactician && m_IsRetreating))
                {
                    state = AIState.Chase;
                    if (robotType == RobotType.A3_Tactician)
                        m_IsFlanking = false;
                }
                else
                {
                    if (robotType == RobotType.A3_Tactician)
                    {
                        UpdateStrafe(deltaTime);
                        UpdateTacticianBurstFire(deltaTime, onShootPlayer, playerPos, distToPlayer, canSeePlayer);
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
                }
                break;
        }

        UpdateDesiredMovement(playerPos, distToPlayer, deltaTime);
    }

    public void BindCharacter(PlayerState playerState)
    {
        m_PlayerState = playerState;
    }

    void StartStrafe()
    {
        m_StrafeDirection = UnityEngine.Random.value > 0.5f ? 1f : -1f;
        m_StrafeTimer = UnityEngine.Random.Range(0.8f, 2.0f);
    }

    void UpdateStrafe(float deltaTime)
    {
        m_StrafeTimer -= deltaTime;
        if (m_StrafeTimer <= 0f)
            StartStrafe();
    }

    void UpdateTacticianBurstFire(float deltaTime, System.Action<float> onShootPlayer,
        Vector3 playerPos, float distanceToPlayer, bool canSeePlayer)
    {
        if (m_BurstPauseTimer > 0f)
        {
            m_BurstPauseTimer -= deltaTime;
            return;
        }

        if (m_BurstShotsRemaining <= 0)
        {
            m_BurstShotsRemaining = UnityEngine.Random.Range(3, 6);
            m_ShootTimer = 0f;
        }

        m_ShootTimer += deltaTime;
        var burstInterval = Mathf.Max(0.12f, m_ShootInterval * 0.3f);
        if (m_ShootTimer >= burstInterval)
        {
            m_ShootTimer = 0f;
            m_BurstShotsRemaining--;
            m_FireThisTick = true;

            var hitChance = CalculateTacticianHitChance(distanceToPlayer, canSeePlayer);
            var didHit = UnityEngine.Random.value <= hitChance;
            var eyePosition = m_Position + Vector3.up * 1.5f;
            var targetPosition = playerPos + Vector3.up * 1.2f;
            m_PlannedAimDirection = (targetPosition - eyePosition).normalized;

            if (didHit)
                m_TacticianMissedShotStreak = 0;
            else if (canSeePlayer)
                m_TacticianMissedShotStreak++;

            if (!canSeePlayer)
                m_PlannedAimDirection = Quaternion.AngleAxis(
                    Mathf.Atan2(0.9f, Mathf.Max(1f, distanceToPlayer)) * Mathf.Rad2Deg *
                    UnityEngine.Random.Range(1.05f, 1.35f) *
                    (UnityEngine.Random.value > 0.5f ? 1f : -1f),
                    Vector3.up) * m_PlannedAimDirection;
            else if (!didHit)
                m_PlannedAimDirection = Quaternion.AngleAxis(
                    Mathf.Atan2(1.0f, Mathf.Max(1f, distanceToPlayer)) * Mathf.Rad2Deg *
                    UnityEngine.Random.Range(1.1f, 1.45f) *
                    (UnityEngine.Random.value > 0.5f ? 1f : -1f),
                    Vector3.up) * m_PlannedAimDirection;

            m_HasPlannedAim = true;

            if (m_BurstShotsRemaining <= 0)
                m_BurstPauseTimer = m_ShootInterval * UnityEngine.Random.Range(1.2f, 2.0f);
        }
    }

    void UpdatePlayerMovementProfile(Vector3 playerPos, float deltaTime)
    {
        if (m_HasPreviousPlayerPosition)
        {
            var movement = Vector3.Distance(playerPos, m_PreviousPlayerPosition);
            var isStationary = movement <= Mathf.Max(0.04f, Game.config.playerSpeed * deltaTime * 0.12f);
            m_PlayerStationaryTime = isStationary ? m_PlayerStationaryTime + deltaTime : 0f;
        }

        m_PreviousPlayerPosition = playerPos;
        m_HasPreviousPlayerPosition = true;
    }

    float CalculateTacticianHitChance(float distanceToPlayer, bool canSeePlayer)
    {
        if (!canSeePlayer)
            return 0f;

        if (m_PlayerStationaryTime > 1.5f)
            return 1f;

        if (m_TacticianMissedShotStreak >= 8)
            return 1f;

        var distanceFactor = Mathf.Lerp(1.45f, 0.8f, Mathf.Clamp01(distanceToPlayer / 25f));
        var effectiveHitChance = m_HitChance * distanceFactor;
        effectiveHitChance += Mathf.Min(0.12f * Mathf.Max(0, m_TacticianMissedShotStreak - 2), 0.48f);

        if (m_PlayerStationaryTime > 0.6f)
            effectiveHitChance += 0.18f;
        if (distanceToPlayer < 8f)
            effectiveHitChance += 0.15f;
        else if (distanceToPlayer < 15f)
            effectiveHitChance += 0.07f;

        return Mathf.Clamp(effectiveHitChance, 0.05f, 0.9f);
    }

    void UpdateFlank(float deltaTime, Vector3 playerPos, float distToPlayer)
    {
        m_FlankTimer -= deltaTime;

        if (m_FlankTimer <= 0f)
        {
            m_IsFlanking = distToPlayer > 12f && UnityEngine.Random.value < 0.35f;
            if (m_IsFlanking)
            {
                var toPlayer = (playerPos - m_Position).normalized;
                var side = UnityEngine.Random.value > 0.5f ? 1f : -1f;
                var flankDirection = Quaternion.Euler(0f, side * UnityEngine.Random.Range(45f, 75f), 0f) * toPlayer;
                m_FlankTarget = playerPos + flankDirection * distToPlayer * 0.6f;
                m_FlankTimer = UnityEngine.Random.Range(2f, 4f);
            }
            else
            {
                m_FlankTimer = UnityEngine.Random.Range(1f, 2.5f);
            }
        }
    }

    public void ResetSpawnPosition(Vector3 position)
    {
        m_Position = position;
        m_PatrolOrigin = position;
        m_PatrolTarget = position;
        m_TargetPosition = position;
        m_Rotation = Quaternion.identity;
        m_HasPatrolTarget = false;
        m_LastPosition = position;
        m_StuckTimer = 0f;
        ClearDetour();
    }

    public void BeginEntry(Vector3 battlefieldPosition)
    {
        battlefieldPosition.y = m_Position.y;
        m_EntryTarget = battlefieldPosition;
        m_TargetPosition = battlefieldPosition;
        m_PatrolOrigin = battlefieldPosition;
        m_PatrolTarget = battlefieldPosition;
        m_HasPatrolTarget = true;
        m_EntryTimer = 0f;
        state = AIState.Enter;
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
                healthState.maxHealth = maxHealth;
                healthState.health = maxHealth;
                entityManager.SetComponentData(entity, healthState);
                if (entityManager.HasComponent<HitCollisionOwnerData>(entity))
                {
                    var collisionOwner = entityManager.GetComponentData<HitCollisionOwnerData>(entity);
                    character.teamId = 1;
                    collisionOwner.colliderFlags = 1U << character.teamId;
                    collisionOwner.collisionEnabled = 1;
                    entityManager.SetComponentData(entity, collisionOwner);
                }
                m_EntityHealthInitialized = true;
                GameDebug.Log(robotType + " health set to " + maxHealth);
            }
        }

        if (entityManager.HasComponent<HealthStateData>(entity))
        {
            var healthState = entityManager.GetComponentData<HealthStateData>(entity);
            var previousHealth = health;
            health = Mathf.Clamp(Mathf.CeilToInt(healthState.health), 0, maxHealth);
            if (health < previousHealth)
            {
                if (isAlive && state != AIState.Chase && state != AIState.Attack)
                {
                    state = AIState.Chase;
                    m_ShootTimer = 0f;
                    ClearDetour();
                }
                GameDebug.Log($"{robotType} damaged: {health}/{maxHealth}");
            }
            m_PreviousHealth = health;
        }

        if (entityManager.HasComponent<CharacterInterpolatedData>(entity))
            m_Position = entityManager.GetComponentData<CharacterInterpolatedData>(entity).position;

        if (entityManager.HasComponent<CharacterPredictedData>(entity))
        {
            var predictedState = entityManager.GetComponentData<CharacterPredictedData>(entity);
            m_Position = predictedState.position;

            if (entityManager.HasComponent<CharacterInterpolatedData>(entity))
            {
                var interpolatedState = entityManager.GetComponentData<CharacterInterpolatedData>(entity);
                if (Vector3.SqrMagnitude(interpolatedState.position - predictedState.position) > 4f)
                {
                    interpolatedState.position = predictedState.position;
                    entityManager.SetComponentData(entity, interpolatedState);
                    GameDebug.Log($"AI presentation snap {robotType}: {predictedState.position}");
                }
            }
        }

        if (m_CharacterObject != null)
        {
            var character = m_CharacterObject.GetComponent<Character>();
            if (character != null)
            {
                character.teamId = 1;
                foreach (var presentation in character.presentations)
                {
                    if (presentation != null &&
                        Vector3.SqrMagnitude(presentation.transform.position - m_Position) > 4f)
                    {
                        presentation.transform.position = m_Position;
                        GameDebug.Log($"AI root snap {robotType}: {m_Position}");
                    }
                }
            }
        }

        if (m_HasRecoveryPosition && entityManager.HasComponent<Character>(entity))
        {
            var character = entityManager.GetComponentObject<Character>(entity);
            if (!character.m_TeleportPending)
            {
                m_HasRecoveryPosition = false;
                m_Position = m_RecoveryPosition;
                m_TargetPosition = m_RecoveryPosition;
                m_LostSightTime = 0f;
                ClearDetour();
                character.TeleportTo(m_RecoveryPosition, Quaternion.LookRotation(Vector3.forward));
            }
        }

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
        commandComponent.command.moveYaw = DesiredMoveYaw;
        commandComponent.command.moveMagnitude = DesiredMoveMagnitude;
        commandComponent.command.buttons.Set(UserCommand.Button.PrimaryFire, m_FireThisTick);
        commandComponent.command.emote = CharacterEmote.None;
        entityManager.SetComponentData(entity, commandComponent);
    }

    void UpdateDesiredMovement(Vector3 playerPos, float distToPlayer, float deltaTime)
    {
        Vector3 target = state == AIState.Attack ? playerPos :
            state == AIState.Enter ? m_EntryTarget : m_PatrolTarget;
        Vector3 direction = target - m_Position;
        direction.y = 0f;

        if (state == AIState.Attack || state == AIState.Chase)
            target = playerPos;

        target = ResolveMoveTarget(target);
        direction = target - m_Position;
        direction.y = 0f;

        if (direction.sqrMagnitude > 0.0001f)
        {
            var targetYaw = Mathf.Atan2(direction.x, direction.z) * Mathf.Rad2Deg;
            DesiredLookYaw = Mathf.MoveTowardsAngle(DesiredLookYaw, targetYaw, 270f * deltaTime);
        }

        var aimDirection = m_HasPlannedAim
            ? m_PlannedAimDirection
            : playerPos + Vector3.up * 1.2f - (m_Position + Vector3.up * 1.5f);
        DesiredLookPitch = Mathf.Clamp(90f - Mathf.Atan2(aimDirection.y, new Vector2(aimDirection.x, aimDirection.z).magnitude) * Mathf.Rad2Deg, 0f, 180f);

        var configuredSpeed = state == AIState.Patrol ? m_MoveSpeed * patrolSpeedFactor : m_MoveSpeed;
        if (m_DetourTimer > 0f && !m_HasDetourTarget && state == AIState.Patrol)
            m_HasPatrolTarget = false;

        if (state == AIState.Idle ||
            (m_DetourTimer > 0f && !m_HasDetourTarget))
        {
            DesiredMoveMagnitude = 0f;
            DesiredMoveYaw = 0f;
        }
        else if (state == AIState.Attack && robotType == RobotType.A3_Tactician)
        {
            DesiredMoveMagnitude = Mathf.Clamp01(m_MoveSpeed * 0.6f / Mathf.Max(1f, Game.config.playerSpeed));
            DesiredMoveYaw = m_StrafeDirection * 90f;
        }
        else if (state == AIState.Attack)
        {
            DesiredMoveMagnitude = 0f;
            DesiredMoveYaw = 0f;
        }
        else
        {
            DesiredMoveMagnitude = Mathf.Clamp01(configuredSpeed / Mathf.Max(1f, Game.config.playerSpeed));
            DesiredMoveYaw = 0f;
        }
        WantsFire = m_FireThisTick;
    }

    void UpdateStuckState(float deltaTime, Vector3 playerPos, float distToPlayer, bool canSeePlayer)
    {
        m_DetourTimer = Mathf.Max(0f, m_DetourTimer - deltaTime);
        m_RecoveryCooldown = Mathf.Max(0f, m_RecoveryCooldown - deltaTime);

        if (state == AIState.Chase && !m_IsRetreating && !canSeePlayer && distToPlayer < chaseRange)
        {
            m_LostSightTime += deltaTime;
            if (m_LostSightTime >= 3f && m_RecoveryCooldown <= 0f)
            {
                if (TryFindRecoveryPosition(playerPos, out m_RecoveryPosition))
                {
                    m_HasRecoveryPosition = true;
                    m_RecoveryCooldown = 4f;
                    GameDebug.Log($"AI recovery {robotType}: {m_Position} -> {m_RecoveryPosition}");
                }
            }
        }
        else
        {
            m_LostSightTime = 0f;
        }

        if (state == AIState.Attack || state == AIState.Idle || DesiredMoveMagnitude <= 0f)
        {
            m_LastPosition = m_Position;
            return;
        }

        var expectedDistance = m_MoveSpeed * deltaTime;
        if (state == AIState.Patrol)
            expectedDistance *= patrolSpeedFactor;
        if (Vector3.Distance(m_Position, m_LastPosition) < expectedDistance * 0.25f)
            m_StuckTimer += deltaTime;
        else
            m_StuckTimer = 0f;

        if (m_StuckTimer >= 0.6f)
        {
            if (state == AIState.Patrol)
            {
                if (m_StuckTimer >= 1.2f)
                {
                    SelectPatrolTarget();
                    m_StuckTimer = 0f;
                    return;
                }
            }
            else
            {
            m_DetourTarget = FindDetourTarget();
            m_HasDetourTarget = m_DetourTarget != Vector3.zero;
            m_DetourTimer = m_HasDetourTarget ? 1.6f : 0.3f;
            m_StuckTimer = 0f;
            }
        }

        m_LastPosition = m_Position;
    }

    Vector3 ResolveMoveTarget(Vector3 target)
    {
        if (state == AIState.Attack || state == AIState.Idle || state == AIState.Patrol)
            return target;

        if (m_DetourTimer > 0f && m_HasDetourTarget &&
            Vector3.Distance(m_Position, m_DetourTarget) > 0.6f)
            return m_DetourTarget;

        var direction = target - m_Position;
        direction.y = 0f;
        if (direction.sqrMagnitude < 0.25f)
            return target;

        var origin = m_Position + Vector3.up * 1.2f;
        RaycastHit obstacle;
        if (!TryFindObstacle(origin, direction.normalized, Mathf.Min(direction.magnitude, 4f), out obstacle))
        {
            ClearDetour();
            return target;
        }

        m_DetourTarget = FindDetourTarget(direction, obstacle);
        m_HasDetourTarget = m_DetourTarget != Vector3.zero;
        m_DetourTimer = m_HasDetourTarget ? 1.6f : 0.3f;
        if (m_HasDetourTarget)
            GameDebug.Log($"AI detour {robotType}: {m_Position} -> {m_DetourTarget}");
        return m_HasDetourTarget ? m_DetourTarget : target;
    }

    Vector3 FindDetourTarget()
    {
        var desiredDirection = (m_Position - m_LastPosition).normalized;
        if (desiredDirection.sqrMagnitude < 0.01f)
            desiredDirection = Vector3.forward;
        return FindDetourTarget(desiredDirection, default(RaycastHit));
    }

    Vector3 FindDetourTarget(Vector3 desiredDirection, RaycastHit obstacle)
    {
        desiredDirection.y = 0f;
        desiredDirection.Normalize();

        Vector3 wallNormal = obstacle.normal;
        wallNormal.y = 0f;
        if (wallNormal.sqrMagnitude < 0.01f)
            wallNormal = -desiredDirection;
        wallNormal.Normalize();

        var alongWall = Vector3.Cross(Vector3.up, wallNormal).normalized;
        if (Vector3.Dot(alongWall, desiredDirection) < 0f)
            alongWall = -alongWall;

        var basePoint = obstacle.collider != null ? obstacle.point : m_Position + desiredDirection * 2f;
        var candidate = basePoint + alongWall * 3f + wallNormal * 0.8f;
        candidate.y = m_Position.y;
        var toCandidate = candidate - m_Position;
        toCandidate.y = 0f;
        if (toCandidate.sqrMagnitude > 0.1f &&
            !TryFindObstacle(m_Position + Vector3.up * 1.2f, toCandidate.normalized,
                Mathf.Min(toCandidate.magnitude, 3f), out _))
            return candidate;

        candidate = basePoint - alongWall * 3f + wallNormal * 0.8f;
        candidate.y = m_Position.y;
        toCandidate = candidate - m_Position;
        toCandidate.y = 0f;
        if (toCandidate.sqrMagnitude > 0.1f &&
            !TryFindObstacle(m_Position + Vector3.up * 1.2f, toCandidate.normalized,
                Mathf.Min(toCandidate.magnitude, 3f), out _))
            return candidate;

        return Vector3.zero;
    }

    void ClearDetour()
    {
        m_DetourTarget = Vector3.zero;
        m_HasDetourTarget = false;
        m_DetourTimer = 0f;
    }

    bool TryFindRecoveryPosition(Vector3 playerPos, out Vector3 recoveryPosition)
    {
        var playerEye = playerPos + Vector3.up * 1.2f;
        var bestPosition = Vector3.zero;
        var bestDistance = float.MaxValue;

        for (var distanceIndex = 0; distanceIndex < 3; distanceIndex++)
        {
            var radius = 6f + distanceIndex * 3f;
            for (var step = 0; step < 16; step++)
            {
                var angle = step * 22.5f + UnityEngine.Random.Range(-6f, 6f);
                var direction = Quaternion.AngleAxis(angle, Vector3.up) * Vector3.forward;
                var candidate = playerPos + direction * radius;
                candidate.y = playerPos.y;

                if (!IsClearRecoveryPosition(candidate) ||
                    !HasClearLine(candidate + Vector3.up * 1.5f, playerEye))
                    continue;

                var candidateDistance = Vector3.Distance(candidate, m_Position);
                if (candidateDistance < bestDistance)
                {
                    bestDistance = candidateDistance;
                    bestPosition = candidate;
                }
            }
        }

        recoveryPosition = bestPosition;
        return bestDistance < float.MaxValue;
    }

    static bool IsClearRecoveryPosition(Vector3 position)
    {
        if (!Physics.Raycast(position + Vector3.up * 2f, Vector3.down, 5f,
            Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
            return false;

        return !Physics.CheckSphere(position + Vector3.up * 0.8f, 0.55f,
            Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
    }

    static bool HasClearLine(Vector3 from, Vector3 to)
    {
        var direction = to - from;
        var distance = direction.magnitude;
        return distance >= 0.5f && !TryFindObstacle(from, direction / distance,
            Mathf.Max(0.1f, distance - 0.2f), out _);
    }

    static bool TryFindObstacle(Vector3 origin, Vector3 direction, float distance, out RaycastHit obstacle)
    {
        obstacle = default(RaycastHit);
        var hits = Physics.RaycastAll(origin, direction, distance, Physics.DefaultRaycastLayers,
            QueryTriggerInteraction.Ignore);
        System.Array.Sort(hits, (left, right) => left.distance.CompareTo(right.distance));

        foreach (var hit in hits)
        {
            if (hit.collider == null || hit.collider.GetComponent<HitCollision>() != null ||
                hit.collider.GetComponentInParent<Character>() != null)
                continue;

            obstacle = hit;
            return true;
        }

        return false;
    }

    void ApplyVariantColor()
    {
        if (m_CharacterObject == null) return;

        var color = robotType == RobotType.A1_Infantry
            ? new Color(0.85f, 0.18f, 0.18f)
            : robotType == RobotType.A3_Tactician
                ? new Color(0.95f, 0.75f, 0.1f)
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
        m_PatrolTimer -= deltaTime;
        if (!m_HasPatrolTarget || m_PatrolTimer <= 0f ||
            Vector3.Distance(m_Position, m_PatrolTarget) < 1.2f)
        {
            SelectPatrolTarget();
        }
        MoveTowards(m_PatrolTarget, deltaTime, GetMoveSpeed());
    }

    bool CanSeePlayer(Vector3 playerPos, float distanceToPlayer)
    {
        if (distanceToPlayer <= 4f)
            return true;

        var eyePosition = m_Position + Vector3.up * 1.5f;
        var targetPosition = playerPos + Vector3.up * 1.2f;
        var direction = targetPosition - eyePosition;
        var distance = direction.magnitude;
        if (distance < 0.5f)
            return true;

        return !TryFindObstacle(eyePosition, direction / distance,
            Mathf.Min(distance - 0.2f, m_DetectionRadius), out _);
    }

    void MoveTowards(Vector3 target, float deltaTime)
    {
        MoveTowards(target, deltaTime, GetMoveSpeed());
    }

    float GetMoveSpeed()
    {
        return state == AIState.Patrol ? m_MoveSpeed * patrolSpeedFactor : m_MoveSpeed;
    }

    void MoveTowards(Vector3 target, float deltaTime, float speed)
    {
        var dir = (target - m_Position).normalized;
        m_Position += dir * speed * deltaTime;
        m_Rotation = Quaternion.LookRotation(dir);
    }

    void SelectPatrolTarget()
    {
        for (var attempt = 0; attempt < 8; attempt++)
        {
            var angle = UnityEngine.Random.Range(0f, Mathf.PI * 2f);
            var distance = UnityEngine.Random.Range(patrolMinWaypointDistance, patrolRadius);
            var candidate = m_PatrolOrigin + new Vector3(
                Mathf.Cos(angle) * distance, 0f, Mathf.Sin(angle) * distance);
            var direction = candidate - m_Position;
            direction.y = 0f;

            if (direction.sqrMagnitude < 0.25f)
                continue;

            if (attempt < 7 && TryFindObstacle(
                m_Position + Vector3.up * 1.2f, direction.normalized,
                Mathf.Min(direction.magnitude, 8f), out _))
                continue;

            m_PatrolTarget = candidate;
            m_HasPatrolTarget = true;
            m_PatrolTimer = direction.magnitude / Mathf.Max(0.1f, GetMoveSpeed()) + 5f;
            ClearDetour();
            return;
        }
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
