using System.Collections.Generic;
using UnityEngine;
using Unity.Entities;
using UnityEditor;


[RequireComponent(typeof(LocalPlayer))]
[RequireComponent(typeof(PlayerCameraSettings))]
public class LocalPlayerCharacterControl : MonoBehaviour     
{
    [ConfigVar(Name = "char.showhistory", DefaultValue = "0", Description = "Show last char loco states")]
    public static ConfigVar ShowHistory;
    
    public Entity lastRegisteredControlledEntity;      

    public CharacterHealthUI healthUI;
    public IngameHUD hud;

    public int lastDamageInflictedTick;
    public int lastDamageReceivedTick;
    public bool isThirdPerson;

    public List<AbilityUI> registeredCharUIs = new List<AbilityUI>();

    public class FirstPersonData
    {
        public Entity char3P;
        public Entity char1P;
        public List<CharacterPresentationSetup> presentations = new List<CharacterPresentationSetup>();
    }

    public FirstPersonData firstPerson = new FirstPersonData(); 
}


[DisableAutoCreation]
public class UpdateCharacter1PSpawn : BaseComponentSystem  
{   
    ComponentGroup Group;
    
    public UpdateCharacter1PSpawn(GameWorld world, BundledResourceManager resourceManager) : base(world)
    {
        m_ResourceManager = resourceManager;
    }

    protected override void OnCreateManager()
    {
        base.OnCreateManager();
        Group = GetComponentGroup(typeof(LocalPlayer), typeof(LocalPlayerCharacterControl));
    }


    private List<LocalPlayerCharacterControl> charControlBuffer = new List<LocalPlayerCharacterControl>(2);
    private List<Entity> entityBuffer = new List<Entity>(2);
    protected override void OnUpdate()
    {
        charControlBuffer.Clear();
        entityBuffer.Clear();
        var charControlArray = Group.GetComponentArray<LocalPlayerCharacterControl>();
        var localPlayerArray = Group.GetComponentArray<LocalPlayer>();
        for (var i = 0; i < charControlArray.Length; i++)
        {
            var localPlayer = localPlayerArray[i];
            var characterControl = charControlArray[i];
            
            var controlledChar3PEntity = EntityManager.Exists(localPlayer.controlledEntity) && EntityManager.HasComponent<Character>(localPlayer.controlledEntity)
                ? localPlayer.controlledEntity
                : Entity.Null;

            if (characterControl.firstPerson.char3P != controlledChar3PEntity)
            {
                charControlBuffer.Add(characterControl);
                entityBuffer.Add(controlledChar3PEntity);
            }
        }

        if (charControlBuffer.Count > 0)
        {

            for (var i = 0; i < charControlBuffer.Count; i++)
            {
                var charCtrl = charControlBuffer[i];
                var charClientEntity = entityBuffer[i];

                // Despawn all previous presentation
                foreach (var charPresentation in charCtrl.firstPerson.presentations)
                {
                    m_world.RequestDespawn(charPresentation.gameObject, PostUpdateCommands);
                }
                charCtrl.firstPerson.presentations.Clear();
                charCtrl.firstPerson.char1P = Entity.Null;
                charCtrl.firstPerson.char3P = charClientEntity;

                // TODO (mogensh) do all creation of character presentation one place ?
                // Spawn new 1P Presentation
                if (charClientEntity != Entity.Null)
                {
                    GameDebug.Log("Spawning 1P char and items");

                    var character = EntityManager.GetComponentObject<Character>(charClientEntity);
        
                    // Create 1P character
                    var char1PGUID = character.heroTypeData.character.prefab1P;
                    var prefab1P = m_ResourceManager.GetSingleAssetResource(char1PGUID) as GameObject;
                    var char1PGOE = m_world.Spawn<GameObjectEntity>(prefab1P);
                    charCtrl.firstPerson.char1P = char1PGOE.Entity;

                    var char1PEntity = char1PGOE.Entity;
                    var char1PPresentation = EntityManager.GetComponentObject<CharacterPresentationSetup>(char1PEntity);
                    char1PPresentation.character = charClientEntity;
                    char1PPresentation.updateTransform = false;
                    char1PPresentation.isFirstPerson = true;
                    charCtrl.firstPerson.presentations.Add(char1PPresentation);
                    
                    // Create 1P items
                    foreach (var itemEntry in character.heroTypeData.items)
                    {
                        var item1PGUID = itemEntry.itemType.prefab1P;
                        if (item1PGUID.IsSet())
                        {
                            var itemPrefab1P = m_ResourceManager.GetSingleAssetResource(item1PGUID) as GameObject;
                            var itemGOE = m_world.Spawn<GameObjectEntity>(itemPrefab1P);
                            var itemEntity = itemGOE.Entity;
                            
                            var itemPresentation = EntityManager.GetComponentObject<CharacterPresentationSetup>(itemEntity);
                            itemPresentation.character = charClientEntity;
                            itemPresentation.attachToPresentation = char1PEntity;
                            itemPresentation.isFirstPerson = true;
                            charCtrl.firstPerson.presentations.Add(itemPresentation);
                        }
                    }
                }
            }
        }
    }

