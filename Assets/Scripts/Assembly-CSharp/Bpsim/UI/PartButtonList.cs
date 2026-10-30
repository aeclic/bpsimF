using System;
using System.Collections.Generic;
using Bpsim.Collections;
using Bpsim.Parts;
using Bpsim.Parts.Simulation;
using Bpsim.Rendering;
using Unity.Entities;
using UnityEngine;
using UnityEngine.UI;

namespace Bpsim.UI
{
	public class PartButtonList : MonoBehaviour
	{
		private class ButtonComparer : IComparer<PartButton>
		{
			public int Compare(PartButton x, PartButton y)
			{
				int componentRank = x.Info.ComponentRank;
				int componentRank2 = y.Info.ComponentRank;
				if (componentRank < componentRank2)
				{
					return -1;
				}
				if (componentRank > componentRank2)
				{
					return 1;
				}
				if (!Settings.HighPartTypePriority)
				{
					Vector2 averagePosition = x.AveragePosition;
					Vector2 averagePosition2 = y.AveragePosition;
					if (averagePosition.x < averagePosition2.x)
					{
						return -1;
					}
					if (averagePosition.x > averagePosition2.x)
					{
						return 1;
					}
				}
				PartType sortedPartType = x.SortedPartType;
				PartType sortedPartType2 = y.SortedPartType;
				if (sortedPartType < sortedPartType2)
				{
					return -1;
				}
				if (sortedPartType > sortedPartType2)
				{
					return 1;
				}
				int partIndex = x.Info.PartIndex;
				int partIndex2 = y.Info.PartIndex;
				if (partIndex < partIndex2)
				{
					return -1;
				}
				if (partIndex > partIndex2)
				{
					return 1;
				}
				int buttonIndex = x.Info.ButtonIndex;
				int buttonIndex2 = y.Info.ButtonIndex;
				if (buttonIndex < buttonIndex2)
				{
					return -1;
				}
				if (buttonIndex > buttonIndex2)
				{
					return 1;
				}
				return 0;
			}
		}

		// internal 而不是 private：QA 需要在没有 PartButtonList 实例时自建映射核对覆盖率
		// （见 Bpsim.QA.SkinSheetQA.AuditButtonSprites）。
		internal struct ButtonSpriteInfo
		{
			public Texture Texture;

			public Rect UVRect;

			public Vector2 Scale;

			public Quaternion Rotation;

			public ButtonSpriteInfo(Texture texture, Rect uvRect, Vector2 scale, Quaternion rotation)
			{
				Texture = texture;
				UVRect = uvRect;
				Scale = scale;
				Rotation = rotation;
			}
		}

		private struct ButtonState
		{
			public int Index;

			public PartButton Button;

			public ButtonState(int index, PartButton button)
			{
				Index = index;
				Button = button;
			}
		}

		[SerializeField]
		private Canvas m_canvas;

		[SerializeField]
		private ScrollRect m_scrollView;

		[SerializeField]
		private GameObject m_triggerButtonPrefab;

		[SerializeField]
		private GameObject m_sliderButtonPrefab;

		private bool m_needsUpdate;

		private ButtonComparer m_comparer;

		private PartButton m_selectedButton;

		private List<PartButton> m_currentButtons;

		private List<PartTriggerButton> m_triggerButtonPool;

		private List<PartSliderButton> m_sliderButtonPool;

		private Heap<(int, int)> m_componentHeap;

		private Dictionary<PartButtonInfo, int> m_buttonInfoMap;

		private Dictionary<PartButtonInfo, ButtonSpriteInfo> m_spriteInfoMap;

		private Dictionary<(Entity, int), ButtonState> m_buttonStateMap;

		public bool NeedsUpdate
		{
			get
			{
				return m_needsUpdate;
			}
			set
			{
				m_needsUpdate = value;
			}
		}

		public PartButton SelectedButton => m_selectedButton;

		public List<PartButton> Buttons => m_currentButtons;

		public GameObject TriggerButtonPrefab => m_triggerButtonPrefab;

		public GameObject SliderButtonPrefab => m_sliderButtonPrefab;

