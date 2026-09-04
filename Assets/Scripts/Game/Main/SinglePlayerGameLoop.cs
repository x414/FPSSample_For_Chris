using System;
using System.Collections.Generic;
using UnityEngine;
using Unity.Entities;
using Unity.Mathematics;

public enum SinglePlayerState
{
    Loading,
    Active
}

public class SinglePlayerGameLoop : Game.IGameLoop
{
    public enum Mode { Wave, Explore }
    public enum Difficulty { Easy, Normal, Hard }

    // Module references (same as PreviewGameLoop)
    CharacterModulePreview m_CharacterModule;
    ProjectileModuleClient m_ProjectileModule;
    HitCollisionModule m_HitCollisionModule;
    PlayerModuleClient m_PlayerModuleClient;
    PlayerModuleServer m_PlayerModuleServer;
    SpectatorCamModuleServer m_SpectatorCamModuleServer;
    SpectatorCamModuleClient m_SpectatorCamModuleClient;
    EffectModuleClient m_EffectModule;
    ItemModule m_ItemModule;
    RagdollModule m_ragdollModule;
    BundledResourceManager m_resourceSystem;

    // Systems
    DespawnProjectiles m_DespawnProjectiles;
    DamageAreaSystemServer m_DamageAreaSystemServer;
    TeleporterSystemServer m_TeleporterSystemServer;
    TeleporterSystemClient m_TeleporterSystemClient;
    UpdateDestructableProps m_UpdateDestructableProps;
    DestructiblePropSystemClient m_DestructiblePropSystemClient;
    UpdatePresentationOwners m_UpdatePresentationOwners;
    HandlePresentationOwnerDesawn m_HandlePresentationOwnerDespawn;
    HandleGrenadeRequest m_HandleGrenadeRequests;
    StartGrenadeMovement m_StartGrenadeMovement;
    FinalizeGrenadeMovement m_FinalizeGrenadeMovement;
    ApplyGrenadePresentation m_ApplyGrenadePresentation;
    MoverUpdate m_moverUpdate;
    SpinSystem m_SpinSystem;
    HandleNamePlateSpawn m_HandleNamePlateOwnerSpawn;
    HandleNamePlateDespawn m_HandleNamePlateOwnerDespawn;
    UpdateNamePlates m_UpdateNamePlates;
    UpdateReplicatedOwnerFlag m_UpdateReplicatedOwnerFlag;
    TwistSystem m_TwistSystem;
    FanSystem m_FanSystem;
    TranslateScaleSystem m_TranslateScaleSystem;

    // Game systems
    StateMachine<SinglePlayerState> m_StateMachine;
    GameWorld m_GameWorld;
    PlayerState m_Player;
    GameTime gameTime = new GameTime(60);
    PreviewGameMode m_previewGameMode;

    // Single player managers
    Mode m_Mode;
    Difficulty m_Difficulty;
    DifficultyConfig m_DiffConfig;
    WaveManager m_WaveManager;
    ExploreManager m_ExploreManager;
    ScoreManager m_ScoreManager;
    TimerManager m_TimerManager;
    PowerupManager m_PowerupManager;
    SinglePlayerMenuUI m_MenuUI;
    SinglePlayerHudUI m_HudUI;
    Vector3 m_SpawnCenter;
    Vector3 m_RobotSpawnForward = Vector3.forward;
    SinglePlayerResultUI m_ResultUI;

   bool m_GameOver;
    bool m_GameplayStarted;
    bool m_AutoStart;
    bool m_PlayerDeathTracked;
    int m_LastBonusWave;
   int m_NextBotPlayerId = 100;
    int m_LivesRemaining;
   float m_PlayerHealth;
    float m_ShieldMultiplier = 1f;