    BundledResourceManager m_ResourceManager;
}




[DisableAutoCreation]
public class UpdateCharacterCamera : BaseComponentSystem<LocalPlayer,LocalPlayerCharacterControl,PlayerCameraSettings>
{
    private const float k_default3PDisst = 4.5f;
    private const float k_thirdPersonAimDistance = 500f;
    public static float selfTestCameraYaw = float.MinValue;
    static readonly HashSet<string> firstPersonOnlyHeroes = new HashSet<string>
    {
        "Hero_M4A1", "Hero_MP5", "Hero_AK47", "Hero_M700"
    };
    private float camDist3P = k_default3PDisst; 
    
    public UpdateCharacterCamera(GameWorld world) : base(world) {}

    public static bool IsFirstPersonOnlyHero(string heroName)
    {
        return !string.IsNullOrEmpty(heroName) && firstPersonOnlyHeroes.Contains(heroName);
    }

    public void ToggleFOrceThirdPerson()   
    {
        var heroName = GetCurrentHeroName();
        if (IsFirstPersonOnlyHero(heroName))
        {
            forceThirdPerson = false;
            GameDebug.Log("Third person unavailable for: " + heroName);
            Debug.Log("Third person unavailable for: " + heroName);
            return;
        }

        forceThirdPerson = !forceThirdPerson;
        var viewName = forceThirdPerson ? "ThirdPerson" : "FirstPerson";
        GameDebug.Log("Camera view switched: " + viewName);
        Debug.Log("Camera view switched: " + viewName);
    }