		public static bool Enabled
		{
			get
			{
				if (Instance != null)
				{
					return Instance.gameObject.activeSelf;
				}
				return false;
			}
		}

		public static PartButtonList Instance { get; private set; }

		public static ButtonSettings Settings => UserSettings.Instance.ButtonSettings;

		private void Awake()
		{
			Instance = this;
			m_comparer = new ButtonComparer();
			m_currentButtons = new List<PartButton>();
			m_triggerButtonPool = new List<PartTriggerButton>();
			m_sliderButtonPool = new List<PartSliderButton>();
			m_componentHeap = new Heap<(int, int)>();
			m_buttonInfoMap = new Dictionary<PartButtonInfo, int>();
			m_buttonStateMap = new Dictionary<(Entity, int), ButtonState>();
		}

		public void Initialize()
		{
			CreateSpriteInfoMap();
			UpdateButtons();
		}

		private void CreateSpriteInfoMap()
		{
			m_spriteInfoMap = BuildSpriteInfoMap(CoreManager.Instance.Resources.LoadAsset<Texture2D>("PartButton_Texture"));
		}

		/// <summary>
		/// 构建「按钮信息 → 贴图」映射。
		/// </summary>
		/// <remarks>
		/// 独立成静态方法是为了让 QA 在 HUD 未显示（<see cref="PartButtonList.Instance" /> 为 null）
		/// 时也能核对贴图覆盖率 —— <see cref="SimulationInterface" /> 只在需要时才实例化这份列表，
		/// 空场景里它根本不存在。
		/// </remarks>
		internal static Dictionary<PartButtonInfo, ButtonSpriteInfo> BuildSpriteInfoMap(Texture2D texture)
		{
			Dictionary<PartButtonInfo, ButtonSpriteInfo> map = new Dictionary<PartButtonInfo, ButtonSpriteInfo>();
			AddRotatedButtons(PartType.Fan, "PartButton_Fan");
			AddRotatedButtons(PartType.Propeller, "PartButton_Propeller");
			AddRotatedButtons(PartType.Rotor, "PartButton_Rotor");
			// BPSS GrapplingHook family: hook (0-5), AutoGun (6), multipart generators (8-10).
			AddVariantButtons(PartType.GrapplingHook, 11, "PartButton_GrapplingHook");
			// DecelerationLight (5) and AutoControlLight / 虚化灯 (6).
			AddVariantButtons(PartType.PointLight, 7, "PartButton_PointLight");
			AddVariantButtons(PartType.Rocket, 4, "PartButton_Rocket");
			AddVariantButtons(PartType.RedRocket, 4, "PartButton_RedRocket");
			AddVariantButtons(PartType.CokeBottle, 5, "PartButton_CokeBottle");
			AddVariantButtons(PartType.SodaBottle, 5, "PartButton_SodaBottle");
			AddVariantButtons(PartType.TNT, 7, "PartButton_TNT");
			// 轮族 / 齿轮箱（BPSS HasOnOffToggle）：动力轮 0-6、越野轮 7 共用动力轮图标。
			AddVariantButtons(PartType.MotorWheel, 8, "PartButton_MotorWheel");
			AddSingleButton(texture, PartType.SmallWheel, 7, "PartButton_MotorWheel");
			AddVariantButtons(PartType.StickyWheel, 8, "PartButton_StickyWheel");
			AddVariantButtons(PartType.Gearbox, 6, "PartButton_Gearbox");
			// 机械闸门 0-4（BPSS MechanicalGate1-5）。
			AddVariantButtons(PartType.MechanicalPart, 5, "PartButton_MechanicalGate");
			// 沙袋三档共用一个图标（BPSS 里沙袋贴图一致，只有配重数量不同）。
			AddVariantButtons(PartType.Sandbag, 6, "PartButton_Sandbag");
			AddVariantButtons(PartType.Sandbag2, 4, "PartButton_Sandbag");
			AddVariantButtons(PartType.Sandbag3, 4, "PartButton_Sandbag");
			// 绳索 0-3（4-7 是铰接板，没有按钮）。
			AddVariantButtons(PartType.Rope, 4, "PartButton_Rope");
			// 喷气引擎组里混着燃料件（见 PartType 注释）：0/1 是引擎，2/3/5 是燃料管
			// （只有 5 阀门可点），4 是燃料箱。贴图按外观分派，别让燃料管顶着引擎图标。
			AddSingleButton(texture, PartType.JetEngine, 0, "PartButton_JetEngine");
			AddSingleButton(texture, PartType.JetEngine, 1, "PartButton_JetEngine");
			AddSingleButton(texture, PartType.JetEngine, 2, "PartButton_ValveFuelTube");
			AddSingleButton(texture, PartType.JetEngine, 3, "PartButton_ValveFuelTube");
			AddSingleButton(texture, PartType.JetEngine, 4, "PartButton_Vcc");
			AddSingleButton(texture, PartType.JetEngine, 5, "PartButton_ValveFuelTube");
			// 特殊蛋 1-5 与普通蛋 0 共用蛋图标（BPSS Egg / AlienEgg 同一张）。
			AddVariantButtons(PartType.Egg, 6, "PartButton_Egg");
			// 电路件里只有 4/5/21 可点：4 Switch、5 SPDT Switch、21 Vcc。
			AddVariantButtons(PartType.ElectricalPart, 22, "PartButton_VariableResistor");
			AddSingleButton(texture, PartType.ElectricalPart, 4, "PartButton_Switch");
			AddSingleButton(texture, PartType.ElectricalPart, 5, "PartButton_SPDTSwitch");
			AddSingleButton(texture, PartType.ElectricalPart, 21, "PartButton_Vcc");
			AddSingleButton(texture, PartType.Engine, 0, "PartButton_Engine");
			AddSingleButton(texture, PartType.EngineSmall, 0, "PartButton_Engine");
			AddSingleButton(texture, PartType.EngineBig, 0, "PartButton_Engine");
			AddVariantButtons(PartType.SpotLight, 4, "PartButton_SpotLight");
			AddVariantButtons(PartType.SpringBoxingGlove, 5, "PartButton_SpringBoxingGlove");
			AddVariantButtons(PartType.Umbrella, 7, "PartButton_Umbrella");
			AddVariantButtons(PartType.PoweredUmbrella, 4, "PartButton_PoweredUmbrella");
			AddVariantButtons(PartType.Bellows, 8, "PartButton_Bellows");
			AddVariantButtons(PartType.Balloon, 8, "PartButton_Balloon");
			AddVariantButtons(PartType.Balloons2, 8, "PartButton_Balloon");
			AddVariantButtons(PartType.Balloons3, 8, "PartButton_Balloon");
			AddVariantButtons(PartType.Pumpkin, 2, "PartButton_Pumpkin");
			AddVariantButtons(PartType.Kicker, 5, "PartButton_Kicker");
			AddVariantButtons(PartType.Wings, 5, "PartButton_WoodenWing");
			AddVariantButtons(PartType.Tailplane, 4, "PartButton_WoodenTail");
			AddVariantButtons(PartType.MetalWing, 4, "PartButton_MetalWing");
			AddVariantButtons(PartType.MetalTail, 4, "PartButton_MetalTail");
			void AddVariantButtons(PartType partType, int count, string spriteName)
			{
				for (int n = 0; n < count; n++)
				{
					map[GetButtonInfo(partType, n)] = BuildSpriteInfo(texture, spriteName);
				}
			}
			void AddRotatedButtons(PartType partType, string spriteName)
			{
				for (int n = 0; n < 8; n++)
				{
					map[GetButtonInfo(partType, n)] = BuildSpriteInfo(texture, spriteName, Quaternion.AngleAxis(90 * (n & 3), Vector3.forward));
				}
			}
			void AddSingleButton(Texture2D tex, PartType partType, int partIndex, string spriteName)
			{
				map[GetButtonInfo(partType, partIndex)] = BuildSpriteInfo(tex, spriteName);
			}
			static PartButtonInfo GetButtonInfo(PartType partType, int partIndex)
			{
				return new PartButtonInfo(PartButtonType.Trigger, 0, GetBasePartType(partType), partIndex, -1);
			}
			return map;
		}

