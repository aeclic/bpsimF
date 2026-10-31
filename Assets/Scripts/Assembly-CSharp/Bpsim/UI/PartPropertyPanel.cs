using System;
using System.Collections.Generic;
using System.Globalization;
using Bpsim.Parts;
using Bpsim.Parts.Skin;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Bpsim.UI
{
	internal class PartPropertyPanel : SwitchableInterface
	{
		[SerializeField] private Text m_partType;
		private PartScene m_scene;
		private Entity m_part, m_skinPart;
		private Font m_font;
		private RectTransform m_content;
		private Text m_info, m_material, m_attitude, m_status, m_physics;
		private Button m_dockButton, m_flipButton, m_shapeButton, m_skinButton;
		private GameObject m_switchTo, m_switchBack;
		private RectTransform m_scrollRoot, m_footer;
		private readonly List<PropertySection> m_sections = new List<PropertySection>();
		private PropertySection m_section, m_rotationSection;
		private readonly List<NumericRow> m_rows = new List<NumericRow>();
		private readonly List<Selectable> m_editors = new List<Selectable>();
		private float m_height;
		private float m_optionalTop;
		private bool m_refreshing;
		internal bool IsClosing { get; private set; }
		public new void Close() { IsClosing = true; base.Close(); }
		private static readonly Color Plate = new Color(.16f, .16f, .16f, 1f);
		private static readonly Color Field = new Color(.2f, .2f, .2f, 1f);
		private static readonly Color Blue = new Color(44f / 255, 89f / 255, 149f / 255, 1f);
		private sealed class PropertySection
		{
			public RectTransform Rect;
			public float Height;
			public int Column;
		}

		private sealed class NumericRow
		{
			public InputField Input;
			public Slider Slider;
			public Func<float> Read;
			public Func<float, bool> Write;
			public bool Recorded;
		}

		public void Initialize(PartScene scene, Entity part)
		{
			CloseSkin(); m_scene = scene; m_part = part;
			foreach (var row in m_rows) row.Recorded = false;
			m_status.text = "";
			RefreshValues();
		}

		public void UpdateLayout(bool sidebar)
		{
			var ui = UserInterface.Instance;
			m_inSidebar = sidebar;
			var rect = (RectTransform)transform;
			rect.SetParent(sidebar ? ui.RightSidebar.Content.transform : ui.SubCanvas.transform, false);
			rect.localScale = Vector3.one;
			if (sidebar)
			{
				rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0f, 1f);
				rect.anchoredPosition = Vector2.zero;
				rect.sizeDelta = new Vector2(ui.RightSidebar.Size.x, ui.RightSidebar.Size.y);
			}
			else
			{
				rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(.5f, .5f);
				rect.anchoredPosition = Vector2.zero;
				var canvasRect = (RectTransform)ui.SubCanvas.transform;
				rect.sizeDelta = new Vector2(Mathf.Min(1600f, canvasRect.rect.width - 40f), Mathf.Min(900f, canvasRect.rect.height - 40f));
			}
			m_switchTo.SetActive(sidebar); m_switchBack.SetActive(!sidebar);
			Reflow();
		}

		protected override void Awake()
		{
			// The shipped prefab has the same header, font and button artwork as bpsimtaffy.
			// Keep that artwork, and lay out the additional editable fields responsively.
			m_font = m_partType != null ? m_partType.font : Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
			if (m_panel != null) m_panel.SetActive(false);
			var body = Rect("Properties", transform); Stretch(body, 0, 0, 0, 0);
			var plate = Image(body, Plate);
			var originalPlate = m_panel.GetComponent<Image>(); plate.sprite = originalPlate.sprite; plate.type = originalPlate.type;
			var header = (RectTransform)Instantiate(m_panel.transform.Find("Header").gameObject, body, false).transform;
			header.name = "Header"; Top(header, 0, 0, 0, 60);
			header.GetComponent<RawImage>().color = Blue;
			header.Find("Text").GetComponent<Text>().text = "部件属性";
			m_switchTo = header.Find("SwitchToButton").gameObject;
			m_switchBack = header.Find("SwitchBackButton").gameObject;
			m_dockButton = m_switchTo.GetComponent<Button>();
			m_dockButton.onClick.AddListener(() => UpdateLayout(false));
			m_switchBack.GetComponent<Button>().onClick.AddListener(() => UpdateLayout(true));

			m_scrollRoot = Rect("Scroll View", body); Stretch(m_scrollRoot, 30, 90, 20, 150);
			var viewport = Rect("Viewport", m_scrollRoot); Stretch(viewport, 0, 0, 30, 0);
			Image(viewport, Color.clear); viewport.gameObject.AddComponent<RectMask2D>();
			m_content = Rect("Content", viewport);
			m_content.anchorMin = new Vector2(0, 1); m_content.anchorMax = Vector2.one; m_content.pivot = new Vector2(0, 1);
			var scroll = m_scrollRoot.gameObject.AddComponent<ScrollRect>(); scroll.viewport = viewport; scroll.content = m_content;
			scroll.horizontal = false; scroll.movementType = ScrollRect.MovementType.Clamped; scroll.scrollSensitivity = 35f;
			var nativeBar = UserInterface.Instance.PartSelector.GetComponentInChildren<Scrollbar>(true);
			var bar = Instantiate(nativeBar.gameObject, m_scrollRoot, false).GetComponent<Scrollbar>();
			bar.name = "Scrollbar Vertical";
			bar.direction = Scrollbar.Direction.BottomToTop;
			bar.gameObject.SetActive(true);
			var barRect = (RectTransform)bar.transform;
			barRect.anchorMin = new Vector2(1, 0); barRect.anchorMax = Vector2.one; barRect.pivot = Vector2.one;
			barRect.offsetMin = new Vector2(-20, 0); barRect.offsetMax = Vector2.zero;
			UIExtensions.StyleScrollbars(m_scrollRoot.gameObject);
			bar.direction = Scrollbar.Direction.BottomToTop; bar.navigation = new Navigation { mode = Navigation.Mode.None };
			scroll.verticalScrollbar = bar;

			Section("PartTypeInfo", "部件类型与材质", 0);
			m_info = ReadOnlyRow("PartType", ""); m_material = ReadOnlyRow("PartIndex", "");
			Section("Position", "位置坐标", 0);
			Number("GridX", "网格 X", -50, 50, true, () => TransformData().X, v => MoveGrid((int)v, TransformData().Y), true);
			Number("GridY", "网格 Y", -50, 50, true, () => TransformData().Y, v => MoveGrid(TransformData().X, (int)v), true);
			Section("Rotation", "旋转朝向", 1);
			Number("Rotation", "朝向序号", 0, 7, true, () => TransformData().Rotation, v => SetRotation((int)v), true);
			var rotation = m_rows[m_rows.Count - 1].Input;
			Top((RectTransform)rotation.transform, 0, 0, 70, 60);
			var increment = Instantiate(m_panel.transform.Find("Rotation/IncrementButton").gameObject, rotation.transform.parent, false).GetComponent<Button>();
			increment.name = "Increment"; Right((RectTransform)increment.transform, 0, 0, 60, 60);
			increment.onClick.AddListener(() => Edit(() => SetRotation(TransformData().Rotation + 1))); m_editors.Add(increment);
			m_attitude = ReadOnlyRow("Quaternion", "");
			Section("Parameters", "", 1);
			Number("ScaleX", "宽度", 1, 6, false, () => Free().Scale.x, v => SetFree(3, Mathf.Clamp(v, .05f, 20)));
			Number("ScaleY", "高度", 1, 6, false, () => Free().Scale.y, v => SetFree(4, Mathf.Clamp(v, .05f, 20)));
			Number("FineAngle", "角度", -180, 180, false, () => Free().RotationDegrees, v => SetFree(2, v));
			Section("ExtraParameters", "其他参数", 1); m_rotationSection = m_section;
			Number("OffsetX", "X 偏移", -10, 10, false, () => Free().Offset.x, v => SetFree(0, v));
			Number("OffsetY", "Y 偏移", -10, 10, false, () => Free().Offset.y, v => SetFree(1, v));
			Number("Layer", "图层", 1, 16, true, () => m_scene.Unmanaged.GetPartLayer(m_part), v => m_scene.Unmanaged.TrySetPartLayer(m_part, (int)v));
			m_flipButton = ActionRow("Flip", "镜像：关", () =>
			{
				var t = TransformData(); t.Flipped = !t.Flipped; Manager.SetComponentData(m_part, t); UpdateNeighbours();
			});
			ActionRow("ResetTransform", "重置变换", () => m_scene.Unmanaged.TrySetPartFree(m_part, PartFreeTransform.Identity));
			m_optionalTop = m_height;
			m_shapeButton = ActionRow("FrameShape", "形状", () => FrameShapeSystem.SetShape(Manager, m_part, (Manager.GetComponentData<FrameShape>(m_part).Value + 1) % FrameShape.Count));
			m_skinButton = ActionRow("Skin", "编辑蒙皮…", () => { m_skinPart = m_part; SkinPanelHost.Instance.Open(m_part); });
			Section("Physics", "部件物理信息", 1);
			var physicsPlate = Rect("Values", m_section.Rect); Top(physicsPlate, 0, m_height, 0, 180); Image(physicsPlate, Field);
			m_physics = Label(physicsPlate, "", 24); Stretch((RectTransform)m_physics.transform, 10, 10, 10, 10);
			m_physics.alignment = TextAnchor.UpperLeft; m_physics.lineSpacing = 1.4f;
			m_height += 180; m_section.Height = m_height;

			m_status = Label(body, "", 20);
			var statusRect = (RectTransform)m_status.transform;
			statusRect.anchorMin = Vector2.zero; statusRect.anchorMax = new Vector2(1, 0); statusRect.pivot = Vector2.zero;
			statusRect.anchoredPosition = new Vector2(30, 100); statusRect.sizeDelta = new Vector2(-60, 30);
			m_footer = Rect("Actions", body); m_footer.anchorMin = Vector2.zero; m_footer.anchorMax = new Vector2(1, 0); m_footer.pivot = Vector2.zero;
			var erase = CopyButton("EraseButton", "Erase", Erase);
			erase.targetGraphic.color = new Color(43f / 255, 77f / 255, 123f / 255, 1);
			CopyButton("CloseButton", "Close", Close);
			m_editors.Add(erase);
		}

		private Button CopyButton(string source, string name, Action action)
		{
			var button = Instantiate(m_panel.transform.Find("ButtonGroup/" + source).gameObject, m_footer, false).GetComponent<Button>();
			button.name = name; button.onClick.AddListener(() => action()); return button;
		}

		private void Reflow()
		{
			float width = ((RectTransform)transform).sizeDelta.x;
			bool columns = !m_inSidebar && width >= 1150f;
			float margin = columns ? width * .075f : 30f;
			Stretch(m_scrollRoot, margin, columns ? 120 : 70, columns ? margin : 0, columns ? 220 : 150);
			float fieldWidth = Mathf.Min(360, columns ? (width - margin * 2 - 80) / 2 : width - 90);
			float left = 0, right = 0;
			foreach (var section in m_sections)
			{
				bool second = columns && section.Column == 1;
				float y = second ? right : left;
				var rect = section.Rect; rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0, 1);
				bool compact = rect.name == "Parameters" || rect.name == "ExtraParameters";
				rect.anchoredPosition = new Vector2(second ? width / 2 : 0, -y); rect.sizeDelta = new Vector2(fieldWidth + (compact ? 30 : 0), section.Height);
				if (second) right += section.Height + 10; else left += section.Height + 10;
			}
			m_content.sizeDelta = new Vector2(0, Mathf.Max(left, right) - 10);
			m_footer.anchorMax = m_footer.anchorMin = Vector2.zero;
			m_footer.anchoredPosition = new Vector2(columns ? width / 2 + margin : margin, columns ? 120 : 30);
			m_footer.sizeDelta = new Vector2(350, 60);
			Right((RectTransform)m_footer.Find("Close"), 0, 0, 150, 60);
			Right((RectTransform)m_footer.Find("Erase"), 200, 0, 150, 60);
		}

		private EntityManager Manager => m_scene.System.EntityManager;
		private PartTransform TransformData() => Manager.GetComponentData<PartTransform>(m_part);
		private PartFreeTransform Free() => m_scene.Unmanaged.GetPartFreeTransform(m_part);
		private bool Valid => m_scene != null && PartManager.Instance != null && PartManager.Instance.ActiveScene == m_scene && Manager.Exists(m_part) && Manager.HasComponent<PartTypeValue>(m_part) && Manager.HasComponent<PartTransform>(m_part);
		private bool CanEdit => Valid && !PartManager.Instance.IsSimulating && m_scene.Unmanaged.CanEdit;
		private void Update() { if (!Valid) { Close(); return; } RefreshValues(); }

		private void RefreshValues()
		{
			if (!Valid) return;
			m_refreshing = true;
			var type = Manager.GetComponentData<PartTypeValue>(m_part);
			m_info.text = type.Type.GetAliasName(); m_material.text = type.Index.ToString();
			if (Manager.HasComponent<Unity.Transforms.WorldTransform>(m_part))
			{
				var angles = math.degrees(Bpsim.Physics.MathExtensions.ToEulerAngles(Manager.GetComponentData<Unity.Transforms.WorldTransform>(m_part).Rotation));
				m_attitude.text = $"({angles.y:0.###}, {angles.z:0.###}, {angles.x:0.###})";
			}
			foreach (var row in m_rows)
			{
				if (row.Input.isFocused) continue;
				float value = row.Read();
				row.Input.SetTextWithoutNotify(value.ToString("0.###", CultureInfo.InvariantCulture));
				if (row.Slider != null) row.Slider.SetValueWithoutNotify(value);
			}
			foreach (var editor in m_editors) editor.interactable = CanEdit;
			m_flipButton.GetComponentInChildren<Text>().text = "镜像：" + (TransformData().Flipped ? "开" : "关");
			bool shape = FrameShapeSystem.FeatureEnabled && Manager.HasComponent<FrameShape>(m_part);
			m_shapeButton.gameObject.SetActive(shape);
			if (shape) m_shapeButton.GetComponentInChildren<Text>().text = "形状：" + FrameShape.Name(Manager.GetComponentData<FrameShape>(m_part).Value);
			m_skinButton.gameObject.SetActive(Manager.HasComponent<SkinSheet>(m_part));
			float y = m_optionalTop;
			if (shape) { Top((RectTransform)m_shapeButton.transform, 0, y, 0, 60); y += 80; }
			if (m_skinButton.gameObject.activeSelf) { Top((RectTransform)m_skinButton.transform, 0, y, 0, 60); y += 80; }
			if (m_rotationSection.Height != y - 20) { m_rotationSection.Height = y - 20; Reflow(); }
			if (Manager.HasComponent<PartExtensionComponent>(m_part))
			{
				var d = Manager.GetComponentData<PartExtensionComponent>(m_part).Value;
				m_physics.text = $"质量    {d.Mass:0.###}\n引擎功率    {d.EnginePower:0.###}\n功耗    {d.PowerConsumption:0.###}\n连接强度    {d.ConnectionStrength:0.###}";
			}
			else m_physics.text = "无物理参数";
			if (PartManager.Instance.IsSimulating) m_status.text = "模拟中 · 属性只读";
			else if (!m_scene.Unmanaged.CanEdit) m_status.text = "全部图层 · 选择具体图层以编辑";
			else if (m_status.text == "模拟中 · 属性只读" || m_status.text == "全部图层 · 选择具体图层以编辑") m_status.text = "修改即时生效 · Ctrl+Z 撤销";
			m_refreshing = false;
		}

		private bool SetFree(int field, float value)
		{
			var free = Free();
			switch (field) { case 0: free.Offset.x = value; break; case 1: free.Offset.y = value; break; case 2: free.RotationDegrees = value; break; case 3: free.Scale.x = value; break; case 4: free.Scale.y = value; break; }
			return m_scene.Unmanaged.TrySetPartFree(m_part, free);
		}
		private bool SetRotation(int value)
		{
			var t = TransformData(); t.Rotation = ((value % 8) + 8) % 8;
			Manager.SetComponentData(m_part, t); UpdateNeighbours(); return true;
		}
		private void UpdateNeighbours()
		{
			int previous = m_scene.Unmanaged.ActiveLayer;
			m_scene.Unmanaged.SetActiveLayer(m_scene.Unmanaged.GetPartLayer(m_part));
			try { var aspect = Manager.GetAspect<PartAspect>(m_part); m_scene.Unmanaged.UpdateNeighbours(in aspect); }
			finally { m_scene.Unmanaged.SetActiveLayer(previous); }
		}
		private bool MoveGrid(int x, int y)
		{
			int previous = m_scene.Unmanaged.ActiveLayer;
			m_scene.Unmanaged.SetActiveLayer(m_scene.Unmanaged.GetPartLayer(m_part));
			try { var t = TransformData(); return m_scene.Unmanaged.TryMovePartContent(t.X, t.Y, x, y); }
			finally { m_scene.Unmanaged.SetActiveLayer(previous); }
		}
		private void Record() => UserInterface.Instance.BuildTool?.Record("部件属性");
		private void Edit(Func<bool> action)
		{
			if (!CanEdit) return;
			Manager.CompleteAllTrackedJobs(); Record();
			m_status.text = action() ? "已更新 · Ctrl+Z 撤销" : "目标位置或图层已占用";
			RefreshValues();
		}
		private void Erase()
		{
			if (!CanEdit) return;
			int previous = m_scene.Unmanaged.ActiveLayer;
			m_scene.Unmanaged.SetActiveLayer(m_scene.Unmanaged.GetPartLayer(m_part));
			try { var t = TransformData(); var type = Manager.GetComponentData<PartTypeValue>(m_part); Edit(() => m_scene.Unmanaged.RemovePartAt(t.X, t.Y, type.Type, type.Index)); }
			finally { m_scene.Unmanaged.SetActiveLayer(previous); }
			Close();
		}
		private void CloseSkin() { if (m_skinPart != Entity.Null) { SkinPanelHost.Instance.Close(); m_skinPart = Entity.Null; } }
		private void OnDestroy() => CloseSkin();

		private void Section(string name, string title, int column)
		{
			m_section = new PropertySection { Rect = Rect(name, m_content), Column = column };
			m_sections.Add(m_section); m_height = string.IsNullOrEmpty(title) ? 0 : 60;
			m_section.Height = m_height;
			if (!string.IsNullOrEmpty(title)) { var text = Label(m_section.Rect, title, 24); Top((RectTransform)text.transform, 0, 0, 0, 60); }
		}
		private Text ReadOnlyRow(string name, string value)
		{
			var rect = Rect(name, m_section.Rect); Top(rect, 0, m_height, 0, 60); Image(rect, Field); m_height += 80;
			m_section.Height = m_height - 20;
			var text = Label(rect, value, 24); Stretch((RectTransform)text.transform, 10, 0, 10, 0); return text;
		}
		private Button ActionRow(string name, string text, Action action)
		{
			var button = Button(m_section.Rect, name, text, () => Edit(() => { action(); return true; }));
			Top((RectTransform)button.transform, 0, m_height, 0, 60); m_height += 80; m_section.Height = m_height - 20; m_editors.Add(button); return button;
		}
		private void Number(string key, string label, float min, float max, bool integer, Func<float> read, Func<float, bool> write, bool bare = false)
		{
			var rect = Rect(key, m_section.Rect); Top(rect, 0, m_height, 0, bare ? 60 : 22); m_height += bare ? 80 : 24;
			m_section.Height = m_height - (bare ? 20 : 2);
			var inputRect = Rect("Value", rect);
			if (bare) Stretch(inputRect, 0, 0, 0, 0);
			else
			{
				Right(inputRect, 0, 0, 56, 22); var title = Label(rect, label, 20); title.fontStyle = FontStyle.Normal;
				var titleRect = (RectTransform)title.transform; titleRect.anchorMin = titleRect.anchorMax = titleRect.pivot = new Vector2(0, 1);
				titleRect.anchoredPosition = Vector2.zero; titleRect.sizeDelta = new Vector2(84, 22);
			}
			var input = inputRect.gameObject.AddComponent<InputField>(); input.targetGraphic = Image(inputRect, bare ? Field : Color.clear);
			var valueText = Label(inputRect, "", bare ? 24 : 20); Stretch((RectTransform)valueText.transform, bare ? 10 : 0, 0, bare ? 10 : 0, 0);
			valueText.alignment = bare ? TextAnchor.MiddleLeft : TextAnchor.MiddleRight; if (!bare) valueText.fontStyle = FontStyle.Normal;
			input.textComponent = valueText; input.lineType = InputField.LineType.SingleLine;
			input.contentType = integer ? InputField.ContentType.IntegerNumber : InputField.ContentType.DecimalNumber;
			PropertyValueSlider slider = null;
			if (!bare)
			{
				var sliderRect = Rect("Slider", rect); Top(sliderRect, 90, 5, 62, 12);
				var track = Rect("Background", sliderRect); Stretch(track, 0, 0, 0, 0); Image(track, new Color(.1248f, .1248f, .1248f, 1));
				var fillArea = Rect("Fill Area", sliderRect); Stretch(fillArea, 6, 0, 6, 0);
				var fill = Rect("Fill", fillArea); Stretch(fill, 0, 0, 0, 0); Image(fill, new Color(47f / 255, 128f / 255, 237f / 255, 1));
				var area = Rect("Handle Slide Area", sliderRect); Stretch(area, 6, 0, 6, 0);
				var knob = Rect("Handle", area); knob.sizeDelta = new Vector2(14, 0);
				slider = sliderRect.gameObject.AddComponent<PropertyValueSlider>(); slider.handleRect = knob; slider.fillRect = fill;
				slider.targetGraphic = Image(knob, new Color(.93f, .93f, .94f, 1));
				slider.minValue = min; slider.maxValue = max; slider.wholeNumbers = integer; slider.navigation = new Navigation { mode = Navigation.Mode.None };
			}
			var row = new NumericRow { Input = input, Slider = slider, Read = read, Write = write }; m_rows.Add(row);
			if (slider != null) { slider.PressStarted = () => row.Recorded = false;
			slider.onValueChanged.AddListener(value =>
			{
				if (m_refreshing || !CanEdit || Mathf.Approximately(row.Read(), value)) return;
				Manager.CompleteAllTrackedJobs(); if (!row.Recorded) { Record(); row.Recorded = true; }
				m_status.text = row.Write(value) ? "已更新 · Ctrl+Z 撤销" : "目标位置或图层已占用"; RefreshValues();
			}); }
			input.onEndEdit.AddListener(text =>
			{
				if (!CanEdit) return;
				if (float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) && math.isfinite(value) && math.abs(value) <= 100000)
				{
					if (integer) value = Mathf.Round(value);
					if (key == "Layer") value = Mathf.Clamp(value, 1, 16);
					if (!Mathf.Approximately(read(), value)) Edit(() => write(value));
				}
				else m_status.text = "请输入有效数值";
				RefreshValues();
			});
			m_editors.Add(input); if (slider != null) m_editors.Add(slider);
		}

		private static RectTransform Rect(string name, Transform parent)
		{
			var rect = (RectTransform)new GameObject(name, typeof(RectTransform)).transform; rect.SetParent(parent, false); return rect;
		}
		private static Image Image(RectTransform rect, Color color)
		{
			var image = rect.gameObject.AddComponent<Image>(); image.color = color; return image;
		}
		private Text Label(Transform parent, string value, int size, Color? color = null)
		{
			var rect = Rect("Text", parent); Stretch(rect, 0, 0, 0, 0);
			var text = rect.gameObject.AddComponent<Text>(); text.font = m_font; text.fontSize = size; text.color = color ?? new Color(.93f, .93f, .93f, 1);
			text.fontStyle = FontStyle.Bold; text.color = color ?? Color.white;
			text.text = value; text.alignment = TextAnchor.MiddleLeft; text.raycastTarget = false; return text;
		}
		private Button Button(Transform parent, string name, string text, Action action)
		{
			var rect = Rect(name, parent); var button = rect.gameObject.AddComponent<Button>(); button.targetGraphic = Image(rect, new Color(.3f, .3f, .3f, 1));
			var label = Label(rect, text, 24); label.alignment = TextAnchor.MiddleCenter;
			button.onClick.AddListener(() => action()); button.navigation = new Navigation { mode = Navigation.Mode.None }; return button;
		}
		private static void Stretch(RectTransform rect, float left, float top, float right, float bottom)
		{
			rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one; rect.offsetMin = new Vector2(left, bottom); rect.offsetMax = new Vector2(-right, -top);
		}
		private static void Top(RectTransform rect, float left, float top, float right, float height)
		{
			rect.anchorMin = new Vector2(0, 1); rect.anchorMax = Vector2.one; rect.pivot = new Vector2(0, 1);
			rect.anchoredPosition = new Vector2(left, -top); rect.sizeDelta = new Vector2(-left - right, height);
		}
		private static void Right(RectTransform rect, float right, float top, float width, float height)
		{
			rect.anchorMin = rect.anchorMax = rect.pivot = Vector2.one; rect.anchoredPosition = new Vector2(-right, -top); rect.sizeDelta = new Vector2(width, height);
		}
	}

	internal sealed class PropertyValueSlider : Slider
	{
		internal Action PressStarted;
		public override void OnPointerDown(PointerEventData eventData)
		{
			PressStarted?.Invoke();
			base.OnPointerDown(eventData);
		}
	}
}
