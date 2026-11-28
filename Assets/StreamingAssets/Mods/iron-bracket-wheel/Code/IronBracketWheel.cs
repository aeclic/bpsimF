using System;
using System.Collections.Generic;
using Bpsim;
using Bpsim.Modding;
using Bpsim.Mods;
using Bpsim.Parts;
using UnityEngine;

namespace IronBracketWheel
{
    public sealed class Entry : ModEntryPoint
    {
        private const int VariantIndex = 9000;
        private const string DisplayName = "铁支架轮";
        private static readonly PartTypeInfo BaseInfo = new PartTypeInfo(PartType.MotorWheel, 0);
        private static readonly PartTypeInfo VariantInfo = new PartTypeInfo(PartType.MotorWheel, VariantIndex);
        private readonly Dictionary<string, string> m_skins = new Dictionary<string, string>
        {
            { "SpokeSprite", "Part_Kicker_0_0" }
        };

        private GameObject m_host;

        public override void OnLoad()
        {
            m_host = new GameObject("IronBracketWheel.Bootstrapper");
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

            PartFactory factory = PartManager.Instance != null ? PartManager.Instance.Factory : null;
            if (factory != null && factory.PartExtensionMap != null)
            {
                factory.PartExtensionMap.Remove(VariantInfo);
            }
            ModParts.RemoveVariant(PartType.MotorWheel, VariantIndex);
        }

        public override bool IsRequiredForMachine(string machineJson)
        {
            return !string.IsNullOrEmpty(machineJson) && machineJson.Contains(VariantIndex.ToString());
        }

        internal bool RegisterVariant()
        {
            PartFactory factory = PartManager.Instance != null ? PartManager.Instance.Factory : null;
            Dictionary<PartTypeInfo, PartExtensionData> extensions = factory != null ? factory.PartExtensionMap : null;
            if (extensions == null || !extensions.TryGetValue(BaseInfo, out PartExtensionData extension))
            {
                return false;
            }

            extension.ConnectionDirection = BitDirection.Any;
            extensions[VariantInfo] = extension;
            if (!ModParts.TryAddVariant(PartType.MotorWheel, 0, VariantIndex, DisplayName, ConfigurePart, out string error))
            {
                extensions.Remove(VariantInfo);
                Debug.LogWarning("[iron-bracket-wheel] variant registration failed: " + error);
                return false;
            }

            ModParts.RegisterVariantIcon(VariantInfo, "Part_Kicker_0_Rendered");
            Debug.Log("[iron-bracket-wheel] registered MotorWheel variant #" + VariantIndex);
            return true;
        }

        private void ConfigurePart(ManagedPart part)
        {
            int changed = ModSprites.SetSkins(part.gameObject, m_skins);
            if (changed != 1)
            {
                throw new InvalidOperationException("MotorWheel#0 must have one SpokeSprite to skin");
            }

            Transform fakeWheel = part.transform.Find("FakeWheelPivot/WheelSprite");
            Renderer renderer = fakeWheel != null ? fakeWheel.GetComponent<Renderer>() : null;
            if (renderer == null)
            {
                throw new InvalidOperationException("MotorWheel#0 is missing FakeWheelPivot/WheelSprite");
            }
            renderer.enabled = false;
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
            if (!m_entry.RegisterVariant()) return;
            Destroy(this);
        }
    }
}