    public bool Init(string[] args)
    {
        m_Mode = Mode.Wave;
        m_Difficulty = Difficulty.Normal;
        const string levelName = "level_01";

        foreach (var argument in args)
        {
            if (string.Equals(argument, "explore", StringComparison.OrdinalIgnoreCase))
                m_Mode = Mode.Explore;
            else if (string.Equals(argument, "easy", StringComparison.OrdinalIgnoreCase))
                m_Difficulty = Difficulty.Easy;
            else if (string.Equals(argument, "hard", StringComparison.OrdinalIgnoreCase))
                m_Difficulty = Difficulty.Hard;
            else if (string.Equals(argument, "autostart", StringComparison.OrdinalIgnoreCase))
                m_AutoStart = true;
        }

        m_DiffConfig = DifficultyConfig.GetConfig(m_Difficulty.ToString());
        m_ScoreManager = new ScoreManager();
        m_TimerManager = new TimerManager(15f);
        m_GameOver = false;
        m_GameplayStarted = false;

        // Register console commands
        Console.AddCommand("nextchar", CmdNextHero, "Select next character", GetHashCode());
        Console.AddCommand("spectator", CmdSpectatorCam, "Select spectator cam", GetHashCode());
        Console.AddCommand("respawn", CmdRespawn, "Force a respawn", GetHashCode());
        Console.AddCommand("score", CmdShowScore, "Show current score", GetHashCode());

        Console.SetOpen(false);

        m_StateMachine = new StateMachine<SinglePlayerState>();
        m_StateMachine.Add(SinglePlayerState.Loading, null, UpdateLoadingState, null);
        m_StateMachine.Add(SinglePlayerState.Active, EnterActiveState, UpdateStateActive, LeaveActiveState);

        m_GameWorld = new GameWorld("World[SinglePlayerGameLoop]");

        Game.game.levelManager.LoadLevel(levelName);
        m_StateMachine.SwitchTo(SinglePlayerState.Loading);

        GameDebug.Log($"SinglePlayer initialized. Mode:{m_Mode} Difficulty:{m_Difficulty} Level:{levelName}");
        return true;
    }

    public void Shutdown()
    {
        Console.RemoveCommandsWithTag(GetHashCode());
        m_StateMachine.Shutdown();
        m_PlayerModuleServer.Shutdown();
        Game.game.levelManager.UnloadLevel();
        m_GameWorld.Shutdown();
    }

    void UpdateLoadingState()
    {
        if (Game.game.levelManager.IsCurrentLevelLoaded())
            m_StateMachine.SwitchTo(SinglePlayerState.Active);
    }

    public void Update()
    {
        m_StateMachine.Update();
    }

