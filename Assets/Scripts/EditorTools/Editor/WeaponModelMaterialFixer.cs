using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

public static class WeaponModelMaterialFixer
{
    class WeaponDefinition
    {
        public string Name;
        public string HeroName;
        public string ModelPath;
        public string TexturePath;
        public string[] SubmeshMaterials;
        public Vector3 LocalPosition;
        public Vector3 LocalRotation;
        public Vector3 LocalScale;
        public float TargetWorldLength;
    }

    [MenuItem("FPS Sample/BuildSystem/Fix Weapon Model Materials")]
    public static void FixAll()
    {
        var weapons = new[]
        {
            new WeaponDefinition
            {
                Name = "M4A1", HeroName = "Hero_M4A1", ModelPath = "Assets/Models/Weapons/RealWeapons/M4A1/M4A1.fbx",
                TexturePath = "Assets/Models/Weapons/RealWeapons/M4A1/M4A1Diffuse.png", SubmeshMaterials = new string[0],
                LocalPosition = new Vector3(0.22f, -0.42f, 2.00f), LocalRotation = new Vector3(0f, 0f, 0f),
                LocalScale = Vector3.one, TargetWorldLength = 0.65f
            },
            new WeaponDefinition
            {
                Name = "MP5", HeroName = "Hero_MP5", ModelPath = "Assets/Models/Weapons/RealWeapons/MP5/MP5.obj",
                TexturePath = "",
                SubmeshMaterials = new[] { "Secondary", "Highlight", "Primary" },
                LocalPosition = new Vector3(0.22f, -0.42f, 1.10f), LocalRotation = new Vector3(0f, -90f, 0f),
                LocalScale = new Vector3(0.18f, 0.18f, 0.18f), TargetWorldLength = 0.60f
            },
            new WeaponDefinition
            {
                Name = "AK47", HeroName = "Hero_AK47", ModelPath = "Assets/Models/Weapons/RealWeapons/AK47/AK47.obj",
                TexturePath = "",
                SubmeshMaterials = new[] { "Dark_metal", "Metal", "Wood" },
                LocalPosition = new Vector3(0.22f, -0.42f, 1.15f), LocalRotation = new Vector3(0f, -90f, 0f),
                LocalScale = new Vector3(0.21f, 0.21f, 0.21f), TargetWorldLength = 0.68f
            },
            new WeaponDefinition
            {
                Name = "M700", HeroName = "Hero_M700", ModelPath = "Assets/Models/Weapons/RealWeapons/M700/M700.obj",
                TexturePath = "",
                SubmeshMaterials = new[] { "Black", "Green", "DarkMetal", "Glass", "Grey" },
                LocalPosition = new Vector3(0.22f, -0.42f, 1.25f), LocalRotation = new Vector3(0f, -90f, 0f),
                LocalScale = new Vector3(0.39f, 0.39f, 0.39f), TargetWorldLength = 0.85f
            },
        };

        foreach (var weapon in weapons)
        {
            var directory = $"Assets/Models/Weapons/RealWeapons/{weapon.Name}";
            var wrapperPath = $"{directory}/{weapon.Name}_Weapon.prefab";
            var source = AssetDatabase.LoadAssetAtPath<GameObject>(weapon.ModelPath);
            if (source == null)
            {
                Debug.LogError($"Missing weapon model: {weapon.ModelPath}");
                continue;
            }

            var instance = Object.Instantiate(source);
            instance.name = weapon.Name;
            var renderers = instance.GetComponentsInChildren<Renderer>();
            foreach (var renderer in renderers)
            {
                var materials = new Material[renderer.sharedMaterials.Length];
                for (var index = 0; index < renderer.sharedMaterials.Length; index++)
                {
                    var sourceMaterial = renderer.sharedMaterials[index];
                    var materialName = index < weapon.SubmeshMaterials.Length
                        ? weapon.SubmeshMaterials[index]
                        : sourceMaterial != null ? sourceMaterial.name : $"{weapon.Name}_Body";
                    var materialPath = $"{directory}/{weapon.Name}_{materialName}.mat";
                    AssetDatabase.DeleteAsset(materialPath);
                    var material = CreateMaterial(weapon, materialName, materialPath);
                    materials[index] = material;
                }
                renderer.sharedMaterials = materials;
            }

            var wrapper = PrefabUtility.SaveAsPrefabAsset(instance, wrapperPath);
            Object.DestroyImmediate(instance);
            AssetDatabase.SaveAssets();

            UpdateWeaponPrefab(weapon, wrapperPath);
            Debug.Log($"Weapon material wrapper created: {wrapperPath}");
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
    }

    static Material CreateMaterial(WeaponDefinition weapon, string materialName, string materialPath)
    {
        var shaderTemplate = AssetDatabase.LoadAssetAtPath<Material>(
            "Assets/Materials/Weapons/Terraformer_Weapon_A_1P/Terraformer_Weapon_A_FirstPerson.mat");
        var material = shaderTemplate != null
            ? new Material(shaderTemplate)
            : new Material(Shader.Find("Standard"));

        foreach (var textureProperty in new[] { "_MaskMap", "_NormalMap", "_NormalMapOS", "_BentNormalMap", "_BentNormalMapOS", "_DetailMap", "_EmissiveColorMap" })
        {
            if (material.HasProperty(textureProperty))
                material.SetTexture(textureProperty, null);
        }

        material.DisableKeyword("_NORMALMAP");
        material.DisableKeyword("_NORMALMAP_TANGENT_SPACE");
        material.DisableKeyword("_MASKMAP");

        if (!string.IsNullOrEmpty(weapon.TexturePath))
        {
            var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(weapon.TexturePath);
            if (texture != null)
                material.SetTexture("_BaseColorMap", texture);
            if (texture != null && material.HasProperty("_MainTex"))
                material.SetTexture("_MainTex", texture);
            if (material.HasProperty("_BaseColor"))
                material.SetColor("_BaseColor", Color.white);
            if (material.HasProperty("_Color"))
                material.SetColor("_Color", Color.white);
            material.SetFloat("_Metallic", 0.72f);
            material.SetFloat("_Smoothness", 0.42f);
        }
        else
        {
            ApplyTexture(material, $"{weapon.Name}_{materialName}", materialPath);
            if (materialName.Contains("Wood"))
            {
                material.SetFloat("_Metallic", 0.08f);
                material.SetFloat("_Smoothness", 0.30f);
            }
            else if (materialName.Contains("Glass"))
            {
                material.SetFloat("_Metallic", 0.10f);
                material.SetFloat("_Smoothness", 0.82f);
            }
            else if (materialName.Contains("Highlight"))
            {
                material.SetFloat("_Metallic", 0.82f);
                material.SetFloat("_Smoothness", 0.52f);
            }
            else if (materialName.ToLowerInvariant().Contains("metal"))
            {
                material.SetFloat("_Metallic", 0.78f);
                material.SetFloat("_Smoothness", 0.46f);
            }
            else if (materialName.Contains("Secondary"))
            {
                material.SetFloat("_Metallic", 0.28f);
                material.SetFloat("_Smoothness", 0.36f);
            }
            else if (materialName.Contains("Grey"))
            {
                material.SetFloat("_Metallic", 0.70f);
                material.SetFloat("_Smoothness", 0.48f);
            }
            else
            {
                material.SetFloat("_Metallic", 0.22f);
                material.SetFloat("_Smoothness", 0.34f);
            }

            if (material.HasProperty("_BaseColor") && !AssetDatabase.LoadAssetAtPath<Texture2D>(
                $"{Path.GetDirectoryName(materialPath).Replace('\\', '/')}/{weapon.Name}_{materialName}.png"))
            {
                material.SetColor("_BaseColor", new Color(0.11f, 0.12f, 0.13f));
            }
        }

        AssetDatabase.CreateAsset(material, materialPath);
        return material;
    }

    static void ApplyTexture(Material material, string textureName, string materialPath)
    {
        var directory = Path.GetDirectoryName(materialPath).Replace('\\', '/');
        var texture = AssetDatabase.LoadAssetAtPath<Texture2D>($"{directory}/{textureName}.png");
        if (texture == null && textureName.Contains("Primary"))
        {
            var black = $"{directory}/{Path.GetFileNameWithoutExtension(materialPath).Split('_')[0]}_Black.png";
            texture = AssetDatabase.LoadAssetAtPath<Texture2D>(black);
        }
        if (texture != null)
        {
            material.SetTexture("_BaseColorMap", texture);
            if (material.HasProperty("_MainTex"))
                material.SetTexture("_MainTex", texture);
        }
    }

    static void UpdateWeaponPrefab(WeaponDefinition weapon, string wrapperPath)
    {
        var prefabPaths = new[]
        {
            "Assets/Prefabs/Weapons/TerraformerWeapon/Item_TerraformerWeapon_1P.prefab",
            "Assets/Prefabs/Weapons/TerraformerWeapon/Item_TerraformerWeapon_Client.prefab",
        };

        foreach (var prefabPath in prefabPaths)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            foreach (var component in prefab.GetComponentsInChildren<WeaponModelOverride>(true))
            {
                if (component.heroName == weapon.HeroName)
                {
                    component.modelPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(wrapperPath);
                    component.localPosition = weapon.LocalPosition;
                    component.localRotation = weapon.LocalRotation;
                    component.localScale = weapon.LocalScale;
                    component.targetWorldLength = weapon.TargetWorldLength;
                    component.materialOverrides = BuildMaterialOverrides(weapon);
                    EditorUtility.SetDirty(component);
                }
            }
            EditorUtility.SetDirty(prefab);
        }
    }

    static WeaponModelOverride.MaterialOverride[] BuildMaterialOverrides(WeaponDefinition weapon)
    {
        var materialNames = weapon.SubmeshMaterials.Length > 0
            ? weapon.SubmeshMaterials
            : new[] { "DefaultHDMaterial" };
        var rendererCount = weapon.Name == "M4A1" ? 2 : 1;
        var overrides = new List<WeaponModelOverride.MaterialOverride>();

        for (var rendererIndex = 0; rendererIndex < rendererCount; rendererIndex++)
        {
            for (var materialIndex = 0; materialIndex < materialNames.Length; materialIndex++)
            {
                var materialPath = $"Assets/Models/Weapons/RealWeapons/{weapon.Name}/{weapon.Name}_{materialNames[materialIndex]}.mat";
                overrides.Add(new WeaponModelOverride.MaterialOverride
                {
                    sourceName = materialNames[materialIndex],
                    material = AssetDatabase.LoadAssetAtPath<Material>(materialPath),
                    rendererIndex = rendererIndex,
                    materialIndex = materialIndex
                });
            }
        }

        return overrides.ToArray();
    }
}
