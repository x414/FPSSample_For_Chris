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

        var shouldShowModel = presentation.IsVisible;
        if (heroName == "Hero_M700" &&
            (AutomaticRifleUI.IsM700Scoped || AutomaticRifleUI.selfTestScopeForced ||
             Ability_AutoRifle.IsAiming(objectEntity.EntityManager, presentation.character)))
            shouldShowModel = false;

        UpdateHandsVisibility(presentation, character.heroTypeData.name);

        if (character.heroTypeData.name != heroName)
            return;

        if (modelInstance != null)
        {
            if (modelInstance.activeSelf != shouldShowModel)
            {
                Debug.Log($"Weapon override visibility change: hero={heroName} first={presentation.isFirstPerson} visible={presentation.IsVisible} from={modelInstance.activeSelf} to={shouldShowModel} pos={modelInstance.transform.position} rot={modelInstance.transform.eulerAngles}");
                modelInstance.SetActive(shouldShowModel);
            }
            if (Time.frameCount % 60 == 0)
            {
                var modelRenderers = modelInstance.GetComponentsInChildren<Renderer>(true);
                Debug.Log($"Weapon override status: hero={heroName} first={presentation.isFirstPerson} visible={presentation.IsVisible} instanceActive={modelInstance.activeSelf} renderers={string.Join(",", modelRenderers.Select(renderer => renderer.enabled.ToString()))} worldPos={modelInstance.transform.position} parentActive={transform.gameObject.activeInHierarchy}");
            }
            return;
        }

        Physics.SyncTransforms();
        var sourceRenderers = GetComponentsInChildren<SkinnedMeshRenderer>(true);
        Bounds sourceBounds = default;
        var hasSourceBounds = TryGetCombinedBounds(sourceRenderers, out sourceBounds);
        foreach (var renderer in sourceRenderers)
            renderer.enabled = false;

        modelInstance = Instantiate(modelPrefab, transform, false);
        var desiredLocalPosition = GetModelLocalPosition(presentation);
        modelInstance.transform.localPosition = desiredLocalPosition;
        modelInstance.transform.localEulerAngles = localRotation;
        modelInstance.transform.localScale = localScale;
        if (!presentation.isFirstPerson)
        {
            modelInstance.transform.localPosition = Vector3.zero;
            modelInstance.transform.localRotation = Quaternion.identity;
            modelInstance.transform.localScale = Vector3.one;
        }
        modelInstance.SetActive(shouldShowModel);
        if (presentation.isFirstPerson)
            NormalizeModel(desiredLocalPosition);
        else
            NormalizeThirdPersonModel(sourceBounds, hasSourceBounds);
        ApplyMaterialOverrides();
        GameDebug.Log($"Weapon model override active: {heroName} -> {modelPrefab.name}");
        Debug.Log($"Weapon override debug: hero={heroName} first={presentation.isFirstPerson} visible={presentation.IsVisible} active={modelInstance.activeSelf} sourceCount={sourceRenderers.Length} source={hasSourceBounds} sourceCenter={(hasSourceBounds ? sourceBounds.center.ToString() : "n/a")} sourceExtents={(hasSourceBounds ? sourceBounds.extents.ToString() : "n/a")} parent={transform.parent?.name}");
    }

    Vector3 GetModelLocalPosition(CharacterPresentationSetup presentation)
    {
        if (presentation.isFirstPerson)
            return localPosition;

        return new Vector3(localPosition.x * 0.25f, Mathf.Abs(localPosition.y) * 0.25f, localPosition.z * 0.25f);
    }

    void NormalizeThirdPersonModel(Bounds sourceBounds, bool hasSourceBounds)
    {
        var renderers = modelInstance.GetComponentsInChildren<Renderer>(true)
            .Where(renderer => renderer is MeshRenderer || renderer is SkinnedMeshRenderer)
            .ToArray();
        if (renderers.Length == 0)
            return;

        Physics.SyncTransforms();
        Bounds modelBounds;
        if (!TryGetCombinedBounds(renderers, out modelBounds))
            return;
        var sourceRenderer = GetComponentsInChildren<SkinnedMeshRenderer>(true).FirstOrDefault(renderer => renderer != null && renderer.sharedMesh != null);
        var modelMeshRenderer = renderers.FirstOrDefault(renderer => renderer != null && renderer.GetComponent<MeshFilter>() != null && renderer.GetComponent<MeshFilter>().sharedMesh != null);
        if (sourceRenderer != null)
            Debug.Log($"3P axes source localExtents={sourceRenderer.sharedMesh.bounds.extents} localToWorld={sourceRenderer.transform.localToWorldMatrix}");
        if (modelMeshRenderer != null)
            Debug.Log($"3P axes model localExtents={modelMeshRenderer.GetComponent<MeshFilter>().sharedMesh.bounds.extents}");

        Physics.SyncTransforms();
        if (TryGetCombinedBounds(renderers, out modelBounds))
        {
            var modelLongDirection = GetLongestModelAxisDirection(modelBounds, modelInstance.transform);
            var forwardLength = GetBoundsLengthAlongDirection(modelBounds, modelLongDirection);
            var desiredLength = targetWorldLength * 1.8f;
            if (forwardLength > 0.0001f && desiredLength > 0.0001f)
                modelInstance.transform.localScale *= desiredLength / forwardLength;
        }

        Physics.SyncTransforms();
        TryGetCombinedBounds(renderers, out modelBounds);
        Debug.Log($"3P normalize: parent={modelInstance.transform.parent?.name} localPos={modelInstance.transform.localPosition} worldPos={modelInstance.transform.position} finalCenter={modelBounds.center} finalExtents={modelBounds.extents}");
    }

    static bool TryGetCombinedBounds(Renderer[] renderers, out Bounds bounds)
    {
        var validRenderers = renderers.Where(renderer => renderer != null && renderer.bounds.extents.magnitude > 0.0001f)
            .ToArray();
        if (validRenderers.Length == 0)
        {
            bounds = default;
            return false;
        }

        bounds = GetRendererBounds(validRenderers[0]);
        for (var index = 1; index < validRenderers.Length; index++)
            bounds.Encapsulate(GetRendererBounds(validRenderers[index]));
        return true;
    }

    static Bounds GetRendererBounds(Renderer renderer)
    {
        var meshFilter = renderer.GetComponent<MeshFilter>();
        if (meshFilter == null || meshFilter.sharedMesh == null)
            return renderer.bounds;

        return TransformBounds(renderer.localToWorldMatrix, meshFilter.sharedMesh.bounds);
    }

    static Bounds TransformBounds(Matrix4x4 matrix, Bounds bounds)
    {
        var center = matrix.MultiplyPoint(bounds.center);
        var extents = Vector3.zero;
        for (var x = -1; x <= 1; x += 2)
        {
            for (var y = -1; y <= 1; y += 2)
            {
                for (var z = -1; z <= 1; z += 2)
                {
                    var corner = bounds.center + Vector3.Scale(bounds.extents, new Vector3(x, y, z));
                    var transformedCorner = matrix.MultiplyPoint(corner);
                    extents = Vector3.Max(extents, new Vector3(
                        Mathf.Abs(transformedCorner.x - center.x),
                        Mathf.Abs(transformedCorner.y - center.y),
                        Mathf.Abs(transformedCorner.z - center.z)));
                }
            }
        }

        return new Bounds(center, extents * 2f);
    }

    void NormalizeModel(Vector3 desiredLocalPosition)
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
        var desiredCenter = parent.TransformPoint(desiredLocalPosition);
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

    static Vector3 GetWorldBoundsLongAxisDirection(Bounds bounds)
    {
        if (bounds.extents.x >= bounds.extents.y && bounds.extents.x >= bounds.extents.z)
            return Vector3.right;
        return bounds.extents.y >= bounds.extents.z ? Vector3.up : Vector3.forward;
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

        if (presentation.isFirstPerson)
        {
            foreach (var renderer in characterPresentation.geomtry.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                renderer.enabled = !shouldBeHidden;
        }

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
