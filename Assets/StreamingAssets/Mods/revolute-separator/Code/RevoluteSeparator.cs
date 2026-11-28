using System;
using System.Collections.Generic;
using Bpsim.Modding;
using Bpsim.Mods;
using Bpsim.Parts;
using Bpsim.Rendering;
using UnityEngine;

namespace RevoluteSeparator
{
    public sealed class Entry : ModEntryPoint
    {
        private const string SkinAtlasFile = "Atlas/revolute_skins.txt";
        private const string IconAtlasFile = "Atlas/revolute_icons.txt";

        private GameObject m_host;

        public override void OnLoad()
        {
            m_host = new GameObject("RevoluteSeparator.Bootstrapper");
            UnityEngine.Object.DontDestroyOnLoad(m_host);
            Bootstrapper bootstrapper = m_host.AddComponent<Bootstrapper>();
            bootstrapper.Bind(this);
        }

        public override void OnUnload()
        {
            if (m_host != null)
            {
                UnityEngine.Object.Destroy(m_host);
                m_host = null;
            }
        }

        public override bool IsRequiredForMachine(string machineJson)
        {
            return !string.IsNullOrEmpty(machineJson) && machineJson.Contains("9000");
        }

        internal bool RegisterSprites()
        {
            Texture2D texture = ModAssets.LoadTexture(ModDirectory, "Atlas/revolute_skins.png");
            if (texture == null)
            {
                Debug.LogWarning("[revolute-separator] unable to load Atlas/revolute_skins.png");
                return false;
            }
            texture.wrapMode = TextureWrapMode.Clamp;
            texture.filterMode = FilterMode.Bilinear;
            return LoadAtlas(texture, SkinAtlasFile) && LoadAtlas(texture, IconAtlasFile);
        }

        private bool LoadAtlas(Texture2D texture, string relativePath)
        {
            string source = ModAssets.ReadText(ModDirectory, relativePath);
            if (string.IsNullOrEmpty(source))
            {
                Debug.LogWarning("[revolute-separator] unable to load " + relativePath);
                return false;
            }
            SpriteMap map;
            try
            {
                map = SpriteMap.Read(source);
            }
            catch (Exception exception)
            {
                Debug.LogWarning("[revolute-separator] invalid " + relativePath + ": " + exception.Message);
                return false;
            }
            SpriteMap.MapHeader header = map.Header;
            if (!ModSprites.RegisterAtlas(header.Name, texture, header.Width, header.Height, header.Scale, out string atlasError))
            {
                if (SpriteManager.Instance == null || !SpriteManager.Instance.HasAtlas(header.Name))
                {
                    Debug.LogWarning("[revolute-separator] atlas registration failed: " + atlasError);
                    return false;
                }
            }
            foreach (KeyValuePair<string, SpriteMap.MapEntry> item in map.Data)
            {
                if (!ModSprites.RegisterSprite(item.Key, header.Name, item.Value, out string spriteError)
                    && !string.IsNullOrEmpty(spriteError)
                    && spriteError.IndexOf("已存在", StringComparison.Ordinal) < 0)
                {
                    Debug.LogWarning("[revolute-separator] sprite registration failed: " + spriteError);
                    return false;
                }
            }
            return true;
        }

        internal bool RegisterVariant()
        {
            const PartType type = PartType.Kicker;
            const int baseIndex = 0;
            const int variantIndex = 9000;
            PartFactory factory = PartManager.Instance != null ? PartManager.Instance.Factory : null;
            Dictionary<PartTypeInfo, PartExtensionData> extensions = factory != null ? factory.PartExtensionMap : null;
            PartTypeInfo baseInfo = new PartTypeInfo(type, baseIndex);
            PartTypeInfo variantInfo = new PartTypeInfo(type, variantIndex);
            bool hasBaseExtension = extensions != null && extensions.TryGetValue(baseInfo, out PartExtensionData extension);
            if (extensions != null && hasBaseExtension)
            {
                extension.IsConnectionSource = false;
                extension.HasCustomConnection = false;
                extension.ConnectionDirection = Bpsim.BitDirection.None;
                extensions[variantInfo] = extension;
            }
            if (!ModParts.TryAddSkinVariant(type, baseIndex, variantIndex, "三角铰链分离器",
                "revolute/TriangularSeparator", null, null, out string error))
            {
                if (extensions != null) extensions.Remove(variantInfo);
                Debug.LogWarning("[revolute-separator] variant registration failed: " + error);
                return false;
            }
            Debug.Log("[revolute-separator] registered triangular separator variant #9000");
            return true;
        }
    }

    internal sealed class Bootstrapper : MonoBehaviour
    {
        private Entry m_entry;
        private float m_nextRetry;

        internal void Bind(Entry entry)
        {
            m_entry = entry;
            m_nextRetry = Time.unscaledTime + 0.5f;
        }

        private void Update()
        {
            if (m_entry == null || Time.unscaledTime < m_nextRetry) return;
            m_nextRetry = Time.unscaledTime + 0.5f;
            PartManager manager = PartManager.Instance;
            if (manager == null || !manager.IsReady || manager.Factory == null || manager.Factory.PartCollection == null) return;
            if (!m_entry.RegisterSprites() || !m_entry.RegisterVariant()) return;
            Destroy(this);
        }
    }
}
