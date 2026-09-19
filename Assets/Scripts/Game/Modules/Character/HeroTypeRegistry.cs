using System.Collections.Generic;
using System.Linq;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

[CreateAssetMenu(menuName = "FPS Sample/Hero/HeroTypeRegistry", fileName = "HeroTypeRegistry")]
public class HeroTypeRegistry : RegistryBase
{
    public List<HeroTypeAsset> entries = new List<HeroTypeAsset>();
    
#if UNITY_EDITOR
    
    public override void PrepareForBuild()
    {
        Debug.Log("HeroTypeRegistry"); 

        entries.Clear();
        var guids = AssetDatabase.FindAssets("t:HeroTypeAsset");
        var definitions = new List<HeroTypeAsset>();
        foreach (var guid in guids)
        {
            var path = AssetDatabase.GUIDToAssetPath(guid);
            var definition = AssetDatabase.LoadAssetAtPath<HeroTypeAsset>(path);
            Debug.Log("   Adding definition:" + definition);
            definitions.Add(definition);
        }

        entries.AddRange(definitions.OrderByDescending(entry => entry != null && (entry.name == "Hero_Terraformer" || entry.name == "Hero_Robot"))
            .ThenBy(entry => entry == null ? string.Empty : entry.name));
        
        EditorUtility.SetDirty(this);
    }
    
    public override void GetSingleAssetGUIDs(List<string> guids, bool serverBuild)
    {
        foreach (var setup in entries)
        {
            foreach(var item in setup.items)
            {
                if (serverBuild && item.itemType.prefabServer.IsSet())
                    guids.Add(item.itemType.prefabServer.GetGuidStr());
                if (!serverBuild && item.itemType.prefabClient.IsSet())
                    guids.Add(item.itemType.prefabClient.GetGuidStr());
                if (!serverBuild && item.itemType.prefab1P.IsSet())
                    guids.Add(item.itemType.prefab1P.GetGuidStr());
            }
        }
    }
#endif
}