		private static ButtonSpriteInfo BuildSpriteInfo(Texture texture, string name)
		{
			return BuildSpriteInfo(texture, name, Quaternion.identity);
		}

		private static ButtonSpriteInfo BuildSpriteInfo(Texture texture, string name, Quaternion rotation)
		{
			int iD = SpriteManager.Instance.GetID(name);
			SpriteRect sprite = SpriteManager.Instance.GetSprite(iD);
			return new ButtonSpriteInfo(texture, new Rect(sprite.U, sprite.V, sprite.W, sprite.H), new Vector2(sprite.VertexW, sprite.VertexH), rotation);
		}

		private bool FindSpriteInfo(PartType partType, out PartButtonInfo buttonInfo, out ButtonSpriteInfo spriteInfo)
		{
			foreach (KeyValuePair<PartButtonInfo, ButtonSpriteInfo> item in m_spriteInfoMap)
			{
				if (item.Key.PartType == partType)
				{
					buttonInfo = item.Key;
					spriteInfo = item.Value;
					return true;
				}
			}
			buttonInfo = default(PartButtonInfo);
			spriteInfo = default(ButtonSpriteInfo);
			return false;
		}

		private bool FindSpriteInfo(PartButtonInfo buttonInfo, out ButtonSpriteInfo spriteInfo)
		{
			buttonInfo.PartType = GetBasePartType(buttonInfo.PartType);
			buttonInfo.ComponentRank = -1;
			// 喷气引擎组里 0/1 是引擎、2/3/5 是燃料管（只有阀门可点）、4 是燃料箱，
			// 索引是有意义的，不能像以前那样统一抹成 -1，否则燃料管会顶着引擎图标。
			return m_spriteInfoMap.TryGetValue(buttonInfo, out spriteInfo);
		}