    void EnterActiveState()
    {
        m_GameWorld.RegisterSceneEntities();
        m_resourceSystem = new BundledResourceManager(m_GameWorld, "BundledResources/Client");

        var dataComponentSerializers = new DataComponentSerializers();

        m_CharacterModule = new CharacterModulePreview(m_GameWorld, m_resourceSystem);
        m_ProjectileModule = new ProjectileModuleClient(m_GameWorld, m_resourceSystem);
        m_HitCollisionModule = new HitCollisionModule(m_GameWorld, 1, 2);
        m_PlayerModuleClient = new PlayerModuleClient(m_GameWorld);
        m_PlayerModuleServer = new PlayerModuleServer(m_GameWorld, m_resourceSystem);
        m_SpectatorCamModuleServer = new SpectatorCamModuleServer(m_GameWorld, m_resourceSystem);
        m_SpectatorCamModuleClient = new SpectatorCamModuleClient(m_GameWorld);
        m_EffectModule = new EffectModuleClient(m_GameWorld, m_resourceSystem);
        m_ItemModule = new ItemModule(m_GameWorld);
        m_ragdollModule = new RagdollModule(m_GameWorld);

        m_DespawnProjectiles = m_GameWorld.GetECSWorld().CreateManager<DespawnProjectiles>(m_GameWorld);
        m_DamageAreaSystemServer = m_GameWorld.GetECSWorld().CreateManager<DamageAreaSystemServer>(m_GameWorld);
        m_TeleporterSystemServer = m_GameWorld.GetECSWorld().CreateManager<TeleporterSystemServer>(m_GameWorld);
        m_TeleporterSystemClient = m_GameWorld.GetECSWorld().CreateManager<TeleporterSystemClient>(m_GameWorld);
        m_UpdateDestructableProps = m_GameWorld.GetECSWorld().CreateManager<UpdateDestructableProps>(m_GameWorld);
        m_DestructiblePropSystemClient = m_GameWorld.GetECSWorld().CreateManager<DestructiblePropSystemClient>(m_GameWorld);
        m_UpdatePresentationOwners = m_GameWorld.GetECSWorld().CreateManager<UpdatePresentationOwners>(m_GameWorld, m_resourceSystem);
        m_HandlePresentationOwnerDespawn = m_GameWorld.GetECSWorld().CreateManager<HandlePresentationOwnerDesawn>(m_GameWorld);
        m_HandleGrenadeRequests = m_GameWorld.GetECSWorld().CreateManager<HandleGrenadeRequest>(m_GameWorld, m_resourceSystem);
        m_StartGrenadeMovement = m_GameWorld.GetECSWorld().CreateManager<StartGrenadeMovement>(m_GameWorld);
        m_FinalizeGrenadeMovement = m_GameWorld.GetECSWorld().CreateManager<FinalizeGrenadeMovement>(m_GameWorld);
        m_ApplyGrenadePresentation = m_GameWorld.GetECSWorld().CreateManager<ApplyGrenadePresentation>(m_GameWorld);
        m_moverUpdate = m_GameWorld.GetECSWorld().CreateManager<MoverUpdate>(m_GameWorld);
        m_SpinSystem = m_GameWorld.GetECSWorld().CreateManager<SpinSystem>(m_GameWorld);
        m_HandleNamePlateOwnerSpawn = m_GameWorld.GetECSWorld().CreateManager<HandleNamePlateSpawn>(m_GameWorld);
        m_HandleNamePlateOwnerDespawn = m_GameWorld.GetECSWorld().CreateManager<HandleNamePlateDespawn>(m_GameWorld);
        m_UpdateNamePlates = m_GameWorld.GetECSWorld().CreateManager<UpdateNamePlates>(m_GameWorld);
        m_UpdateReplicatedOwnerFlag = m_GameWorld.GetECSWorld().CreateManager<UpdateReplicatedOwnerFlag>(m_GameWorld);
        m_UpdateReplicatedOwnerFlag.SetLocalPlayerId(-1);

        m_TwistSystem = new TwistSystem(m_GameWorld);
        m_FanSystem = new FanSystem(m_GameWorld);
        m_TranslateScaleSystem = new TranslateScaleSystem(m_GameWorld);

        m_PlayerModuleClient.RegisterLocalPlayer(0, null);

        // Spawn player
        m_Player = m_PlayerModuleServer.CreatePlayer(m_GameWorld, 0, "Hero", true);
        var playerEntity = m_Player.gameObject.GetComponent<GameObjectEntity>().Entity;
        var charControl = m_GameWorld.GetEntityManager().GetComponentObject<PlayerCharacterControl>(playerEntity);
        charControl.characterType = math.max(Game.characterType.IntValue, 0);
        m_Player.teamIndex = 0;

       m_previewGameMode = m_GameWorld.GetECSWorld().CreateManager<PreviewGameMode>(m_GameWorld, m_Player);
        m_previewGameMode.respawnDelay = 1;

       var menuObject = new GameObject("SinglePlayerMenu");
       m_MenuUI = menuObject.AddComponent<SinglePlayerMenuUI>();
       m_MenuUI.Initialize(ConfirmSelection);

        var hudObject = new GameObject("SinglePlayerHud");
        m_HudUI = hudObject.AddComponent<SinglePlayerHudUI>();

       Game.SetMousePointerLock(false);
        GameDebug.Log("SinglePlayer ready. Select mode and difficulty.");
        if (m_AutoStart)
            ConfirmSelection(m_Mode, m_Difficulty);
    }

