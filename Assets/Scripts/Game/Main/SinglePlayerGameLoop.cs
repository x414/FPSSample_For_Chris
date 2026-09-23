using System;
using System.Collections.Generic;
using System.Linq;
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
    public static bool suppressRealPlayerDamage;
    public static System.Action<float> redirectedPlayerDamage;
    public static Entity redirectedPlayerEntity;
    public static System.Func<int> redirectedPlayerVisualHealth;

    public enum Mode { Wave, Explore, TestWave, TestExplore, AIBattle }
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
    AIBattleManager m_AIBattleManager;
    ScoreManager m_ScoreManager;
    TimerManager m_TimerManager;
    DailyPlayTimeTracker m_PlayTimeTracker;
    PowerupManager m_PowerupManager;
    SinglePlayerMenuUI m_MenuUI;
    SinglePlayerHudUI m_HudUI;
    Vector3 m_SpawnCenter;
    Vector3 m_RobotSpawnForward = Vector3.forward;
    SinglePlayerResultUI m_ResultUI;
    float m_rocketLastFireTime;
    bool m_RocketPendingFire;
    bool m_RocketAimNearest;
    bool m_RocketAimGround;
    float m_RocketScoreLogTime;
    int m_RocketPendingAttempts;
    float m_RocketNextAttemptTime;
    int m_RocketBaselineKills;
    float[] m_RocketShotSchedule;
    float m_RocketShotBaseTime;

   bool m_GameOver;
    bool m_GameplayStarted;
    bool m_AutoStart;
    bool m_DeveloperSelfTest;
    bool m_VoiceSelfTest;
    bool m_VisualSelfTest;
    bool m_VisualSelfTestThirdPersonRequested;
    bool m_VisualSelfTestGunRequested;
    bool m_VisualSelfTestScope;
    string m_VisualSelfTestGun;
    float m_VisualSelfTestStartTime;
    int m_VisualSelfTestScreenshotIndex;
    bool m_PlayerDeathTracked;
    int m_LastBonusWave;
   int m_NextBotPlayerId = 100;
    int m_LivesRemaining;
   float m_PlayerHealth;
    float m_ShieldMultiplier = 1f;
    float m_PlayTimeWarningTimer;
    float m_StartupGraceTimer;
    string m_PendingGunName;
    const float StartupGracePeriod = 5f;

    public bool Init(string[] args)
    {
        m_Mode = Mode.Wave;
        m_Difficulty = Difficulty.Normal;
        const string levelName = "level_01";

        foreach (var argument in args)
        {
            if (string.Equals(argument, "explore", StringComparison.OrdinalIgnoreCase))
                m_Mode = Mode.Explore;
            else if (string.Equals(argument, "test-wave", StringComparison.OrdinalIgnoreCase))
                m_Mode = Mode.TestWave;
            else if (string.Equals(argument, "test-explore", StringComparison.OrdinalIgnoreCase))
                m_Mode = Mode.TestExplore;
            else if (string.Equals(argument, "ai-battle", StringComparison.OrdinalIgnoreCase))
                m_Mode = Mode.AIBattle;
            else if (string.Equals(argument, "easy", StringComparison.OrdinalIgnoreCase))
                m_Difficulty = Difficulty.Easy;
            else if (string.Equals(argument, "hard", StringComparison.OrdinalIgnoreCase))
                m_Difficulty = Difficulty.Hard;
            else if (string.Equals(argument, "autostart", StringComparison.OrdinalIgnoreCase))
                m_AutoStart = true;
            else if (string.Equals(argument, "dev-selftest", StringComparison.OrdinalIgnoreCase))
                m_DeveloperSelfTest = true;
            else if (string.Equals(argument, "voice-selftest", StringComparison.OrdinalIgnoreCase))
                m_VoiceSelfTest = true;
            else if (string.Equals(argument, "visual-selftest", StringComparison.OrdinalIgnoreCase))
                m_VisualSelfTest = true;
            else if (argument.StartsWith("selftest-gun=", StringComparison.OrdinalIgnoreCase))
                m_VisualSelfTestGun = argument.Substring("selftest-gun=".Length);
            else if (string.Equals(argument, "selftest-scope", StringComparison.OrdinalIgnoreCase))
                m_VisualSelfTestScope = true;
        }

        m_DiffConfig = DifficultyConfig.GetConfig(m_Difficulty.ToString());
        m_PlayTimeTracker = new DailyPlayTimeTracker();
        if (m_DeveloperSelfTest)
            GameDebug.Log("Developer self-test active; daily play time recording disabled.");
        Debug.Log($"SinglePlayer visual self-test flags: visual={m_VisualSelfTest} gun={m_VisualSelfTestGun} scope={m_VisualSelfTestScope}");
        m_ScoreManager = new ScoreManager();
        m_TimerManager = new TimerManager(20f);
        m_GameOver = false;
        m_GameplayStarted = false;

        // Register console commands
        Console.AddCommand("nextchar", CmdNextHero, "Select next character", GetHashCode());
        Console.AddCommand("gun", CmdSelectHero, "Select a gun hero by name (M4A1, MP5, AK47, M700)", GetHashCode());
        Console.AddCommand("spectator", CmdSpectatorCam, "Select spectator cam", GetHashCode());
        Console.AddCommand("respawn", CmdRespawn, "Force a respawn", GetHashCode());
        Console.AddCommand("score", CmdShowScore, "Show current score", GetHashCode());
        Console.AddCommand("rocket", CmdFireRocket, "Fire rocket launcher. Optional arg 'nearest' auto-aims at nearest robot; 'ground' aims ahead/down for visual tests", GetHashCode());

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
       suppressRealPlayerDamage = false;
       redirectedPlayerDamage = null;
       redirectedPlayerVisualHealth = null;
        redirectedPlayerEntity = Entity.Null;
        if (m_PlayTimeTracker != null)
            m_PlayTimeTracker.Flush();

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
       redirectedPlayerEntity = playerEntity;

       m_previewGameMode = m_GameWorld.GetECSWorld().CreateManager<PreviewGameMode>(m_GameWorld, m_Player);
        m_previewGameMode.respawnDelay = 1;

       var menuObject = new GameObject("SinglePlayerMenu");
       m_MenuUI = menuObject.AddComponent<SinglePlayerMenuUI>();
       m_MenuUI.Initialize(ConfirmSelection, m_PlayTimeTracker, m_DeveloperSelfTest);

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

       if (m_Player != null)
           redirectedPlayerEntity = m_Player.controlledEntity;

       UpdateRocketLauncher();

       m_PlayTimeWarningTimer = Mathf.Max(0f, m_PlayTimeWarningTimer - Time.unscaledDeltaTime);
        if (!m_DeveloperSelfTest && m_PlayTimeTracker.ConsumeTenMinuteWarning())
        {
            m_PlayTimeWarningTimer = 5f;
            m_HudUI.AnnounceCue("TenMinuteWarning");
        }

       // Tick the game loop
       UpdateStateActiveTick();
        if (m_StartupGraceTimer > 0f)
            m_StartupGraceTimer -= Time.deltaTime;
       else if (m_Mode != Mode.AIBattle)
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

        if ((m_Mode == Mode.Wave || m_Mode == Mode.TestWave) && m_WaveManager != null)
        {
           m_WaveManager.Tick(Time.deltaTime, playerPos, onShootPlayer, m_GameWorld);
           CheckWaveKills();
           var waveAnnouncement = m_WaveManager.GetAnnouncementText();
            m_HudUI.UpdateStats(
                "Lives: " + m_LivesRemaining + "    Score: " + m_ScoreManager.totalScore + "    Time: " + m_TimerManager.GetFormattedTime(),
                m_WaveManager.GetProgressText(),
                waveAnnouncement);
           if (!m_VoiceSelfTest)
               m_HudUI.AnnounceWave(m_WaveManager.currentWave, m_WaveManager.waveTotalEnemies, waveAnnouncement);
        }
        else if ((m_Mode == Mode.Explore || m_Mode == Mode.TestExplore) && m_ExploreManager != null)
        {
           m_ExploreManager.Tick(Time.deltaTime, playerPos, onShootPlayer, m_GameWorld);
           CheckExploreKills();
            m_HudUI.UpdateStats(
                "Lives: " + m_LivesRemaining + "    Score: " + m_ScoreManager.totalScore + "    Time: " + m_TimerManager.GetFormattedTime(),
               m_ExploreManager.GetProgressText(),
                "");
        }
        else if (m_Mode == Mode.AIBattle)
        {
            if (m_AIBattleManager == null && playerPos != Vector3.zero)
                m_AIBattleManager = new AIBattleManager(m_DiffConfig, playerPos, m_RobotSpawnForward, OnRobotKilled, CreateRobotEntity);

            if (m_AIBattleManager == null)
                return;

            m_AIBattleManager.Tick(Time.deltaTime, playerPos, onShootPlayer, m_GameWorld);
            if (m_StartupGraceTimer <= 0f)
                CheckAIBattleKills();
            m_HudUI.UpdateStats(
                "Lives: " + m_LivesRemaining + "    Score: " + m_ScoreManager.totalScore + "    Time: " + m_TimerManager.GetFormattedTime(),
                m_AIBattleManager.GetProgressText(),
                "");
            m_HudUI.UpdatePlayerHealth(m_PlayerHealth, m_DiffConfig.playerMaxHealth);
        }

        var playTimeExempt = IsPlayTimeExempt(m_Mode);
        m_HudUI.UpdatePlayTimeWarning(!playTimeExempt && m_PlayTimeWarningTimer > 0f
            ? m_PlayTimeTracker.GetTenMinuteWarningMessage()
            : string.Empty);

        // Check game over
        if (m_StartupGraceTimer <= 0f && m_TimerManager.IsExpired)
        {
            OnGameOver("Time's up!");
        }

        if (!playTimeExempt)
            m_PlayTimeTracker.Record(Time.unscaledDeltaTime, Game.GetMousePointerLock());
        bool isPlayTimeLimited = !playTimeExempt;
        if (isPlayTimeLimited && m_PlayTimeTracker.IsLimitReached)
            OnGameOver(m_PlayTimeTracker.GetLimitMessage());
    }

    void OnRobotKilled(AIController robot)
    {
        var isA2 = robot.robotType == RobotType.A2_Hunter;
        m_ScoreManager.AddKill(robot.robotType == RobotType.A3_Tactician ? 50 : isA2 ? 15 : 10, isA2);
    }

    void CreateRobotEntity(AIController robot, Vector3 position)
    {
        position = ResolveRobotSpawnPosition(position);
        robot.ResetSpawnPosition(position);

        var player = m_PlayerModuleServer.CreatePlayer(m_GameWorld, m_NextBotPlayerId++, robot.robotType.ToString(), true);
        player.teamIndex = 1;
        player.playerName = robot.robotType == RobotType.A1_Infantry ? "A1 Robot"
            : robot.robotType == RobotType.A3_Tactician ? "A3 Tactician" : "A2 Robot";

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
        if (!IsPlayTimeExempt(mode) && m_PlayTimeTracker != null &&
            m_PlayTimeTracker.IsLimitReached) return;

        m_Mode = mode;
        m_Difficulty = difficulty;
       m_DiffConfig = DifficultyConfig.GetConfig(difficulty.ToString());
       suppressRealPlayerDamage = mode == Mode.AIBattle;
       redirectedPlayerDamage = mode == Mode.AIBattle ? (System.Action<float>)OnPlayerHit : null;
       redirectedPlayerVisualHealth = mode == Mode.AIBattle ? (System.Func<int>)GetPlayerVisualHealth : null;
       m_LivesRemaining = m_DiffConfig.maxLives;
       m_PlayerHealth = m_DiffConfig.playerMaxHealth;
        m_PlayerDeathTracked = false;
        m_ScoreManager.Reset();
        m_TimerManager = new TimerManager(IsTestMode(mode) || mode == Mode.AIBattle ? 5f : 20f);
        m_StartupGraceTimer = StartupGracePeriod;

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
        m_PowerupManager.PowerupActivated += type => m_HudUI.AnnouncePowerup(type);

        var baseMode = GetBaseMode(mode);
        if (mode == Mode.AIBattle)
        {
            m_AIBattleManager = new AIBattleManager(m_DiffConfig, m_SpawnCenter, m_RobotSpawnForward, OnRobotKilled, CreateRobotEntity);
            m_HudUI.AnnounceCue("AIBattleStart");
        }
        else if (baseMode == Mode.Wave)
            m_WaveManager = new WaveManager(m_DiffConfig, m_SpawnCenter, m_RobotSpawnForward, OnRobotKilled, CreateRobotEntity);
        else
        {
            m_ExploreManager = new ExploreManager(m_DiffConfig, m_SpawnCenter, m_RobotSpawnForward, OnRobotKilled, CreateRobotEntity,
                CollectExplorePatrolAnchors());
            m_HudUI.AnnounceCue("ExploreStart");
        }

        if (m_MenuUI != null)
            UnityEngine.Object.Destroy(m_MenuUI.gameObject);

        Game.SetMousePointerLock(true);
        m_GameplayStarted = true;
        if (m_VisualSelfTest)
            m_VisualSelfTestStartTime = Time.realtimeSinceStartup;
        if (m_DeveloperSelfTest && m_VoiceSelfTest)
            m_HudUI.AnnounceTestSequence();
        if (m_DeveloperSelfTest)
        {
            GameDebug.Log($"Display state: mode={Screen.fullScreenMode}, window={Screen.width}x{Screen.height}, " +
                $"desktop={Screen.currentResolution.width}x{Screen.currentResolution.height}@{Screen.currentResolution.refreshRate}");
            var isFullscreenWindow = Screen.fullScreenMode == FullScreenMode.FullScreenWindow;
            var isNativeResolution = Screen.width == Screen.currentResolution.width &&
                Screen.height == Screen.currentResolution.height;
            GameDebug.Log(isFullscreenWindow && isNativeResolution
                ? "Display self-test passed: native fullscreen window."
                : "Display self-test failed: window is not native fullscreen.");
        }
        GameDebug.Log($"SinglePlayer active! Mode:{m_Mode} Difficulty:{m_Difficulty} Score:{m_ScoreManager.totalScore}");
    }

    public static bool IsTestMode(Mode mode)
    {
        return mode == Mode.TestWave || mode == Mode.TestExplore;
    }

    bool IsPlayTimeExempt(Mode mode)
    {
        return m_DeveloperSelfTest || IsTestMode(mode) || mode == Mode.AIBattle;
    }

    public bool IsTestMode()
    {
        return IsTestMode(m_Mode);
    }

    static Mode GetBaseMode(Mode mode)
    {
        return mode == Mode.TestWave ? Mode.Wave
            : mode == Mode.TestExplore ? Mode.Explore
            : mode;
    }

    List<Vector3> CollectExplorePatrolAnchors()
    {
        var anchors = new List<Vector3>();
        var seen = new HashSet<Vector3>();

        foreach (var spawnPoint in UnityEngine.Object.FindObjectsOfType<SpawnPoint>())
        {
            if (spawnPoint.teamIndex != -1)
                continue;

            var position = spawnPoint.transform.position;
            position.y = Mathf.Max(0f, position.y);
            if (seen.Add(position))
                anchors.Add(position);
        }

        if (anchors.Count < 6)
        {
            foreach (var spawnPoint in UnityEngine.Object.FindObjectsOfType<SpawnPoint>())
            {
                if (spawnPoint.teamIndex == m_Player.teamIndex)
                    continue;

                var position = spawnPoint.transform.position;
                position.y = Mathf.Max(0f, position.y);
                if (seen.Add(position))
                    anchors.Add(position);
            }

        }

        GameDebug.Log($"Explore patrol anchors: {anchors.Count}");
        return anchors;
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
           m_ScoreManager.ApplyPenalty(0.8f);
           m_PlayerHealth = m_DiffConfig.playerMaxHealth;
           if (m_Mode == Mode.AIBattle)
           {
               m_LivesRemaining--;
               if (m_LivesRemaining <= 0)
                   OnGameOver("Out of lives!");
           }
           GameDebug.Log("Player down! Lives remaining: " + m_LivesRemaining + ". Respawning...");
       }
        else
        {
            // Passive health regen
            m_PlayerHealth = Mathf.Min(m_PlayerHealth + m_DiffConfig.playerHealthRegen * Time.deltaTime, m_DiffConfig.playerMaxHealth);
        }
    }

    int GetPlayerVisualHealth()
    {
        return Mathf.CeilToInt(m_PlayerHealth);
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
            m_HudUI.AnnounceCue("WaveCleared");
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

    void CheckAIBattleKills()
    {
        if (m_AIBattleManager == null) return;
        if (m_AIBattleManager.IsVictory())
        {
            m_ScoreManager.AddWaveBonus();
            OnGameOver("Victory!");
        }
    }

    void OnGameOver(string reason)
    {
        if (m_GameOver) return;

        m_GameOver = true;
        m_HudUI.AnnounceGameOver(reason);
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

        if (!string.IsNullOrEmpty(m_PendingGunName) && m_Player != null)
        {
            var requestedGun = m_PendingGunName;
            m_PendingGunName = null;
            CmdSelectHero(new[] { requestedGun });
        }

        if (m_VisualSelfTest)
            UpdateVisualSelfTest();

        if (gameTime.tickRate != Game.serverTickRate.IntValue)
            gameTime.tickRate = Game.serverTickRate.IntValue;

        if ((Game.Input.GetKeyUp(KeyCode.H) || Game.Input.GetKeyUp(KeyCode.Joystick1Button6)) &&
            Game.allowCharChange.IntValue == 1)
            CmdNextHero(null);

        if (!Console.IsOpen() &&
            (Game.Input.GetKeyDown(KeyCode.C) || Game.Input.GetKeyDown(KeyCode.Joystick1Button9)))
            m_CharacterModule.ToggleThirdPerson();

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

    void CmdSelectHero(string[] args)
    {
        if (args.Length == 0) return;
        if (m_Player == null)
        {
            m_PendingGunName = args[0];
            GameDebug.Log("Gun selection queued: " + args[0]);
            return;
        }
        if (Game.allowCharChange.IntValue != 1) return;
        var charSetupRegistry = m_resourceSystem.GetResourceRegistry<HeroTypeRegistry>();
        var requestedHeroName = args[0];
        var selectedHero = charSetupRegistry.entries.Find(entry =>
            entry != null && (entry.name.Equals(requestedHeroName, StringComparison.OrdinalIgnoreCase) ||
                               entry.name.Equals("Hero_" + requestedHeroName, StringComparison.OrdinalIgnoreCase)));
        if (selectedHero == null)
        {
            GameDebug.LogError("Unknown gun hero: " + args[0]);
            GameDebug.LogError("Available heroes: " + string.Join(", ", charSetupRegistry.entries.Select(entry => entry == null ? "<null>" : entry.name)));
            return;
        }

        var playerEntity = m_Player.gameObject.GetComponent<GameObjectEntity>().Entity;
        var charControl = m_GameWorld.GetEntityManager().GetComponentObject<PlayerCharacterControl>(playerEntity);
        charControl.requestedCharacterType = charSetupRegistry.entries.IndexOf(selectedHero);
        GameDebug.Log("Gun self-test selected: " + selectedHero.name);
    }

    void UpdateVisualSelfTest()
    {
        var elapsed = Time.realtimeSinceStartup - m_VisualSelfTestStartTime;
        Debug.Log($"Visual self-test tick elapsed={elapsed:F2} phase={m_VisualSelfTestScreenshotIndex} gun={m_VisualSelfTestGun} tp={m_VisualSelfTestThirdPersonRequested} gameplay={m_GameplayStarted}");
        if (elapsed > 1.0f && string.IsNullOrEmpty(m_VisualSelfTestGun))
        {
            m_VisualSelfTestGun = "Hero_M4A1";
            GameDebug.Log("Visual self-test: default gun selected.");
        }
        if (elapsed > 1.0f && !m_VisualSelfTestGunRequested && m_GameplayStarted)
        {
            CmdSelectHero(new[] { m_VisualSelfTestGun });
            m_VisualSelfTestGunRequested = true;
            m_VisualSelfTestScreenshotIndex = -1;
            GameDebug.Log("Visual self-test: gun requested " + m_VisualSelfTestGun);
        }
        if (elapsed > 3.0f && !m_VisualSelfTestThirdPersonRequested)
        {
            m_CharacterModule.ToggleThirdPerson();
            m_VisualSelfTestThirdPersonRequested = true;
            m_VisualSelfTestScreenshotIndex = 0;
            GameDebug.Log("Visual self-test: third person enabled.");
        }
        if (m_VisualSelfTestScope && elapsed > 6.5f && m_VisualSelfTestScreenshotIndex == 3 && !AutomaticRifleUI.selfTestScopeForced)
        {
            AutomaticRifleUI.selfTestScopeForced = true;
            AutomaticRifleUI.IsM700Scoped = true;
            ForceM700AbilityAiming();
            m_VisualSelfTestScreenshotIndex = 3;
            GameDebug.Log("Visual self-test: M700 scope forced.");
        }
        if (elapsed > 5.0f && m_VisualSelfTestScreenshotIndex >= 0 && m_VisualSelfTestScreenshotIndex < 3)
        {
            var screenshotIndex = m_VisualSelfTestScreenshotIndex;
            UpdateCharacterCamera.selfTestCameraYaw = screenshotIndex * 90f;
            m_VisualSelfTestScreenshotIndex++;
            var fileName = $"D:/Codex_Project/FPS-Unity/FPSSample/Build/Windows64/selftest_{m_VisualSelfTestGun}_{screenshotIndex}.png";
            ScreenCapture.CaptureScreenshot(fileName);
            GameDebug.Log($"Visual self-test: captured {fileName}");
        }
        if (m_VisualSelfTestScope && elapsed > 7.5f && m_VisualSelfTestScreenshotIndex == 4)
        {
            var screenshotIndex = m_VisualSelfTestScreenshotIndex;
            UpdateCharacterCamera.selfTestCameraYaw = 45f;
            m_VisualSelfTestScreenshotIndex++;
            var fileName = $"D:/Codex_Project/FPS-Unity/FPSSample/Build/Windows64/selftest_{m_VisualSelfTestGun}_{screenshotIndex}.png";
            ScreenCapture.CaptureScreenshot(fileName);
            GameDebug.Log($"Visual self-test: captured {fileName} with scope");
        }
        if (elapsed > 9.0f)
            Application.Quit();
    }

    void ForceM700AbilityAiming()
    {
        if (m_Player == null || m_Player.controlledEntity == Entity.Null || m_GameWorld == null) return;
        var entityManager = m_GameWorld.GetEntityManager();
        var characterReplicatedData = entityManager.GetComponentData<CharacterReplicatedData>(m_Player.controlledEntity);
        var ability = characterReplicatedData.FindAbilityWithComponent(entityManager, typeof(Ability_AutoRifle.PredictedState));
        if (ability == Entity.Null) return;
        var state = entityManager.GetComponentData<Ability_AutoRifle.PredictedState>(ability);
        state.aiming = true;
        state.lastAimButton = true;
        entityManager.SetComponentData(ability, state);
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

    void UpdateRocketLauncher()
    {
        if (m_Player == null || m_Player.controlledEntity == Entity.Null) return;

        if (m_RocketScoreLogTime > 0f && Time.time >= m_RocketScoreLogTime)
        {
            m_RocketScoreLogTime = 0f;
            CmdShowScore(null);
        }

        if (m_RocketShotSchedule != null && m_RocketShotSchedule.Length > 0 &&
            Time.time - m_RocketShotBaseTime >= m_RocketShotSchedule[0])
        {
            Console.EnqueueCommandNoHistory("screenshot");
            var rest = new float[m_RocketShotSchedule.Length - 1];
            System.Array.Copy(m_RocketShotSchedule, 1, rest, 0, rest.Length);
            m_RocketShotSchedule = rest.Length > 0 ? rest : null;
        }

        if (m_RocketPendingFire)
        {
            if (!m_GameplayStarted || m_GameOver) return;
            if (Time.time < m_RocketNextAttemptTime) return;

            if (m_RocketPendingAttempts > 0)
            {
                if (m_ScoreManager.killCount > m_RocketBaselineKills)
                {
                    GameDebug.Log($"Rocket test: kill confirmed after {m_RocketPendingAttempts} attempt(s).");
                    CmdShowScore(null);
                    m_RocketPendingFire = false;
                    m_RocketPendingAttempts = 0;
                    return;
                }
                if (m_RocketPendingAttempts >= 30)
                {
                    GameDebug.Log("Rocket test: gave up after 30 attempts.");
                    m_RocketPendingFire = false;
                    m_RocketPendingAttempts = 0;
                    return;
                }
            }

            m_RocketPendingAttempts++;
            m_RocketNextAttemptTime = Time.time + 4f;
            m_rocketLastFireTime = Time.time;
            GameDebug.Log($"Rocket test attempt {m_RocketPendingAttempts} (aimNearest:{m_RocketAimNearest}).");
            m_RocketShotSchedule = new float[] { 0.12f, 0.3f, 0.6f, 1.0f, 1.5f };
            m_RocketShotBaseTime = Time.time;
            FireRocket(m_RocketAimNearest);
            return;
        }

        if (!Game.GetMousePointerLock()) return;
        if (!Game.Input.GetKeyDown(KeyCode.Q) && !Game.Input.GetKeyDown(KeyCode.Joystick1Button3)) return;
        if (Time.time - m_rocketLastFireTime < 2.0f) return;

        m_rocketLastFireTime = Time.time;
        GameDebug.Log("Rocket launcher fired via Q key.");
        FireRocket(false);
    }

    void CmdFireRocket(string[] args)
    {
        m_RocketAimNearest = args != null && args.Length > 0 &&
            string.Equals(args[0], "nearest", StringComparison.OrdinalIgnoreCase);
        m_RocketAimGround = args != null && args.Length > 0 &&
            string.Equals(args[0], "ground", StringComparison.OrdinalIgnoreCase);
        m_RocketPendingFire = true;
        m_RocketPendingAttempts = 0;
        m_RocketNextAttemptTime = 0f;
        m_RocketBaselineKills = m_ScoreManager != null ? m_ScoreManager.killCount : 0;
        GameDebug.Log("Rocket fire queued. Waiting for gameplay to start.");
    }

    void FireRocket(bool aimNearest)
    {
        var entityManager = m_GameWorld.GetEntityManager();
        var playerEntity = m_Player.controlledEntity;
        if (!entityManager.HasComponent<CharacterPredictedData>(playerEntity))
        {
            GameDebug.Log("Rocket aborted: player has no CharacterPredictedData.");
            return;
        }
        if (!entityManager.HasComponent<Character>(playerEntity))
        {
            GameDebug.Log("Rocket aborted: player has no Character component.");
            return;
        }

        var charPredicted = entityManager.GetComponentData<CharacterPredictedData>(playerEntity);
        var character = entityManager.GetComponentObject<Character>(playerEntity);
        var command = entityManager.GetComponentData<UserCommandComponentData>(playerEntity).command;

        float3 eyePos = (float3)charPredicted.position + new float3(0f, character.eyeHeight, 0f);
        float3 aimDir = command.lookDir;

        if (m_RocketAimGround)
        {
            aimDir = math.normalize(aimDir * 0.8f + new float3(0f, -1f, 0f));
        }
        else if (aimNearest)
        {
            var nearest = FindNearestEnemyCharacter(entityManager, playerEntity, eyePos);
            if (nearest == Entity.Null)
            {
                GameDebug.Log("Rocket aborted: no living enemy character found.");
                return;
            }
            var targetPos = entityManager.GetComponentData<CharacterPredictedData>(nearest).position;
            aimDir = math.normalize((float3)targetPos + new float3(0f, 1f, 0f) - eyePos);
        }

        if (math.lengthsq(aimDir) < 0.01f)
        {
            GameDebug.Log("Rocket aborted: invalid aim direction.");
            return;
        }
        aimDir = math.normalize(aimDir);

        HandleClientProjectileRequests.SettingsOverride = new ProjectileSettings
        {
            velocity = 45f,
            impactDamage = 999999f,
            impactImpulse = 50000f,
            collisionRadius = 0.3f,
            splashDamage = new SplashDamageSettings
            {
                radius = 3f,
                falloffStartRadius = 1.5f,
                damage = 999999f,
                minDamage = 999999f,
                impulse = 15f,
                minImpulse = 5f,
                ownerDamageFraction = 0f,
            },
        };

        var requestEntity = entityManager.CreateEntity();
        entityManager.AddComponentData(requestEntity, new ProjectileRequest
        {
            projectileAssetGuid = new WeakAssetReference("0ae4f3eb805dfaa46b0fb17e468206f8"),
            startTick = m_GameWorld.worldTime.tick,
            startPosition = eyePos + aimDir * 0.6f,
            endPosition = eyePos + aimDir * 500f,
            owner = playerEntity,
            teamId = character.teamId,
            collisionTestTickDelay = 0,
        });

        GameDebug.Log("Rocket projectile launched.");
        m_RocketScoreLogTime = Time.time + 3f;
    }

    Entity FindNearestEnemyCharacter(EntityManager entityManager, Entity playerEntity, float3 from)
    {
        var best = Entity.Null;
        var bestDist = float.MaxValue;
        var entities = entityManager.GetAllEntities();
        try
        {
            for (var i = 0; i < entities.Length; i++)
            {
                var e = entities[i];
                if (e == playerEntity || !entityManager.HasComponent<CharacterPredictedData>(e)) continue;
                if (entityManager.HasComponent<HealthStateData>(e) &&
                    entityManager.GetComponentData<HealthStateData>(e).health <= 0f) continue;
                var dist = math.distance(from, entityManager.GetComponentData<CharacterPredictedData>(e).position);
                if (dist < bestDist)
                {
                    bestDist = dist;
                    best = e;
                }
            }
        }
        finally
        {
            entities.Dispose();
        }
        return best;
    }
}