		private void Update()
		{
			if (m_needsUpdate)
			{
				UpdateButtons();
				m_needsUpdate = false;
			}
		}

		private void UpdateButtons()
		{
			SaveButtonStates();
			CreateButtons();
			RenderButtons();
		}

		internal static bool IsTntButtonHidden(Entity part)
		{
			var em = PartManager.Instance.System.EntityManager;
			if (!em.Exists(part) || !em.HasComponent<PartTypeValue>(part)) return false;
			var info = em.GetComponentData<PartTypeValue>(part).Value;
			if (info.PartType != PartType.TNT) return false;
			if (info.PartIndex == 1 || info.PartIndex == 3) return true;
			return Bpsim.Parts.Gameplay.PartGameplayManager.Instance?.Find(part) is Bpsim.Parts.Gameplay.TNTBehavior tnt && tnt.Spent;
		}

		private void CreateButtons()
		{
			Reference<PartSimulatorUnmanaged> unmanagedRef = PartManager.Instance.PartSimulator.UnmanagedRef;
			int length = unmanagedRef.Value.ConnectedComponents.Length;
			int num = Math.Min(Settings.MaxSeparationCount, length);
			m_componentHeap.Clear();
			m_componentHeap.PushRange(GetComponentInfo(unmanagedRef));
			int[] array = new int[length];
			Array.Fill(array, num);
			for (int i = 0; i < num; i++)
			{
				array[m_componentHeap.Pop().Item2] = i;
			}
			m_buttonInfoMap.Clear();
			bool[] array2 = new bool[m_currentButtons.Count];
			List<PartButton> list = new List<PartButton>(m_currentButtons.Count);
			foreach (Entity part in unmanagedRef.Value.Parts)
			{
				PartAspect aspect = PartManager.Instance.System.EntityManager.GetAspect<PartAspect>(part);
				if (!BasePart.IsTriggerable(aspect.TypeInfo) || IsTntButtonHidden(part))
				{
					continue;
				}
				foreach (PartButtonInfo item in GetAllButtonInfo(aspect))
				{
					PartButtonInfo current2 = item;
					int componentRank = array[current2.ComponentIndex];
					current2.ComponentRank = componentRank;
					if (!m_buttonInfoMap.TryGetValue(current2, out var value))
					{
						value = list.Count;
						m_buttonInfoMap.Add(current2, value);
						list.Add(null);
					}
					m_buttonStateMap.TryGetValue((part, current2.ButtonIndex), out var value2);
					if (list[value] == null && value2.Button != null && !array2[value2.Index])
					{
						list[value] = value2.Button;
						array2[value2.Index] = true;
					}
				}
			}
			for (int j = 0; j < m_currentButtons.Count; j++)
			{
				PartButton partButton = m_currentButtons[j];
				if (!array2[j])
				{
					FreeButton(partButton);
				}
				else
				{
					partButton.Parts.Clear();
				}
			}
			array2 = new bool[list.Count];
			foreach (Entity part2 in unmanagedRef.Value.Parts)
			{
				PartAspect aspect2 = PartManager.Instance.System.EntityManager.GetAspect<PartAspect>(part2);
				if (!BasePart.IsTriggerable(aspect2.TypeInfo) || IsTntButtonHidden(part2))
				{
					continue;
				}
				foreach (PartTriggerButtonInfo item2 in BasePart.GetTriggerButtonInfo(aspect2))
				{
					PartButtonInfo value3 = item2.Value;
					int componentRank2 = array[value3.ComponentIndex];
					value3.ComponentRank = componentRank2;
					int num2 = m_buttonInfoMap[value3];
					PartTriggerButton partTriggerButton = list[num2] as PartTriggerButton;
					if (partTriggerButton == null)
					{
						partTriggerButton = (PartTriggerButton)(list[num2] = AllocateButton<PartTriggerButton>());
					}
					if (array2[num2])
					{
						partTriggerButton.Parts.Add(part2);
						continue;
					}
					array2[num2] = true;
					partTriggerButton.Parts.Add(part2);
					partTriggerButton.SetInfo(value3);
					partTriggerButton.SetConsistent(item2.Consistent);
					partTriggerButton.SetMultiple(item2.Multiple);
					SetButtonSprite(value3, partTriggerButton);
				}
				foreach (PartSliderButtonInfo item3 in BasePart.GetSliderButtonInfo(aspect2))
				{
					PartButtonInfo value4 = item3.Value;
					int componentRank3 = array[value4.ComponentIndex];
					value4.ComponentRank = componentRank3;
					int num3 = m_buttonInfoMap[value4];
					PartSliderButton partSliderButton = list[num3] as PartSliderButton;
					if (partSliderButton == null)
					{
						partSliderButton = (PartSliderButton)(list[num3] = AllocateButton<PartSliderButton>());
					}
					if (array2[num3])
					{
						partSliderButton.Parts.Add(part2);
						continue;
					}
					array2[num3] = true;
					partSliderButton.Parts.Add(part2);
					partSliderButton.SetInfo(value4);
					partSliderButton.SetRange(item3.Range);
					SetButtonSprite(value4, partSliderButton);
				}
			}
			m_currentButtons = list;
			static IEnumerable<PartButtonInfo> GetAllButtonInfo(PartAspect partAspect)
			{
				foreach (PartTriggerButtonInfo item4 in BasePart.GetTriggerButtonInfo(partAspect))
				{
					yield return item4.Value;
				}
				foreach (PartSliderButtonInfo item5 in BasePart.GetSliderButtonInfo(partAspect))
				{
					yield return item5.Value;
				}
			}
			static IEnumerable<(int, int)> GetComponentInfo(Reference<PartSimulatorUnmanaged> simulator)
			{
				int componentCount = simulator.Value.ConnectedComponents.Length;
				for (int k = 0; k < componentCount; k++)
				{
					yield return (-simulator.Value.ConnectedComponents[k].PartCount, k);
				}
			}
		}