    void LeaveActiveState()
    {
       if (m_MenuUI != null)
           UnityEngine.Object.Destroy(m_MenuUI.gameObject);
       if (m_HudUI != null)
           UnityEngine.Object.Destroy(m_HudUI.gameObject);
        if (m_ResultUI != null)
        {
            UnityEngine.Object.Destroy(m_ResultUI.gameObject);
            m_ResultUI = null;
        }

       // Same cleanup as PreviewGameLoop
        m_CharacterModule.Shutdown();
        m_ProjectileModule.Shutdown();
        m_ragdollModule.Shutdown();
        m_HitCollisionModule.Shutdown();
        m_PlayerModuleClient.Shutdown();
        m_PlayerModuleServer.Shutdown();
        m_SpectatorCamModuleServer.Shutdown();
        m_SpectatorCamModuleClient.Shutdown();
        m_EffectModule.Shutdown();
        m_ItemModule.Shutdown();

        m_GameWorld.GetECSWorld().DestroyManager(m_DamageAreaSystemServer);
        m_GameWorld.GetECSWorld().DestroyManager(m_DespawnProjectiles);
        m_GameWorld.GetECSWorld().DestroyManager(m_TeleporterSystemServer);
        m_GameWorld.GetECSWorld().DestroyManager(m_TeleporterSystemClient);
        m_GameWorld.GetECSWorld().DestroyManager(m_UpdateDestructableProps);
        m_GameWorld.GetECSWorld().DestroyManager(m_DestructiblePropSystemClient);
        m_GameWorld.GetECSWorld().DestroyManager(m_UpdatePresentationOwners);
        m_GameWorld.GetECSWorld().DestroyManager(m_HandlePresentationOwnerDespawn);
        m_GameWorld.GetECSWorld().DestroyManager(m_HandleGrenadeRequests);
        m_GameWorld.GetECSWorld().DestroyManager(m_StartGrenadeMovement);
        m_GameWorld.GetECSWorld().DestroyManager(m_FinalizeGrenadeMovement);
        m_GameWorld.GetECSWorld().DestroyManager(m_ApplyGrenadePresentation);
        m_GameWorld.GetECSWorld().DestroyManager(m_moverUpdate);
        m_GameWorld.GetECSWorld().DestroyManager(m_previewGameMode);
        m_GameWorld.GetECSWorld().DestroyManager(m_SpinSystem);
        m_GameWorld.GetECSWorld().DestroyManager(m_HandleNamePlateOwnerSpawn);
        m_GameWorld.GetECSWorld().DestroyManager(m_HandleNamePlateOwnerDespawn);
        m_GameWorld.GetECSWorld().DestroyManager(m_UpdateNamePlates);
        m_GameWorld.GetECSWorld().DestroyManager(m_UpdateReplicatedOwnerFlag);

        m_TwistSystem.ShutDown();
        m_FanSystem.ShutDown();
        m_TranslateScaleSystem.ShutDown();

        m_resourceSystem.Shutdown();
    }

