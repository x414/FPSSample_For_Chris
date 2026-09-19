using System;
using System.Collections.Generic;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

[CreateAssetMenu(fileName = "Ability_RocketLauncher", menuName = "FPS Sample/Abilities/Ability_RocketLauncher")]
public class Ability_RocketLauncher : CharBehaviorFactory
{
    public enum Phase
    {
        Idle,
        Active,
        Cooldown,
    }

    public struct LocalState : IComponentData
    {
        public int lastFireTick;
        public int rayQueryId;
    }

    [Serializable]
    public struct Settings : IComponentData
    {
        public UserCommand.Button activateButton;
        public float damage;
        public float damageImpulse;
        public float cooldownDuration;
        public float hitRadius;
        public float activationDuration;
    }

    public struct PredictedState : IPredictedComponent<PredictedState>, IComponentData
    {
        public Phase phase;
        public int phaseStartTick;

        public void SetPhase(Phase phase, int tick)
        {
            this.phase = phase;
            this.phaseStartTick = tick;
        }

        public static IPredictedComponentSerializerFactory CreateSerializerFactory()
        {
            return new PredictedComponentSerializerFactory<PredictedState>();
        }

        public void Serialize(ref SerializeContext context, ref NetworkWriter writer)
        {
            writer.WriteInt32("phase", (int)phase);
            writer.WriteInt32("phaseStart", phaseStartTick);
        }

        public void Deserialize(ref SerializeContext context, ref NetworkReader reader)
        {
            phase = (Phase)reader.ReadInt32();
            phaseStartTick = reader.ReadInt32();
        }

#if UNITY_EDITOR
        public bool VerifyPrediction(ref PredictedState state)
        {
            return phase == state.phase && phaseStartTick == state.phaseStartTick;
        }
#endif
    }

    public struct InterpolatedState : IInterpolatedComponent<InterpolatedState>, IComponentData
    {
        public int fireTick;

        public static IInterpolatedComponentSerializerFactory CreateSerializerFactory()
        {
            return new InterpolatedComponentSerializerFactory<InterpolatedState>();
        }

        public void Serialize(ref SerializeContext context, ref NetworkWriter writer)
        {
            writer.WriteInt32("fireTick", fireTick);
        }

        public void Deserialize(ref SerializeContext context, ref NetworkReader reader)
        {
            fireTick = reader.ReadInt32();
        }

        public void Interpolate(ref SerializeContext context, ref InterpolatedState first, ref InterpolatedState last, float t)
        {
            this = first;
        }
    }

    public Settings settings;

    public override Entity Create(EntityManager entityManager, List<Entity> entities)
    {
        var entity = CreateCharBehavior(entityManager);
        entities.Add(entity);

        entityManager.AddComponentData(entity, settings);
        entityManager.AddComponentData(entity, new LocalState());
        entityManager.AddComponentData(entity, new PredictedState());
        entityManager.AddComponentData(entity, new InterpolatedState());
        return entity;
    }
}

[DisableAutoCreation]
class RocketLauncher_RequestActive : BaseComponentDataSystem<CharBehaviour, AbilityControl,
    Ability_RocketLauncher.PredictedState, Ability_RocketLauncher.Settings>
{
    public RocketLauncher_RequestActive(GameWorld world) : base(world)
    {
        ExtraComponentRequirements = new ComponentType[] { typeof(ServerEntity) };
    }

    protected override void Update(Entity entity, CharBehaviour charAbility, AbilityControl abilityCtrl,
        Ability_RocketLauncher.PredictedState predictedState, Ability_RocketLauncher.Settings settings)
    {
        if (abilityCtrl.behaviorState == AbilityControl.State.Active || abilityCtrl.behaviorState == AbilityControl.State.Cooldown)
            return;
        if (!CharacterBehaviours.IsValidCharacter(EntityManager, charAbility.character))
            return;

        var command = EntityManager.GetComponentData<UserCommandComponentData>(charAbility.character).command;
        abilityCtrl.behaviorState = command.buttons.IsSet(settings.activateButton) ?
            AbilityControl.State.RequestActive : AbilityControl.State.Idle;
        EntityManager.SetComponentData(entity, abilityCtrl);
    }
}