		/// <summary>
		/// 判断某个按钮信息能否取到贴图，查找顺序与 <see cref="SetButtonSprite" /> 一致。
		/// </summary>
		/// <remarks>
		/// 供 QA 使用：<c>SkinSheetQA</c> 会枚举所有可触发的 (类型, 索引) 组合核对覆盖率，
		/// 这样「按钮只剩一块底板」这类回归不必靠肉眼截图也能发现。
		/// </remarks>
		internal bool HasSprite(PartButtonInfo buttonInfo)
		{
			return HasSprite(m_spriteInfoMap, buttonInfo);
		}

		/// <summary>
		/// 静态版：给定一份贴图映射，判断按钮信息能否命中。
		/// </summary>
		/// <remarks>
		/// 查找顺序刻意与 <see cref="FindSpriteInfo(PartButtonInfo, out ButtonSpriteInfo)" />
		/// 和 <see cref="SetButtonSprite" /> 的「同类型任一索引」回退保持一致，
		/// 否则审计通过而实际按钮仍可能空图。
		/// </remarks>
		internal static bool HasSprite(Dictionary<PartButtonInfo, ButtonSpriteInfo> map, PartButtonInfo buttonInfo)
		{
			if (map == null)
			{
				return false;
			}
			buttonInfo.ComponentRank = -1;
			buttonInfo.PartType = GetBasePartType(buttonInfo.PartType);
			if (map.ContainsKey(buttonInfo))
			{
				return true;
			}
			foreach (KeyValuePair<PartButtonInfo, ButtonSpriteInfo> item in map)
			{
				if (item.Key.PartType == buttonInfo.PartType)
				{
					return true;
				}
			}
			return false;
		}