    void UpdateStateActive()
    {
        if (!m_GameplayStarted)
        {
            UpdateStateActiveTick();
            return;
        }

       if (m_GameOver) return;

       // Tick the game loop
       UpdateStateActiveTick();
        UpdatePlayerLives();

       // Tick global timer
        m_TimerManager.Tick(Time.deltaTime);
        m_ScoreManager.Tick(Time.deltaTime);
        m_PowerupManager.Tick(Time.deltaTime);

        // Get player position
        var playerPos = m_Player != null && m_Player.controlledEntity != Entity.Null
            ? GetPlayerPosition()
            : Vector3.zero;

        // Tick game mode
        System.Action<float> onShootPlayer = (damage) => OnPlayerHit(damage);

        if (m_Mode == Mode.Wave && m_WaveManager != null)
        {
           m_WaveManager.Tick(Time.deltaTime, playerPos, onShootPlayer, m_GameWorld);
           CheckWaveKills();
            m_HudUI.UpdateStats(
                "Lives: " + m_LivesRemaining + "    Score: " + m_ScoreManager.totalScore + "    Time: " + m_TimerManager.GetFormattedTime(),
                m_WaveManager.GetProgressText(),
                m_WaveManager.GetAnnouncementText());
        }
        else if (m_Mode == Mode.Explore && m_ExploreManager != null)
        {
           m_ExploreManager.Tick(Time.deltaTime, playerPos, onShootPlayer, m_GameWorld);
           CheckExploreKills();
            m_HudUI.UpdateStats(
                "Lives: " + m_LivesRemaining + "    Score: " + m_ScoreManager.totalScore + "    Time: " + m_TimerManager.GetFormattedTime(),
               m_ExploreManager.GetProgressText(),
                "");
        }

        // Check game over
        if (m_TimerManager.IsExpired)
        {
            OnGameOver("Time's up!");
        }
    }

    void OnRobotKilled(AIController robot)
    {
        var isA2 = robot.robotType == RobotType.A2_Hunter;
        m_ScoreManager.AddKill(isA2 ? 15 : 10, isA2);
    }

    void CreateRobotEntity(AIController robot, Vector3 position)
    {
        position = ResolveRobotSpawnPosition(position);
        robot.ResetSpawnPosition(position);

        var player = m_PlayerModuleServer.CreatePlayer(m_GameWorld, m_NextBotPlayerId++, robot.robotType.ToString(), true);
        player.teamIndex = 1;
        player.playerName = robot.robotType == RobotType.A1_Infantry ? "A1 Robot" : "A2 Robot";

        var playerEntity = player.gameObject.GetComponent<GameObjectEntity>().Entity;
        var entityManager = m_GameWorld.GetEntityManager();
        var characterControl = entityManager.GetComponentObject<PlayerCharacterControl>(playerEntity);
        characterControl.characterType = 0;
        characterControl.requestedCharacterType = 0;

        CharacterSpawnRequest.Create(entityManager, 0, position, Quaternion.Euler(0f, UnityEngine.Random.Range(0f, 360f), 0f), playerEntity);
        robot.BindCharacter(player);
        GameDebug.Log($"Spawned robot entity {player.playerName} at {position}");
    }

    void ConfirmSelection(Mode mode, Difficulty difficulty)
    {
        if (m_GameplayStarted) return;

        m_Mode = mode;
        m_Difficulty = difficulty;
       m_DiffConfig = DifficultyConfig.GetConfig(difficulty.ToString());
       m_LivesRemaining = m_DiffConfig.maxLives;
       m_PlayerHealth = m_DiffConfig.playerMaxHealth;
        m_PlayerDeathTracked = false;
        m_ScoreManager.Reset();
        m_TimerManager = new TimerManager(15f);

        m_SpawnCenter = Vector3.zero;
        foreach (var spawnPoint in UnityEngine.Object.FindObjectsOfType<SpawnPoint>())
        {
            if (spawnPoint.teamIndex == m_Player.teamIndex)
            {
                m_SpawnCenter = spawnPoint.transform.position;
                m_RobotSpawnForward = spawnPoint.transform.forward;
                m_RobotSpawnForward.y = 0f;
                m_RobotSpawnForward.Normalize();
            }
        }

        if (m_SpawnCenter == Vector3.zero)
            m_SpawnCenter = GetPlayerPosition();
        m_PowerupManager = new PowerupManager(m_DiffConfig, m_SpawnCenter);

        if (m_Mode == Mode.Wave)
            m_WaveManager = new WaveManager(m_DiffConfig, m_SpawnCenter, m_RobotSpawnForward, OnRobotKilled, CreateRobotEntity);
        else
            m_ExploreManager = new ExploreManager(m_DiffConfig, m_SpawnCenter, m_RobotSpawnForward, OnRobotKilled, CreateRobotEntity);

        if (m_MenuUI != null)
            UnityEngine.Object.Destroy(m_MenuUI.gameObject);

        Game.SetMousePointerLock(true);
        m_GameplayStarted = true;
        GameDebug.Log($"SinglePlayer active! Mode:{m_Mode} Difficulty:{m_Difficulty} Score:{m_ScoreManager.totalScore}");
    }