[DisableAutoCreation]
class RocketLauncher_Update : BaseComponentDataSystem<CharBehaviour, AbilityControl,
    Ability_RocketLauncher.PredictedState, Ability_RocketLauncher.Settings>
{
    public RocketLauncher_Update(GameWorld world) : base(world)
    {
        ExtraComponentRequirements = new ComponentType[] { typeof(ServerEntity) };
    }

    protected override void Update(Entity entity, CharBehaviour charAbility, AbilityControl abilityCtrl,
        Ability_RocketLauncher.PredictedState predictedState, Ability_RocketLauncher.Settings settings)
    {
        var time = m_world.worldTime;

        switch (predictedState.phase)
        {
            case Ability_RocketLauncher.Phase.Idle:
                if (abilityCtrl.active == 1)
                {
                    if (!CharacterBehaviours.IsValidCharacter(EntityManager, charAbility.character))
                        return;

                    var charPredictedState = EntityManager.GetComponentData<CharacterPredictedData>(charAbility.character);
                    var character = EntityManager.GetComponentObject<Character>(charAbility.character);

                    abilityCtrl.behaviorState = AbilityControl.State.Active;
                    predictedState.SetPhase(Ability_RocketLauncher.Phase.Active, time.tick);
                    charPredictedState.SetAction(CharacterPredictedData.Action.PrimaryFire, time.tick);

                    var localState = EntityManager.GetComponentData<Ability_RocketLauncher.LocalState>(entity);
                    if (time.tick > localState.lastFireTick)
                    {
                        localState.lastFireTick = time.tick;
                        EntityManager.SetComponentData(entity, localState);

                        var command = EntityManager.GetComponentData<UserCommandComponentData>(charAbility.character).command;
                        var aimDir = (float3)command.lookDir;
                        var eyePos = charPredictedState.position + Vector3.up * character.eyeHeight;

                        FireRocket(entity, charAbility.character, eyePos, aimDir, settings, command.renderTick);
                    }

                    EntityManager.SetComponentData(entity, abilityCtrl);
                    EntityManager.SetComponentData(entity, predictedState);
                    EntityManager.SetComponentData(charAbility.character, charPredictedState);
                }
                break;

            case Ability_RocketLauncher.Phase.Active:
            {
                var phaseDuration = time.DurationSinceTick(predictedState.phaseStartTick);
                if (phaseDuration > settings.activationDuration)
                {
                    abilityCtrl.behaviorState = AbilityControl.State.Cooldown;
                    predictedState.SetPhase(Ability_RocketLauncher.Phase.Cooldown, time.tick);

                    var charAbility2 = EntityManager.GetComponentData<CharBehaviour>(entity);
                    if (CharacterBehaviours.IsValidCharacter(EntityManager, charAbility2.character))
                    {
                        var charPredictedState = EntityManager.GetComponentData<CharacterPredictedData>(charAbility2.character);
                        charPredictedState.SetAction(CharacterPredictedData.Action.None, time.tick);
                        EntityManager.SetComponentData(charAbility2.character, charPredictedState);
                    }

                    EntityManager.SetComponentData(entity, abilityCtrl);
                    EntityManager.SetComponentData(entity, predictedState);
                }
                break;
            }

            case Ability_RocketLauncher.Phase.Cooldown:
            {
                var phaseDuration = time.DurationSinceTick(predictedState.phaseStartTick);
                if (phaseDuration > settings.cooldownDuration)
                {
                    abilityCtrl.behaviorState = AbilityControl.State.Idle;
                    predictedState.SetPhase(Ability_RocketLauncher.Phase.Idle, time.tick);
                    EntityManager.SetComponentData(entity, abilityCtrl);
                    EntityManager.SetComponentData(entity, predictedState);
                }
                break;
            }
        }
    }

    void FireRocket(Entity abilityEntity, Entity characterEntity, float3 eyePos, float3 aimDir, Ability_RocketLauncher.Settings settings, int renderTick)
    {
        const int distance = 500;
        var collisionMask = ~0U;

        var queryReciever = World.GetExistingManager<RaySphereQueryReciever>();
        var queryId = queryReciever.RegisterQuery(new RaySphereQueryReciever.Query()
        {
            origin = eyePos,
            direction = aimDir,
            distance = distance,
            ExcludeOwner = characterEntity,
            hitCollisionTestTick = renderTick,
            radius = settings.hitRadius,
            mask = collisionMask,
        });

        var localState = EntityManager.GetComponentData<Ability_RocketLauncher.LocalState>(abilityEntity);
        localState.rayQueryId = queryId;
        EntityManager.SetComponentData(abilityEntity, localState);

        var interpolatedState = EntityManager.GetComponentData<Ability_RocketLauncher.InterpolatedState>(abilityEntity);
        interpolatedState.fireTick = m_world.worldTime.tick;
        EntityManager.SetComponentData(abilityEntity, interpolatedState);
    }
}

[DisableAutoCreation]
class RocketLauncher_HandleCollisionQuery : BaseComponentDataSystem<Ability_RocketLauncher.LocalState,
    Ability_RocketLauncher.InterpolatedState, Ability_RocketLauncher.Settings>
{
    public RocketLauncher_HandleCollisionQuery(GameWorld world) : base(world)
    {
        ExtraComponentRequirements = new ComponentType[] { typeof(ServerEntity) };
    }

    protected override void Update(Entity abilityEntity, Ability_RocketLauncher.LocalState localState,
        Ability_RocketLauncher.InterpolatedState interpolatedState, Ability_RocketLauncher.Settings settings)
    {
        if (localState.rayQueryId == -1)
            return;

        var queryReciever = World.GetExistingManager<RaySphereQueryReciever>();
        RaySphereQueryReciever.Query query;
        RaySphereQueryReciever.QueryResult queryResult;
        queryReciever.GetResult(localState.rayQueryId, out query, out queryResult);
        localState.rayQueryId = -1;
        EntityManager.SetComponentData(abilityEntity, localState);

        if (queryResult.hit > 0 && queryResult.hitCollisionOwner != Entity.Null)
        {
            var charAbility = EntityManager.GetComponentData<CharBehaviour>(abilityEntity);
            var damageEventBuffer = EntityManager.GetBuffer<DamageEvent>(queryResult.hitCollisionOwner);
            DamageEvent.AddEvent(damageEventBuffer, charAbility.character, settings.damage,
                query.direction, settings.damageImpulse);
        }
    }
}
