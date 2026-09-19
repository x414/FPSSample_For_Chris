using System.Collections;
using System.Collections.Generic;
using Unity.Entities;
using UnityEngine;
using UnityEngine.UI;

public class RocketLauncherUI : AbilityUI
{
    public RawImage activeIcon;
    public TMPro.TextMeshProUGUI cooldownText;
    public GameObject disabledOverlay;

    public override void UpdateAbilityUI(EntityManager entityManager, ref GameTime time)
    {
        var charRepAll = entityManager.GetComponentData<CharacterReplicatedData>(abilityOwner);
        var ability = charRepAll.FindAbilityWithComponent(entityManager,typeof(Ability_ProjectileLauncher.PredictedState));
        if (ability == Entity.Null)
        {
            activeIcon.enabled = false;
            cooldownText.gameObject.SetActive(false);
            disabledOverlay.SetActive(false);
            return;
        }

        var behaviorCtrl = entityManager.GetComponentData<AbilityControl>(ability);
        var predictedState = entityManager.GetComponentData<Ability_ProjectileLauncher.PredictedState>(ability);
        var settings = entityManager.GetComponentData<Ability_ProjectileLauncher.Settings>(ability);

        activeIcon.enabled = behaviorCtrl.behaviorState == AbilityControl.State.Active;

        bool showCooldown = behaviorCtrl.behaviorState == AbilityControl.State.Cooldown;
        cooldownText.gameObject.SetActive(showCooldown);
        disabledOverlay.SetActive(showCooldown);
        if (showCooldown)
        {
            float cooldownLeft = settings.cooldownDuration - time.DurationSinceTick(predictedState.activeTick);
            cooldownText.text = string.Format("{0:F1}", cooldownLeft);
        }
    }
}