    Vector3 GetPlayerPosition()
    {
        if (m_Player == null || m_Player.controlledEntity == Entity.Null) return Vector3.zero;
        var entityManager = m_GameWorld.GetEntityManager();
        if (entityManager.HasComponent<CharacterPredictedData>(m_Player.controlledEntity))
        {
            var data = entityManager.GetComponentData<CharacterPredictedData>(m_Player.controlledEntity);
            return new Vector3(data.position.x, data.position.y, data.position.z);
        }
        if (entityManager.HasComponent<CharacterInterpolatedData>(m_Player.controlledEntity))
        {
            var data = entityManager.GetComponentData<CharacterInterpolatedData>(m_Player.controlledEntity);
            return new Vector3(data.position.x, data.position.y, data.position.z);
        }
        return m_Player.transform.position;
    }

    void ShowResult(string reason)
    {
        Game.SetMousePointerLock(false);
        if (m_ResultUI == null)
        {
            var resultObject = new GameObject("SinglePlayerResult");
            m_ResultUI = resultObject.AddComponent<SinglePlayerResultUI>();
        }

        var stats = "Score: " + m_ScoreManager.totalScore +
            "  Kills: " + m_ScoreManager.killCount +
            "  Max Combo: x" + m_ScoreManager.maxCombo;
        m_ResultUI.Initialize(
            reason,
            stats,
            () => Console.EnqueueCommandNoHistory("chris"),
            () => Console.EnqueueCommandNoHistory("quit"));
    }

    Vector3 ResolveRobotSpawnPosition(Vector3 position)
    {
        return position;
    }

    void OnPlayerHit(float damage)
    {
        if (m_ShieldMultiplier < 1f) damage *= 0.5f;
        m_PlayerHealth -= damage;
        GameDebug.Log($"Player hit! -{damage} HP:{m_PlayerHealth:F0}");

       if (m_PlayerHealth <= 0)
       {
           m_PlayerHealth = 0;
           // Respawn after 3s with score penalty
           m_ScoreManager.ApplyPenalty(0.8f);
           m_PlayerHealth = m_DiffConfig.playerMaxHealth;
           GameDebug.Log("Player down! Lives remaining: " + m_LivesRemaining + ". Respawning...");
       }
        else
        {
            // Passive health regen
            m_PlayerHealth = Mathf.Min(m_PlayerHealth + m_DiffConfig.playerHealthRegen * Time.deltaTime, m_DiffConfig.playerMaxHealth);
        }
    }

    void UpdatePlayerLives()
    {
        if (m_Player == null) return;

        var entity = m_Player.controlledEntity;
        if (entity == Entity.Null) return;

        var entityManager = m_GameWorld.GetEntityManager();
        if (!entityManager.HasComponent<HealthStateData>(entity)) return;

        var healthState = entityManager.GetComponentData<HealthStateData>(entity);
        if (healthState.health <= 0)
        {
           if (m_PlayerDeathTracked) return;
           m_PlayerDeathTracked = true;
           m_LivesRemaining--;
            m_ScoreManager.ApplyPenalty(0.8f);
           GameDebug.Log("Player down! Lives remaining: " + m_LivesRemaining);
            if (m_LivesRemaining <= 0)
            {
                OnGameOver("Out of lives!");
            }
        }
        else
        {
            m_PlayerDeathTracked = false;
        }
    }

