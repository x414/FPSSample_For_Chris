using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

public class RobotFadeOut : MonoBehaviour
{
    readonly List<Renderer> m_Renderers = new List<Renderer>();
    readonly List<Material> m_Materials = new List<Material>();
    readonly List<Transform> m_ScaleTargets = new List<Transform>();
    readonly List<Vector3> m_OriginalScales = new List<Vector3>();

    public void Begin(float duration, List<CharacterPresentationSetup> presentations)
    {
        if (m_Materials.Count > 0)
            return;

        CollectRenderers(presentations);
        if (m_Renderers.Count == 0)
            return;

        PrepareMaterials();
        CollectScaleTargets();
        GameDebug.Log($"Robot fade started:{gameObject.name} renderers:{m_Renderers.Count} materials:{m_Materials.Count}");
        StartCoroutine(FadeRoutine(duration));
    }

    void OnDestroy()
    {
        StopAllCoroutines();

        foreach (var material in m_Materials)
        {
            if (material != null)
                Destroy(material);
        }

        m_Materials.Clear();
        m_Renderers.Clear();
        m_ScaleTargets.Clear();
        m_OriginalScales.Clear();
    }

    void CollectRenderers(List<CharacterPresentationSetup> presentations)
    {
        m_Renderers.AddRange(GetComponentsInChildren<Renderer>(true));

        if (presentations != null)
        {
            foreach (var presentation in presentations)
            {
                if (presentation == null)
                    continue;

                AddTarget(presentation.gameObject);
            }
        }

        foreach (var ragdollOwner in GetComponentsInChildren<RagdollOwner>(true))
        {
            if (ragdollOwner.ragdollInstance != null)
                AddTarget(ragdollOwner.ragdollInstance);
        }
    }

    void AddTarget(GameObject target)
    {
        if (target == null)
            return;

        m_Renderers.AddRange(target.GetComponentsInChildren<Renderer>(true));
        AddScaleTarget(target.transform);

        foreach (var ragdollOwner in target.GetComponentsInChildren<RagdollOwner>(true))
        {
            if (ragdollOwner.ragdollInstance != null &&
                !ragdollOwner.ragdollInstance.transform.IsChildOf(target.transform))
                AddTarget(ragdollOwner.ragdollInstance);
        }
    }

    void CollectScaleTargets()
    {
        AddScaleTarget(transform);

    }

    void AddScaleTarget(Transform target)
    {
        if (target == null || m_ScaleTargets.Contains(target))
            return;

        m_ScaleTargets.Add(target);
        m_OriginalScales.Add(target.localScale);
    }

    void PrepareMaterials()
    {
        var materialClones = new Dictionary<Material, Material>();

        foreach (var renderer in m_Renderers)
        {
            var materials = renderer.materials;
            for (var index = 0; index < materials.Length; index++)
            {
                var originalMaterial = materials[index];
                if (originalMaterial == null)
                    continue;

                if (!materialClones.TryGetValue(originalMaterial, out var fadeMaterial))
                {
                    fadeMaterial = new Material(originalMaterial)
                    {
                        name = originalMaterial.name + "_FadeOut"
                    };
                    MakeTransparent(fadeMaterial);
                    materialClones.Add(originalMaterial, fadeMaterial);
                    m_Materials.Add(fadeMaterial);
                }

                materials[index] = fadeMaterial;
            }

            renderer.materials = materials;
        }
    }

    static void MakeTransparent(Material material)
    {
        if (!material.HasProperty("_SurfaceType"))
            return;

        material.SetFloat("_SurfaceType", 1f);
        material.SetFloat("_BlendMode", 0f);
        material.SetInt("_SrcBlend", (int)BlendMode.One);
        material.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
        material.SetInt("_ZWrite", 0);
        material.SetInt("_ZTestDepthEqualForOpaque", (int)CompareFunction.LessEqual);
        material.SetInt("_ZTestGBuffer", (int)CompareFunction.LessEqual);
        material.SetFloat("_TransparentDepthPrepassEnable", 0f);
        material.SetFloat("_TransparentDepthPostpassEnable", 0f);
        material.SetFloat("_TransparentBackfaceEnable", 0f);
        material.SetFloat("_EnableFogOnTransparent", 1f);
        material.SetOverrideTag("RenderType", "Transparent");
        material.DisableKeyword("_ALPHATEST_ON");
        material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        material.EnableKeyword("_BLENDMODE_ALPHA");
        material.EnableKeyword("_BLENDMODE_PRESERVE_SPECULAR_LIGHTING");
        material.DisableKeyword("_BLENDMODE_PRE_MULTIPLY");
        material.DisableKeyword("_BLENDMODE_ADD");
        material.renderQueue = (int)RenderQueue.Transparent;
        material.SetShaderPassEnabled("GBuffer", false);
        material.SetShaderPassEnabled("DepthOnly", false);
        material.SetShaderPassEnabled("MotionVectors", false);
    }

    static Color GetColor(Material material)
    {
        if (material.HasProperty("_BaseColor"))
            return material.GetColor("_BaseColor");
        if (material.HasProperty("_Color"))
            return material.GetColor("_Color");
        return Color.white;
    }

    static void SetColor(Material material, Color color)
    {
        if (material.HasProperty("_BaseColor"))
            material.SetColor("_BaseColor", color);
        if (material.HasProperty("_Color"))
            material.SetColor("_Color", color);
    }

    IEnumerator FadeRoutine(float duration)
    {
        var colors = new Color[m_Materials.Count];
        for (var index = 0; index < m_Materials.Count; index++)
            colors[index] = GetColor(m_Materials[index]);

        for (var elapsed = 0f; elapsed < duration; elapsed += Time.deltaTime)
        {
            var progress = Mathf.Clamp01(elapsed / duration);
            var visibleScale = 1f - Mathf.SmoothStep(0f, 1f, progress);
            var alpha = visibleScale;

            for (var index = 0; index < m_Materials.Count; index++)
            {
                colors[index].a = alpha;
                SetColor(m_Materials[index], colors[index]);
            }

            for (var index = 0; index < m_ScaleTargets.Count; index++)
                m_ScaleTargets[index].localScale = m_OriginalScales[index] * visibleScale;

            yield return null;
        }

        for (var index = 0; index < m_Materials.Count; index++)
        {
            colors[index].a = 0f;
            SetColor(m_Materials[index], colors[index]);
        }

        for (var index = 0; index < m_ScaleTargets.Count; index++)
            m_ScaleTargets[index].localScale = Vector3.zero;
    }
}