		private void SetButtonSprite(PartButtonInfo buttonInfo, PartButton button)
		{
			buttonInfo.ComponentRank = -1;
			buttonInfo.PartType = GetBasePartType(buttonInfo.PartType);
			// 先按 (类型, 索引) 精确查；查不到再退回「同类型任意索引」。
			// 退回分支覆盖的是索引漂移：扇叶/螺旋桨/旋翼的按钮索引取自部件的
			// <c>Rotation</c>（八向放置时可能是 4-7），以及后续新增的变体索引 ——
			// 以前这种情况直接落进 else，按钮就只剩一块底板没有贴图。
			if (FindSpriteInfo(buttonInfo, out var spriteInfo)
				|| FindSpriteInfo(buttonInfo.PartType, out _, out spriteInfo))
			{
				button.SetSprite(enabled: true, spriteInfo.Texture, spriteInfo.UVRect, spriteInfo.Scale / 0.7f, spriteInfo.Rotation);
			}
			else
			{
				button.SetSprite(enabled: false, null, Rect.zero, Vector2.zero, Quaternion.identity);
			}
		}

		public void RenderButtons()
		{
			if (m_currentButtons.Count == 0)
			{
				return;
			}
			foreach (PartButton currentButton in m_currentButtons)
			{
				currentButton.Initialize();
			}
			m_currentButtons.Sort(m_comparer);
			bool displayButtonIndex = Settings.DisplayButtonIndex;
			for (int i = 0; i < m_currentButtons.Count; i++)
			{
				char c = (char)(65 + i % 26);
				char c2 = (char)(48 + i / 26);
				string text = (displayButtonIndex ? ((i < 26) ? c.ToString() : (c.ToString() + c2)) : string.Empty);
				m_currentButtons[i].DisplayIndexText(text);
			}
			int num = 0;
			foreach (PartButton currentButton2 in m_currentButtons)
			{
				num += 1 + currentButton2.SubButtonCount;
			}
			float num2 = 1029.375f * ((float)Screen.width / (float)Screen.height);
			int j;
			for (j = 1; num > j * (int)(num2 / GetPadding(j + 1)); j++)
			{
			}
			float num3 = GetPadding(j);
			float min = GetPadding(j + 1);
			int num4 = num / j + ((num % j != 0) ? 1 : 0);
			if (Settings.LayoutMode == 1 && j > 1)
			{
				num4 = Math.Max(num4, (int)(num2 / num3));
			}
			float num5 = Math.Clamp(num2 / (float)num4, min, num3);
			float num6 = num5 / 240f;
			Vector3 localScale = new Vector3(num6, num6, 1f);
			int num7 = 0;
			float num8 = 10f;
			RectTransform content = m_scrollView.content;
			RectTransform rectTransform = (RectTransform)m_scrollView.transform;
			content.sizeDelta = new Vector2(content.sizeDelta.x, (float)j * num5 + num8);
			rectTransform.sizeDelta = new Vector2(rectTransform.sizeDelta.x, Math.Min((float)j * num5 + num8, 360f * Settings.ScrollViewHeightScale));
			foreach (PartButton currentButton3 in m_currentButtons)
			{
				float num9 = (float)(-(num4 - 1)) / 2f + (float)(num7 % num4);
				float num10 = (float)(-(j - 1)) / 2f + (float)(num7 / num4);
				Vector2 vector = new Vector2(num5 * num9, (0f - num5) * num10 + num8);
				RectTransform obj = (RectTransform)currentButton3.transform;
				obj.localScale = localScale;
				obj.anchoredPosition = vector;
				num7++;
				if (currentButton3.SubButtonCount == 0)
				{
					continue;
				}
				foreach (PartButton subButton in currentButton3.SubButtons)
				{
					num9 = (float)(-(num4 - 1)) / 2f + (float)(num7 % num4);
					num10 = (float)(-(j - 1)) / 2f + (float)(num7 / num4);
					Vector2 vector2 = new Vector2(num5 * num9, (0f - num5) * num10 + num8);
					((RectTransform)subButton.transform).anchoredPosition = (vector2 - vector) / num6;
					num7++;
				}
			}
			static float GetPadding(int n)
			{
				return (float)Math.Max(200 - 20 * n, 140) * Settings.ButtonScale;
			}
		}