    void CheckWaveKills()
   {
       if (m_WaveManager == null) return;
        if (!m_WaveManager.isWaveActive && m_WaveManager.currentWave > m_LastBonusWave)
        {
            m_ScoreManager.AddWaveBonus();
            m_LastBonusWave = m_WaveManager.currentWave;
        }
    }

    void CheckExploreKills()
    {
        if (m_ExploreManager == null) return;
        if (m_ExploreManager.IsVictory())
        {
            m_ScoreManager.AddWaveBonus();
            OnGameOver("Victory!");
        }
    }

    void OnGameOver(string reason)
    {
        m_GameOver = true;
        ShowResult(reason);
        GameDebug.Log($"GAME OVER! {reason}");
        GameDebug.Log($"Score: {m_ScoreManager.totalScore} | Kills: {m_ScoreManager.killCount} | Max Combo: x{m_ScoreManager.maxCombo}");
        GameDebug.Log("Press 'chris' in console to play again, or 'boot' to return to menu.");
    }

    public void FixedUpdate() { }

    public void SinglePlayerTickUpdate()
    {
        m_GameWorld.worldTime = gameTime;
        m_GameWorld.frameDuration = gameTime.tickDuration;

        m_PlayerModuleClient.ResolveReferenceFromLocalPlayerToPlayer();
        m_PlayerModuleClient.HandleCommandReset();
        m_PlayerModuleClient.StoreCommand(m_GameWorld.worldTime.tick);

        m_previewGameMode.Update();

        m_CharacterModule.HandleSpawnRequests();
        m_ProjectileModule.HandleProjectileRequests();
        m_HandleGrenadeRequests.Update();
        m_UpdatePresentationOwners.Update();
        m_UpdateReplicatedOwnerFlag.Update();
        m_PlayerModuleClient.RetrieveCommand(m_GameWorld.worldTime.tick);

        m_CharacterModule.HandleSpawns();
        m_SpectatorCamModuleServer.HandleSpawnRequests();
        m_HitCollisionModule.HandleSpawning();
        m_HandleNamePlateOwnerSpawn.Update();
        m_PlayerModuleClient.HandleSpawn();
        m_ragdollModule.HandleSpawning();
        m_TwistSystem.HandleSpawning();
        m_FanSystem.HandleSpawning();
        m_TranslateScaleSystem.HandleSpawning();
        m_ProjectileModule.HandleProjectileSpawn();
        m_ItemModule.HandleSpawn();

        m_PlayerModuleClient.HandleControlledEntityChanged();
        m_CharacterModule.HandleControlledEntityChanged();

        m_SpinSystem.Update();
        m_moverUpdate.Update();
        m_ProjectileModule.StartPredictedMovement();
        m_StartGrenadeMovement.Update();

        m_SpectatorCamModuleClient.Update();
        m_TeleporterSystemServer.Update();
        m_CharacterModule.AbilityRequestUpdate();
        m_CharacterModule.MovementStart();
        m_CharacterModule.MovementResolve();
        m_CharacterModule.AbilityStart();
        m_CharacterModule.AbilityResolve();

        m_FinalizeGrenadeMovement.Update();
        m_ProjectileModule.FinalizePredictedMovement();

        m_HitCollisionModule.HandleSplashDamage();
        m_UpdateDestructableProps.Update();
        m_DamageAreaSystemServer.Update();
        m_CharacterModule.HandleDamage();

        m_CharacterModule.UpdatePresentation();
        m_DestructiblePropSystemClient.Update();
        m_TeleporterSystemClient.Update();
        m_ApplyGrenadePresentation.Update();

        m_HandlePresentationOwnerDespawn.Update();
        m_CharacterModule.HandleDepawns();
        m_DespawnProjectiles.Update();
        m_ProjectileModule.HandleProjectileDespawn();
        m_HandleNamePlateOwnerDespawn.Update();
        m_TwistSystem.HandleDespawning();
        m_FanSystem.HandleDespawning();
        m_ragdollModule.HandleDespawning();
        m_HitCollisionModule.HandleDespawn();
        m_TranslateScaleSystem.HandleDepawning();
        m_GameWorld.ProcessDespawns();
    }