    protected override void Update(Entity entity, LocalPlayer localPlayer, LocalPlayerCharacterControl characterControl, PlayerCameraSettings cameraSettings)
    {
    
        
        if (localPlayer.controlledEntity == Entity.Null || !EntityManager.HasComponent<Character>(localPlayer.controlledEntity))
        {
            controlledEntity = Entity.Null;
            return;
        }
            
        if (characterControl.firstPerson.char1P == Entity.Null)
        {
            controlledEntity = Entity.Null;
            return;
        }

        GameDebug.Assert(EntityManager.HasComponent<CharacterInterpolatedData>(localPlayer.controlledEntity),"Controlled entity has no animstate");

        var character = EntityManager.GetComponentObject<Character>(localPlayer.controlledEntity);
        var charPredictedState = EntityManager.GetComponentData<CharacterPredictedData>(localPlayer.controlledEntity);
        
        var animState = EntityManager.GetComponentData<CharacterInterpolatedData>(localPlayer.controlledEntity);
        var character1P = EntityManager.GetComponentObject<Character1P>(characterControl.firstPerson.char1P);

        // Check if this is first time update is called with this controlled entity
        var characterChanged = localPlayer.controlledEntity != controlledEntity;
        if (characterChanged)
        {
            controlledEntity = localPlayer.controlledEntity;
            
        }            

        // Update character visibility
        if (character.heroTypeData != null && IsFirstPersonOnlyHero(character.heroTypeData.name))
            forceThirdPerson = false;

        var camProfile = forceThirdPerson ? CameraProfile.ThirdPerson : charPredictedState.cameraProfile;
        var isM700Scoped = character.heroTypeData != null && character.heroTypeData.name == "Hero_M700" &&
            (AutomaticRifleUI.IsM700Scoped || AutomaticRifleUI.selfTestScopeForced ||
             Ability_AutoRifle.IsAiming(EntityManager, localPlayer.controlledEntity));
        if (isM700Scoped)
        {
            camProfile = CameraProfile.FirstPerson;
            forceThirdPerson = false;
        }
        var thirdPerson = camProfile != CameraProfile.FirstPerson;
        characterControl.isThirdPerson = thirdPerson;
        if (Time.frameCount % 30 == 0)
            Debug.Log($"Camera state debug: frame={Time.frameCount} force={forceThirdPerson} profile={camProfile} third={thirdPerson}");
        foreach (var charPress in character.presentations)
        {
            charPress.SetVisible(thirdPerson);
        }
        foreach (var charPress in characterControl.firstPerson.presentations)
        {
            charPress.SetVisible(!thirdPerson);
        }
      
        // Update camera settings
        var userCommand = EntityManager.GetComponentData<UserCommandComponentData>(localPlayer.controlledEntity);
        var lookRotation = userCommand.command.lookRotation;
        if (selfTestCameraYaw >= 0f)
            lookRotation = Quaternion.Euler(0f, selfTestCameraYaw, 0f);
        
        cameraSettings.isEnabled = true;

        // Update FOV
        if(characterChanged)
            cameraSettings.fieldOfView = Game.configFov.FloatValue;
        var settings = character.heroTypeData.sprintCameraSettings;
        var targetFOV = animState.sprinting == 1 ? settings.FOVFactor* Game.configFov.FloatValue : Game.configFov.FloatValue;
        var speed = targetFOV > cameraSettings.fieldOfView ? settings.FOVInceraetSpeed : settings.FOVDecreaseSpeed;

        cameraSettings.fieldOfView = Mathf.MoveTowards(cameraSettings.fieldOfView, targetFOV, speed);
        
        switch (camProfile)
        {
            case CameraProfile.FirstPerson:
            {
                var eyePos = charPredictedState.position + Vector3.up*character.eyeHeight;
                
                // Set camera position and adjust 1P char. As 1P char is scaled down we need to "up-scale" camera
                // animation to world space. We dont want to upscale cam transform relative to 1PChar so we adjust
                // position accordingly
                var camLocalOffset = character1P.cameraTransform.position - character1P.transform.position;
                var cameraRotationOffset = Quaternion.Inverse(character1P.transform.rotation)*character1P.cameraTransform.rotation;
                var camWorldOffset = camLocalOffset/character1P.transform.localScale.x;  
                var camWorldPos = eyePos + camWorldOffset;
                var charWorldPos = camWorldPos - camLocalOffset;

                cameraSettings.position = camWorldPos;
                cameraSettings.rotation = userCommand.command.lookRotation * cameraRotationOffset;

                var char1PPresentation = EntityManager.GetComponentObject<CharacterPresentationSetup>(characterControl.firstPerson.char1P);
                char1PPresentation.transform.position = charWorldPos;
                char1PPresentation.transform.rotation = userCommand.command.lookRotation;

                break;
            }

            case CameraProfile.Shoulder:
            case CameraProfile.ThirdPerson:
            {
#if UNITY_EDITOR
                if (Input.GetAxis("Mouse ScrollWheel") > 0)
                {
                    camDist3P -= 0.2f;
                }
                if (Input.GetAxis("Mouse ScrollWheel") < 0)
                {
                    camDist3P += 0.2f;
                }
#endif                    
                
                
                var eyePos = charPredictedState.position + Vector3.up*character.eyeHeight;
                cameraSettings.rotation = lookRotation;

                // Simpe offset of camera for better 3rd person view. This is only for animation debug atm
                var viewDir = cameraSettings.rotation * Vector3.forward;
                var cameraOffset = -camDist3P * viewDir
                    + lookRotation * Vector3.right * 0.8f
                    + lookRotation * Vector3.up * 0.8f;
                var desiredCameraPosition = eyePos + cameraOffset;

                if (Physics.Raycast(eyePos, cameraOffset.normalized, out var cameraHit,
                    cameraOffset.magnitude, 1 << LayerMask.NameToLayer("Default"),
                    QueryTriggerInteraction.Ignore))
                {
                    desiredCameraPosition = eyePos + cameraOffset.normalized *
                        Mathf.Max(cameraHit.distance - 0.15f, 0.1f);
                }

                cameraSettings.position = desiredCameraPosition;
                var aimDirection = lookRotation * Vector3.forward;
                var aimPoint = eyePos + aimDirection * k_thirdPersonAimDistance;
                var aimLayerMask = ~0;
                if (Physics.Raycast(eyePos + aimDirection * 0.5f, aimDirection, out var aimHit,
                    k_thirdPersonAimDistance, aimLayerMask, QueryTriggerInteraction.Ignore))
                {
                    aimPoint = aimHit.point;
                }
                cameraSettings.rotation = Quaternion.LookRotation(aimPoint - desiredCameraPosition, Vector3.up);
            break;
        }
    }
        
        
        // TODO (mogensh) find better place to put this. 
        if (LocalPlayerCharacterControl.ShowHistory.IntValue > 0)
        {
            character.ShowHistory(m_world.worldTime.tick);
        }
    }

    string GetCurrentHeroName()
    {
        var localPlayerGroup = GetComponentGroup(typeof(LocalPlayer));
        var localPlayerArray = localPlayerGroup.GetComponentArray<LocalPlayer>();
        for (var index = 0; index < localPlayerArray.Length; index++)
        {
            var localPlayer = localPlayerArray[index];
            if (localPlayer.controlledEntity == Entity.Null ||
                !EntityManager.Exists(localPlayer.controlledEntity) ||
                !EntityManager.HasComponent<Character>(localPlayer.controlledEntity))
                continue;

            var character = EntityManager.GetComponentObject<Character>(localPlayer.controlledEntity);
            if (character != null && character.heroTypeData != null)
                return character.heroTypeData.name;
        }

        return null;
    }

    bool forceThirdPerson;
    Entity controlledEntity;
}