		private void SaveButtonStates()
		{
			m_buttonStateMap.Clear();
			for (int i = 0; i < m_currentButtons.Count; i++)
			{
				PartButton partButton = m_currentButtons[i];
				foreach (Entity part in partButton.Parts)
				{
					m_buttonStateMap.Add((part, partButton.Info.ButtonIndex), new ButtonState(i, partButton));
				}
			}
		}

		private void FreeButtons()
		{
			foreach (PartButton currentButton in m_currentButtons)
			{
				if (currentButton != null)
				{
					FreeButton(currentButton);
				}
			}
			m_currentButtons.Clear();
		}

		private T AllocateButton<T>() where T : PartButton
		{
			if (typeof(T) == typeof(PartTriggerButton))
			{
				List<PartTriggerButton> triggerButtonPool = m_triggerButtonPool;
				if (triggerButtonPool.Count == 0)
				{
					return CreateButton<T>();
				}
				T val = (T)(PartButton)triggerButtonPool[triggerButtonPool.Count - 1];
				triggerButtonPool.RemoveAt(triggerButtonPool.Count - 1);
				val.gameObject.SetActive(value: true);
				return val;
			}
			if (typeof(T) == typeof(PartSliderButton))
			{
				List<PartSliderButton> sliderButtonPool = m_sliderButtonPool;
				if (sliderButtonPool.Count == 0)
				{
					return CreateButton<T>();
				}
				T val2 = (T)(PartButton)sliderButtonPool[sliderButtonPool.Count - 1];
				sliderButtonPool.RemoveAt(sliderButtonPool.Count - 1);
				val2.gameObject.SetActive(value: true);
				return val2;
			}
			return null;
		}

		private T CreateButton<T>() where T : PartButton
		{
			GameObject original = null;
			if (typeof(T) == typeof(PartTriggerButton))
			{
				original = m_triggerButtonPrefab;
			}
			else if (typeof(T) == typeof(PartSliderButton))
			{
				original = m_sliderButtonPrefab;
			}
			T component = UnityEngine.Object.Instantiate(original).GetComponent<T>();
			component.transform.SetParent(m_scrollView.content, worldPositionStays: false);
			component.gameObject.SetActive(value: true);
			return component;
		}

		private void FreeButton(PartButton button)
		{
			button.Reset();
			button.gameObject.SetActive(value: false);
			if (button is PartTriggerButton item)
			{
				m_triggerButtonPool.Add(item);
			}
			else if (button is PartSliderButton item2)
			{
				m_sliderButtonPool.Add(item2);
			}
		}

		private static PartType GetBasePartType(PartType partType)
		{
			if (partType == PartType.EngineSmall || partType == PartType.EngineBig)
			{
				partType = PartType.Engine;
			}
			return partType;
		}
	}
}