   public void LateUpdate()
   {
        if (m_StateMachine != null && m_GameWorld != null && m_StateMachine.CurrentState() == SinglePlayerState.Active &&
            m_TranslateScaleSystem != null && m_TwistSystem != null && m_FanSystem != null && m_HitCollisionModule != null &&
            m_CharacterModule != null && m_ItemModule != null && m_ragdollModule != null && m_ProjectileModule != null &&
            m_EffectModule != null && m_PlayerModuleClient != null && m_UpdateNamePlates != null)
       {
            m_GameWorld.frameDuration = Time.deltaTime;

            m_TranslateScaleSystem.Schedule();
            var twistSystemHandle = m_TwistSystem.Schedule();
            m_FanSystem.Schedule(twistSystemHandle);

            m_HitCollisionModule.StoreColliderState();
            m_CharacterModule.LateUpdate();
            m_ItemModule.LateUpdate();
            m_ragdollModule.LateUpdate();
            m_ProjectileModule.UpdateClientProjectilesPredicted();
            m_EffectModule.ClientUpdate();
            m_PlayerModuleClient.CameraUpdate();
            m_CharacterModule.UpdateUI();
            m_UpdateNamePlates.Update();

            m_TranslateScaleSystem.Complete();
            m_FanSystem.Complete();
        }
    }

    void UpdateStateActiveTick()
    {
        bool userInputEnabled = Game.GetMousePointerLock();
        m_PlayerModuleClient.SampleInput(userInputEnabled, Time.deltaTime, 0);

        if (gameTime.tickRate != Game.serverTickRate.IntValue)
            gameTime.tickRate = Game.serverTickRate.IntValue;

        if (Game.Input.GetKeyUp(KeyCode.H) && Game.allowCharChange.IntValue == 1)
            CmdNextHero(null);

        bool commandWasConsumed = false;
        while (Game.frameTime > m_GameWorld.nextTickTime)
        {
            gameTime.tick++;
            gameTime.tickDuration = gameTime.tickInterval;
            commandWasConsumed = true;
            SinglePlayerTickUpdate();
            m_GameWorld.nextTickTime += m_GameWorld.worldTime.tickInterval;
        }
        if (commandWasConsumed)
            m_PlayerModuleClient.ResetInput(userInputEnabled);
    }

    void CmdNextHero(string[] args)
    {
        if (m_Player == null || Game.allowCharChange.IntValue != 1) return;
        var charSetupRegistry = m_resourceSystem.GetResourceRegistry<HeroTypeRegistry>();
        var playerEntity = m_Player.gameObject.GetComponent<GameObjectEntity>().Entity;
        var charControl = m_GameWorld.GetEntityManager().GetComponentObject<PlayerCharacterControl>(playerEntity);
        charControl.requestedCharacterType = charControl.characterType + 1;
        if (charControl.requestedCharacterType >= charSetupRegistry.entries.Count)
            charControl.requestedCharacterType = 0;
    }

    void CmdSpectatorCam(string[] args)
    {
        if (m_Player == null || Game.allowCharChange.IntValue != 1) return;
        var playerEntity = m_Player.gameObject.GetComponent<GameObjectEntity>().Entity;
        var charControl = m_GameWorld.GetEntityManager().GetComponentObject<PlayerCharacterControl>(playerEntity);
        charControl.requestedCharacterType = 1000;
    }

    void CmdRespawn(string[] args)
    {
        if (m_Player == null) return;
        m_previewGameMode.respawnDelay = args.Length == 0 ? 3 : int.Parse(args[0]);
    }

    void CmdShowScore(string[] args)
    {
        GameDebug.Log($"Score: {m_ScoreManager.totalScore} | Kills: {m_ScoreManager.killCount} | Combo: x{m_ScoreManager.currentCombo} | Time: {m_TimerManager.GetFormattedTime()}");
    }
}
