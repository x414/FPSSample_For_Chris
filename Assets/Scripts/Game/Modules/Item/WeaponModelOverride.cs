using System;
using System.Linq;
using Unity.Entities;
using UnityEngine;

public class WeaponModelOverride : MonoBehaviour
{
    public string heroName;
    public GameObject modelPrefab;
    public Vector3 localPosition;
    public Vector3 localRotation;
    public Vector3 localScale = Vector3.one;
    public float targetWorldLength;
    public bool hideHands = true;
    public MaterialOverride[] materialOverrides;

    GameObject modelInstance;
    void Awake()
    {
        hideHands = true;
    }

    void LateUpdate()
    {
        if (string.IsNullOrEmpty(heroName) || modelPrefab == null)
            return;

        var presentation = GetComponent<CharacterPresentationSetup>();
        var objectEntity = GetComponent<GameObjectEntity>();
        if (presentation == null || objectEntity == null || presentation.character == Entity.Null ||
            !objectEntity.EntityManager.Exists(presentation.character) ||
            !objectEntity.EntityManager.HasComponent<Character>(presentation.character))
        {
            return;
        }

        var character = objectEntity.EntityManager.GetComponentObject<Character>(presentation.character);
        if (character == null || character.heroTypeData == null)
            return;

        UpdateHandsVisibility(presentation, character.heroTypeData.name);

        if (character.heroTypeData.name != heroName)
            return;

        if (modelInstance != null)
        {
            return;
        }

        Physics.SyncTransforms();
        foreach (var renderer in GetComponentsInChildren<SkinnedMeshRenderer>(true))
            renderer.enabled = false;

        modelInstance = Instantiate(modelPrefab, transform, false);
        modelInstance.transform.localPosition = localPosition;
        modelInstance.transform.localEulerAngles = localRotation;
        modelInstance.transform.localScale = localScale;
        NormalizeModel();
        ApplyMaterialOverrides();
        GameDebug.Log($"Weapon model override active: {heroName} -> {modelPrefab.name}");
    }

    void NormalizeModel()
    {
        var renderers = modelInstance.GetComponentsInChildren<Renderer>(true);
        if (renderers.Length == 0)
            return;

        Physics.SyncTransforms();
        var bounds = renderers[0].bounds;
        for (var index = 1; index < renderers.Length; index++)
            bounds.Encapsulate(renderers[index].bounds);

        var lengthDirection = GetLongestModelAxisDirection(bounds, modelInstance.transform);
        var forwardLength = GetBoundsLengthAlongDirection(bounds, lengthDirection);
        if (targetWorldLength > 0f && forwardLength > 0.0001f)
            modelInstance.transform.localScale *= targetWorldLength / forwardLength;

        Physics.SyncTransforms();
        bounds = renderers[0].bounds;
        for (var index = 1; index < renderers.Length; index++)
            bounds.Encapsulate(renderers[index].bounds);

        var parent = modelInstance.transform.parent != null ? modelInstance.transform.parent : modelInstance.transform;
        var desiredCenter = parent.TransformPoint(localPosition);
        modelInstance.transform.localPosition += parent.InverseTransformVector(desiredCenter - bounds.center);
    }

    static float GetBoundsLengthAlongDirection(Bounds bounds, Vector3 direction)
    {
        direction.Normalize();
        var minimum = float.MaxValue;
        var maximum = float.MinValue;
        for (var x = -1; x <= 1; x += 2)
        {
            for (var y = -1; y <= 1; y += 2)
            {
                for (var z = -1; z <= 1; z += 2)
                {
                    var corner = bounds.center + Vector3.Scale(bounds.extents, new Vector3(x, y, z));
                    var projection = Vector3.Dot(corner, direction);
                    minimum = Mathf.Min(minimum, projection);
                    maximum = Mathf.Max(maximum, projection);
                }
            }
        }

        return maximum - minimum;
    }

    static Vector3 GetLongestModelAxisDirection(Bounds bounds, Transform modelTransform)
    {
        var directions = new[] { modelTransform.right, modelTransform.up, modelTransform.forward };
        var longestDirection = directions[0];
        var longestLength = GetBoundsLengthAlongDirection(bounds, longestDirection);
        for (var index = 1; index < directions.Length; index++)
        {
            var length = GetBoundsLengthAlongDirection(bounds, directions[index]);
            if (length > longestLength)
            {
                longestLength = length;
                longestDirection = directions[index];
            }
        }

        return longestDirection;
    }

    void UpdateHandsVisibility(CharacterPresentationSetup presentation, string currentHeroName)
    {
        var objectEntity = GetComponent<GameObjectEntity>();
        if (presentation == null || objectEntity == null || presentation.attachToPresentation == Entity.Null ||
            !objectEntity.EntityManager.Exists(presentation.attachToPresentation) ||
            !objectEntity.EntityManager.HasComponent<CharacterPresentationSetup>(presentation.attachToPresentation))
            return;

        var characterPresentation = objectEntity.EntityManager.GetComponentObject<CharacterPresentationSetup>(
            presentation.attachToPresentation);
        if (characterPresentation == null || characterPresentation.geomtry == null)
            return;

        var shouldBeHidden = GetComponents<WeaponModelOverride>().Any(overrideItem =>
            overrideItem != null && overrideItem.hideHands && overrideItem.heroName == currentHeroName);

        foreach (var renderer in characterPresentation.geomtry.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            renderer.enabled = !shouldBeHidden;

        foreach (var candidate in characterPresentation.geomtry.GetComponentsInChildren<Transform>(true))
        {
            var isHandBone = candidate.name.StartsWith("Left_Hand") || candidate.name.StartsWith("Right_Hand");
            if (isHandBone && candidate.name != "Right_Hand_Attach")
                candidate.localScale = shouldBeHidden ? Vector3.one * 0.0001f : Vector3.one;
        }

    }

    void ApplyMaterialOverrides()
    {
        if (materialOverrides == null || materialOverrides.Length == 0)
            return;

        foreach (var renderer in modelInstance.GetComponentsInChildren<Renderer>(true))
        {
            var materials = renderer.sharedMaterials.Select(source =>
            {
                if (source == null)
                    return source;

                var exact = materialOverrides.FirstOrDefault(overrideItem =>
                    overrideItem != null && overrideItem.material != null && overrideItem.sourceName == source.name);
                if (exact != null)
                    return exact.material;

                var wildcard = materialOverrides.FirstOrDefault(overrideItem =>
                    overrideItem != null && overrideItem.material != null && overrideItem.sourceName == "*");
                return wildcard != null ? wildcard.material : source;
            }).ToArray();

            var rendererIndex = Array.IndexOf(modelInstance.GetComponentsInChildren<Renderer>(true), renderer);
            for (var materialIndex = 0; materialIndex < materials.Length; materialIndex++)
            {
                var indexed = materialOverrides.FirstOrDefault(overrideItem =>
                    overrideItem != null && overrideItem.material != null &&
                    overrideItem.rendererIndex == rendererIndex && overrideItem.materialIndex == materialIndex);
                if (indexed != null)
                    materials[materialIndex] = indexed.material;
            }

            renderer.sharedMaterials = materials;
        }
    }

    [Serializable]
    public class MaterialOverride
    {
        public string sourceName;
        public Material material;
        public int rendererIndex = -1;
        public int materialIndex = -1;
    }
}
