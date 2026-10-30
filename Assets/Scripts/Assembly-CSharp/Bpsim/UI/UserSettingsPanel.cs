using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Bpsim.Background;
using Bpsim.ComponentModel;
using UnityEngine;
using UnityEngine.UI;

namespace Bpsim.UI
{
	internal class UserSettingsPanel : InterfaceBase
	{
		[SerializeField]
		private GameObject m_content;

		[SerializeField]
		private GameObject m_buttonGroup;

		[SerializeField]
		private Button m_resetButton;

		[SerializeField]
		private Button m_closeButton;

		[SerializeField]
		private GameObject m_toggleTemplate;

		[SerializeField]
		private GameObject m_inputFieldTemplate;

		[SerializeField]
		private GameObject m_dropdownTemplate;

		private List<GameObject> m_settingItems;

		private DependencyProxy<SimulationSettings> m_simulationSettingsProxy;

		private DependencyProxy<InteractionSettings> m_interactionSettingsProxy;

		// ---- 自定义背景 ----

		private DependencyProxy<CustomBackgroundSettings> m_backgroundProxy;

		private InputField m_backgroundProgressField;

		private Slider m_backgroundProgressSlider;

		private Text m_backgroundStatusText;

		private bool m_backgroundFillApplied;

		private float m_backgroundStashedScaleX = 1f;

		private float m_backgroundStashedScaleY = 1f;

		private bool m_backgroundDirty;

		// ---- 自定义背景抽屉 ----

		private GameObject m_backgroundDrawer;

		private Text m_backgroundDrawerArrow;

		private Transform m_rowParent;

		/// <summary>新行挂到哪个容器：抽屉打开时是抽屉，平时是面板 Content。</summary>
		private Transform RowParent
		{
			get
			{
				return (m_rowParent != null) ? m_rowParent : m_content.transform;
			}
		}

		// ---- Debug 控制台 ----

		private DependencyProxy<DebugConsoleSettings> m_debugConsoleProxy;

		private void Start()
		{
			Bind();
		}

		private void Update()
		{
			UpdateBackgroundPlayback();
			if (m_backgroundDirty)
			{
				m_backgroundDirty = false;
				UserSettings.Save();
			}
		}

		private void Bind()
		{
			m_closeButton.onClick.AddListener(Close);
			m_resetButton.onClick.AddListener(Reset);
			m_settingItems = new List<GameObject>();
			m_simulationSettingsProxy = new DependencyProxy<SimulationSettings>(UserSettings.Instance.SimulationSettings);
			AddSettingItems(m_simulationSettingsProxy);
			m_interactionSettingsProxy = new DependencyProxy<InteractionSettings>(UserSettings.Instance.InteractionSettings);
			AddSettingItems(m_interactionSettingsProxy);
			BindCustomBackground();
			BindModSection();
			BindDebugConsoleSection();
		}

		/// <summary>
		/// Mod 分组：一个「打开 Mod 管理器」按钮，放在设置里，玩家不必知道 F10 快捷键。
		/// </summary>
		private void BindModSection()
		{
			AddSectionHeader("Mod");
			AddButton("打开 Mod 管理器", OpenModManager);
		}

		private void OpenModManager()
		{
			Bpsim.Mods.ModManagerUI.Open();
		}

		/// <summary>
		/// Debug 控制台分组：一个开关，决定整个控制台组件存不存在。
		/// </summary>
		/// <remarks>
		/// 用显式的中文标题行而不是 <see cref="AddSettingItems{T}"/> 的自动行，因为自动行会把属性名
		/// （<c>Enabled</c>）直接当标题显示，看不出这是哪个功能的开关。
		/// </remarks>
		private void BindDebugConsoleSection()
		{
			DebugConsoleSettings settings = UserSettings.Instance.DebugConsole;
			if (settings == null)
			{
				return;
			}
			m_debugConsoleProxy = new DependencyProxy<DebugConsoleSettings>(settings);
			AddSectionHeader("调试");
			DependencyProperty<bool> property = m_debugConsoleProxy.GetProperty<bool>("Enabled");
			GameObject row = UnityEngine.Object.Instantiate(m_toggleTemplate);
			row.transform.Find("Value").GetComponent<ToggleSwitch>().Bind(property);
			row.SetActive(value: true);
			row.transform.SetParent(RowParent, worldPositionStays: false);
			row.transform.Find("Name").GetComponent<Text>().text = "启用 debug 控制台";
			m_settingItems.Add(row);
		}

		private void AddSettingItems<T>(DependencyProxy<T> proxy) where T : ObservableObject
		{
			foreach (KeyValuePair<string, DependencyProperty> item in proxy.Properties)
			{
				DependencyProperty value = item.Value;
				GameObject gameObject;
				if (value is DependencyProperty<bool> property)
				{
					gameObject = UnityEngine.Object.Instantiate(m_toggleTemplate);
					gameObject.transform.Find("Value").GetComponent<ToggleSwitch>().Bind(property);
				}
				else if (value.PropertyType.IsEnum)
				{
					gameObject = UnityEngine.Object.Instantiate(m_dropdownTemplate);
					Dropdown component = gameObject.transform.Find("Value").GetComponent<Dropdown>();
					string[] names = Enum.GetNames(value.PropertyType);
					component.options = new List<Dropdown.OptionData>(names.Length);
					string[] array = names;
					foreach (string text in array)
					{
						component.options.Add(new Dropdown.OptionData(text));
					}
					typeof(Binding).GetMethod("Bind", new Type[2]
					{
						typeof(Dropdown),
						typeof(DependencyProperty<>).MakeGenericType(Type.MakeGenericMethodParameter(0))
					}).MakeGenericMethod(value.PropertyType).Invoke(this, new object[2] { component, value });
				}
				else
				{
					gameObject = UnityEngine.Object.Instantiate(m_inputFieldTemplate);
					InputField component2 = gameObject.transform.Find("Value").GetComponent<InputField>();
					typeof(Binding).GetMethod("Bind", new Type[2]
					{
						typeof(InputField),
						typeof(DependencyProperty<>).MakeGenericType(Type.MakeGenericMethodParameter(0))
					}).MakeGenericMethod(value.PropertyType).Invoke(this, new object[2] { component2, value });
				}
				gameObject.SetActive(value: true);
				gameObject.transform.SetParent(RowParent, worldPositionStays: false);
				gameObject.transform.Find("Name").GetComponent<Text>().text = item.Key switch
				{
					"TNTExplosionForce" => "TNT 爆炸力度",
					"TNTExplosionRadius" => "TNT 爆炸范围倍率",
					"AlienTNTExplosionForce" => "外星 TNT 爆炸力度",
					"AlienTNTExplosionRadius" => "外星 TNT 爆炸范围倍率",
					"AlienTNTExplosionCoolingTime" => "外星 TNT 冷却时间（秒）",
					"AlienTNTTriggerGun" => "外星 TNT 触发手动枪",
					"BlasterTNTDestroy" => "冲击波 TNT 爆炸后销毁",
					_ => item.Key
				};
				m_settingItems.Add(gameObject);
			}
		}

		private void Reset()
		{
			// Reset() 会把所有子设置（含 CustomBackground）恢复成默认值；Path 变空后
			// 背景系统下一帧就会释放媒体资源，所以这里不需要额外通知。
			UserSettings.Instance.Reset();
			m_backgroundFillApplied = false;
			UserSettings.Save();
		}

		// ============================================================================================
		// 自定义背景分组
		//
		// 分组结构（与 BPSS 的 INCustomBackgroundInterface 一一对应）：
		//   标题
		//   开启背景 / 背景路径 / 视频音量 / 透明度
		//   自定义缩放 / 摄像机自适应 / X、Y 轴拉伸 / X、Y 轴偏移
		//   启用绝对坐标 / 绝对坐标 X、Y / 旋转角度 / Z 轴距离
		//   播放循环 / 播放速度 / 播放、暂停 / 跳转时间 / 播放进度（+ 可拖动进度条）
		//   上传背景 / 清除背景 / 快速填充
		// 值本身全部由 DependencyProxy 双向绑定到 UserSettings.CustomBackground，
		// 所以「设置面板 → 设置对象 → ECS 系统 → 画面」这条链路不需要任何手工同步代码。
		// ============================================================================================

		private void BindCustomBackground()
		{
			CustomBackgroundSettings settings = UserSettings.Instance.CustomBackground;
			if (settings == null)
			{
				return;
			}
			m_backgroundProxy = new DependencyProxy<CustomBackgroundSettings>(settings);
			settings.PropertyChanged += OnBackgroundSettingChanged;

			BuildBackgroundDrawer();

			m_rowParent = m_backgroundDrawer.transform;

			AddBackgroundRow("开启背景", "Enabled");
			AddBackgroundRow("背景路径", "Path");
			AddBackgroundRow("视频音量", "Volume");
			AddBackgroundRow("透明度", "Opacity");
			AddBackgroundRow("自定义缩放", "CustomScale");
			AddBackgroundRow("摄像机自适应", "CameraAdaptive");
			AddBackgroundRow("X 轴拉伸", "ScaleX");
			AddBackgroundRow("Y 轴拉伸", "ScaleY");
			AddBackgroundRow("X 轴偏移", "OffsetX");
			AddBackgroundRow("Y 轴偏移", "OffsetY");
			AddBackgroundRow("启用绝对坐标", "UseAbsolute");
			AddBackgroundRow("绝对坐标 X", "AbsoluteX");
			AddBackgroundRow("绝对坐标 Y", "AbsoluteY");
			AddBackgroundRow("旋转角度（度）", "Rotation");
			AddBackgroundRow("Z 轴距离", "Distance");
			AddBackgroundRow("播放循环", "Loop");
			AddBackgroundRow("播放速度", "PlaybackSpeed");
			AddBackgroundRow("播放/暂停", "Playing");
			AddBackgroundRow("跳转时间（秒）", "SeekTime");
			m_backgroundProgressField = AddReadOnlyRow("播放进度", "00:00.0 / 00:00.0");
			m_backgroundProgressSlider = AddSliderRow("拖动进度");
			if (m_backgroundProgressSlider != null)
			{
				m_backgroundProgressSlider.onValueChanged.AddListener(OnBackgroundProgressDragged);
			}
			BuildBackgroundButtons();

			m_rowParent = null;
			SetBackgroundDrawerOpen(m_backgroundDrawerOpen);
	}

	/// <summary>抽屉默认是否展开。收起时整个自定义背景分组只占一行，不用滚半天。</summary>
	private bool m_backgroundDrawerOpen;

	/// <summary>
	/// 建「自定义背景」抽屉：一行可点的标题（带 ▸/▾ 箭头）+ 一个可折叠的容器。
	/// 容器自带 VerticalLayoutGroup + ContentSizeFitter，展开后高度自动撑开，
	/// 面板 Scroll View 也跟着变长。
	/// </summary>
	private void BuildBackgroundDrawer()
	{
		GameObject header = UnityEngine.Object.Instantiate(m_inputFieldTemplate);
		header.SetActive(value: true);
		header.name = "BackgroundDrawerHeader";
		header.transform.SetParent(m_content.transform, worldPositionStays: false);
		Transform headerValue = header.transform.Find("Value");
		if (headerValue != null)
		{
			headerValue.gameObject.SetActive(value: false);
		}
		Text headerName = header.transform.Find("Name").GetComponent<Text>();
		headerName.text = "自定义背景";
		m_settingItems.Add(header);

		// 整行可点：模板行自己没有 Graphic，所以铺一层透明 Image 当射线目标。
		GameObject hit = new GameObject("DrawerToggle", typeof(RectTransform), typeof(Image));
		RectTransform hitRect = (RectTransform)hit.transform;
		hitRect.SetParent(header.transform, worldPositionStays: false);
		hitRect.anchorMin = Vector2.zero;
		hitRect.anchorMax = Vector2.one;
		hitRect.offsetMin = Vector2.zero;
		hitRect.offsetMax = Vector2.zero;
		Image hitImage = hit.GetComponent<Image>();
		hitImage.color = new Color(1f, 1f, 1f, 0f);
		hitImage.raycastTarget = true;
		Button toggle = hit.AddComponent<Button>();
		toggle.targetGraphic = hitImage;
		toggle.transition = Selectable.Transition.None;
		toggle.onClick.AddListener(ToggleBackgroundDrawer);

		// 箭头：直接克隆 Name 的 Text，字体/字号/颜色都跟面板一致。
		GameObject arrow = UnityEngine.Object.Instantiate(headerName.gameObject);
		arrow.name = "Arrow";
		RectTransform arrowRect = (RectTransform)arrow.transform;
		arrowRect.SetParent(header.transform, worldPositionStays: false);
		arrowRect.anchorMin = new Vector2(1f, 0.5f);
		arrowRect.anchorMax = new Vector2(1f, 0.5f);
		arrowRect.pivot = new Vector2(1f, 0.5f);
		arrowRect.sizeDelta = new Vector2(60f, 60f);
		arrowRect.anchoredPosition = new Vector2(-20f, 0f);
		m_backgroundDrawerArrow = arrow.GetComponent<Text>();
		if (m_backgroundDrawerArrow != null)
		{
			m_backgroundDrawerArrow.text = "▾";
			m_backgroundDrawerArrow.raycastTarget = false;
		}

		GameObject drawer = new GameObject("BackgroundDrawer", typeof(RectTransform));
		RectTransform drawerRect = (RectTransform)drawer.transform;
		drawerRect.SetParent(m_content.transform, worldPositionStays: false);
		drawerRect.anchorMin = new Vector2(0f, 1f);
		drawerRect.anchorMax = new Vector2(0f, 1f);
		drawerRect.pivot = new Vector2(0.5f, 1f);
		drawerRect.sizeDelta = new Vector2(900f, 0f);
		drawerRect.anchoredPosition = Vector2.zero;
		VerticalLayoutGroup layout = drawer.AddComponent<VerticalLayoutGroup>();
		layout.padding = new RectOffset(0, 0, 10, 10);
		layout.spacing = 20f;
		layout.childAlignment = TextAnchor.UpperCenter;
		layout.childControlWidth = true;
		layout.childControlHeight = false;
		layout.childForceExpandWidth = true;
		layout.childForceExpandHeight = false;
		ContentSizeFitter fitter = drawer.AddComponent<ContentSizeFitter>();
		fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
		fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
		m_backgroundDrawer = drawer;
	}

	private void ToggleBackgroundDrawer()
	{
		SetBackgroundDrawerOpen(!m_backgroundDrawerOpen);
	}

	/// <summary>展开/收起自定义背景抽屉。收起只是隐藏容器，行和绑定都还在，不会丢状态。</summary>
	internal void SetBackgroundDrawerOpen(bool open)
	{
		m_backgroundDrawerOpen = open;
		if (m_backgroundDrawer != null)
		{
			m_backgroundDrawer.SetActive(open);
		}
		if (m_backgroundDrawerArrow != null)
		{
			m_backgroundDrawerArrow.text = open ? "▾" : "▸";
		}
	}

	/// <summary>抽屉容器（QA 按名字找行时用）。</summary>
	internal Transform BackgroundDrawer
	{
		get
		{
			return (m_backgroundDrawer != null) ? m_backgroundDrawer.transform : null;
		}
	}

	private void OnBackgroundSettingChanged(object sender, System.ComponentModel.PropertyChangedEventArgs args)
		{
			// 任何一项改动都落盘（UserSettings 以前只有 QA 会存，这里补上真正的保存时机）。
			// 控件与设置值的双向同步由 DependencyProxy 负责，这里只关心要不要存盘。
			m_backgroundDirty = true;
		}

		private void AddSectionHeader(string label)
		{
			GameObject row = UnityEngine.Object.Instantiate(m_inputFieldTemplate);
			row.SetActive(value: true);
			row.transform.SetParent(RowParent, worldPositionStays: false);
			Transform value = row.transform.Find("Value");
			if (value != null)
			{
				value.gameObject.SetActive(value: false);
			}
			Text name = row.transform.Find("Name").GetComponent<Text>();
			name.text = label;
			m_settingItems.Add(row);
		}

		private GameObject AddBackgroundRow(string label, string propertyName)
		{
			DependencyProperty property = m_backgroundProxy.GetProperty(propertyName);
			GameObject row;
			if (property is DependencyProperty<bool> boolProperty)
			{
				row = UnityEngine.Object.Instantiate(m_toggleTemplate);
				row.transform.Find("Value").GetComponent<ToggleSwitch>().Bind(boolProperty);
			}
			else
			{
				row = UnityEngine.Object.Instantiate(m_inputFieldTemplate);
				InputField field = row.transform.Find("Value").GetComponent<InputField>();
				BindInputField(field, property);
			}
			row.SetActive(value: true);
			row.transform.SetParent(RowParent, worldPositionStays: false);
			row.transform.Find("Name").GetComponent<Text>().text = label;
			m_settingItems.Add(row);
			return row;
		}

		private void BindInputField(InputField field, DependencyProperty property)
		{
			typeof(Binding).GetMethod("Bind", new Type[2]
			{
				typeof(InputField),
				typeof(DependencyProperty<>).MakeGenericType(Type.MakeGenericMethodParameter(0))
			}).MakeGenericMethod(property.PropertyType).Invoke(this, new object[2] { field, property });
		}

		private InputField AddReadOnlyRow(string label, string initialText)
		{
			GameObject row = UnityEngine.Object.Instantiate(m_inputFieldTemplate);
			row.SetActive(value: true);
			row.transform.SetParent(RowParent, worldPositionStays: false);
			row.transform.Find("Name").GetComponent<Text>().text = label;
			InputField field = row.transform.Find("Value").GetComponent<InputField>();
			field.readOnly = true;
			field.text = initialText;
			m_settingItems.Add(row);
			return field;
		}

		/// <summary>
		/// 进度条行：模板行的 Value 上已经挂了 InputField，而 uGUI 规定一个 GameObject 只能有一个
		/// Selectable，所以 Slider 必须建在**子物体**上（在同一物体上 AddComponent&lt;Slider&gt; 会失败，
		/// 之前就是这么把后面三个按钮一起搞没的：AddComponent 返回 null → 空引用 → Start 中断）。
		/// </summary>
		private Slider AddSliderRow(string label)
		{
			GameObject row = UnityEngine.Object.Instantiate(m_inputFieldTemplate);
			row.SetActive(value: true);
			row.transform.SetParent(RowParent, worldPositionStays: false);
			row.transform.Find("Name").GetComponent<Text>().text = label;
			m_settingItems.Add(row);
			Transform value = row.transform.Find("Value");
			// 原来的 InputField 只留下壳（文字/占位符都藏起来），真正的交互交给下面的 Slider。
			InputField field = value.GetComponent<InputField>();
			if (field != null)
			{
				field.enabled = false;
			}
			Transform text = value.Find("Text");
			if (text != null)
			{
				text.gameObject.SetActive(value: false);
			}
			Transform placeholder = value.Find("Placeholder");
			if (placeholder != null)
			{
				placeholder.gameObject.SetActive(value: false);
			}

			GameObject sliderObject = new GameObject("Slider", typeof(RectTransform), typeof(Image));
			RectTransform sliderRect = (RectTransform)sliderObject.transform;
			sliderRect.SetParent(value, worldPositionStays: false);
			sliderRect.anchorMin = Vector2.zero;
			sliderRect.anchorMax = Vector2.one;
			sliderRect.offsetMin = Vector2.zero;
			sliderRect.offsetMax = Vector2.zero;
			Image background = sliderObject.GetComponent<Image>();
			background.color = new Color(1f, 1f, 1f, 0.16f);
			background.raycastTarget = true;

			GameObject fillArea = new GameObject("Fill Area", typeof(RectTransform));
			RectTransform fillAreaRect = (RectTransform)fillArea.transform;
			fillAreaRect.SetParent(sliderRect, worldPositionStays: false);
			fillAreaRect.anchorMin = Vector2.zero;
			fillAreaRect.anchorMax = Vector2.one;
			fillAreaRect.offsetMin = new Vector2(4f, 4f);
			fillAreaRect.offsetMax = new Vector2(-4f, -4f);
			GameObject fill = new GameObject("Fill", typeof(RectTransform), typeof(Image));
			RectTransform fillRect = (RectTransform)fill.transform;
			fillRect.SetParent(fillAreaRect, worldPositionStays: false);
			fillRect.anchorMin = Vector2.zero;
			fillRect.anchorMax = Vector2.one;
			fillRect.offsetMin = Vector2.zero;
			fillRect.offsetMax = Vector2.zero;
			Image fillImage = fill.GetComponent<Image>();
			fillImage.color = new Color(0.45f, 0.75f, 1f, 0.9f);
			fillImage.raycastTarget = false;

			Slider slider = sliderObject.AddComponent<Slider>();
			slider.fillRect = fillRect;
			slider.targetGraphic = background;
			slider.direction = Slider.Direction.LeftToRight;
			slider.transition = Selectable.Transition.None;
			slider.minValue = 0f;
			slider.maxValue = 1f;
			slider.wholeNumbers = false;
			slider.value = 0f;
			return slider;
		}

		private void BuildBackgroundButtons()
		{
			AddButton("上传背景", UploadBackground);
			AddButton("清除背景", ClearBackground);
			AddButton("快速填充", FillBackgroundShortcut);
			GameObject statusRow = UnityEngine.Object.Instantiate(m_inputFieldTemplate);
			statusRow.SetActive(value: true);
			statusRow.transform.SetParent(RowParent, worldPositionStays: false);
			statusRow.transform.Find("Name").GetComponent<Text>().text = "背景状态";
			Transform statusValue = statusRow.transform.Find("Value");
			InputField statusField = statusValue.GetComponent<InputField>();
			if (statusField != null)
			{
				statusField.readOnly = true;
			}
			m_backgroundStatusText = statusValue.GetComponentInChildren<Text>();
			if (m_backgroundStatusText != null)
			{
				m_backgroundStatusText.text = "未加载";
			}
			m_settingItems.Add(statusRow);
		}

		/// <summary>克隆 Bottom/Reset 按钮当一个分组内的操作按钮（尺寸与列表行对齐）。</summary>
		private Button AddButton(string label, UnityEngine.Events.UnityAction action)
		{
			GameObject clone = UnityEngine.Object.Instantiate(m_resetButton.gameObject);
			clone.name = "Background" + label;
			clone.SetActive(value: true);
			clone.transform.SetParent(RowParent, worldPositionStays: false);
			RectTransform rectTransform = clone.transform as RectTransform;
			if (rectTransform != null)
			{
				rectTransform.anchorMin = new Vector2(0f, 1f);
				rectTransform.anchorMax = new Vector2(0f, 1f);
				rectTransform.pivot = new Vector2(0.5f, 0.5f);
				rectTransform.sizeDelta = new Vector2(900f, 90f);
				rectTransform.anchoredPosition = new Vector2(791.5f, -105f);
			}
			Button button = clone.GetComponent<Button>();
			button.onClick.RemoveAllListeners();
			button.onClick.AddListener(action);
			Text caption = clone.transform.Find("Text")?.GetComponent<Text>();
			if (caption != null)
			{
				caption.text = label;
			}
			m_settingItems.Add(clone);
			return button;
		}

		/// <summary>上传：选文件 → 图片复制进持久化目录（视频只记路径）→ 写进设置，系统会自动重载媒体。</summary>
		private void UploadBackground()
		{
			CustomBackgroundSettings settings = UserSettings.Instance.CustomBackground;
			if (settings == null)
			{
				return;
			}
			string picked = CustomBackgroundMediaLoader.PickMediaFile(Application.dataPath);
			if (string.IsNullOrEmpty(picked) || !File.Exists(picked))
			{
				return;
			}
			// 只需把路径写进设置；真正解码/建 VideoPlayer 的是渲染系统（它按路径指纹重载）。
			string stored = picked;
			if (!CustomBackgroundMediaLoader.IsVideoFile(picked))
			{
				try
				{
					Directory.CreateDirectory(CustomBackgroundMediaLoader.StorageDirectory);
					File.Copy(picked, CustomBackgroundMediaLoader.UploadedImagePath, overwrite: true);
					stored = CustomBackgroundMediaLoader.UploadedImagePath;
				}
				catch (Exception ex)
				{
					Debug.LogWarning("[BpsimBackground] 复制图片失败，改为直接引用原路径: " + ex.Message);
				}
			}
			settings.Path = stored;
			settings.Enabled = true;
			settings.SeekTime = 0f;
			settings.Playing = true;
			UserSettings.Save();
		}

		/// <summary>清除：释放资源、开关关闭、所有参数回到默认值（与 BPSS 的 ClearBackground 一致）。</summary>
		private void ClearBackground()
		{
			CustomBackgroundSettings settings = UserSettings.Instance.CustomBackground;
			if (settings == null)
			{
				return;
			}
			settings.Path = string.Empty;
			settings.Enabled = false;
			settings.CustomScale = true;
			settings.CameraAdaptive = true;
			settings.ScaleX = 1f;
			settings.ScaleY = 1f;
			settings.OffsetX = 0f;
			settings.OffsetY = 0f;
			settings.UseAbsolute = false;
			settings.AbsoluteX = 0f;
			settings.AbsoluteY = 0f;
			settings.Rotation = 0f;
			settings.Distance = 4f;
			settings.Opacity = 1f;
			settings.Volume = 1f;
			settings.PlaybackSpeed = 1f;
			settings.SeekTime = 0f;
			settings.Playing = true;
			settings.Loop = true;
			m_backgroundFillApplied = false;
			UserSettings.Save();
		}

		/// <summary>
		/// 快速填充（开关式，语义照 BPSS 的 FillShortcut）：
		/// 第一次按下 → 关闭摄像机自适应 + 启用绝对坐标，并把背景「当前」的渲染尺寸/位置写进
		/// X/Y 拉伸与绝对坐标；再按一次 → 恢复摄像机自适应并还原拉伸值。
		/// </summary>
		private void FillBackgroundShortcut()
		{
			CustomBackgroundSettings settings = UserSettings.Instance.CustomBackground;
			if (settings == null)
			{
				return;
			}
			if (!CustomBackgroundSystem.TryGetSurface(out Vector3 position, out Vector2 size, out float _))
			{
				Debug.LogWarning("[BpsimBackground] 背景当前不可见，无法快速填充（先开启背景并加载一张图片/视频）。");
				return;
			}
			m_backgroundFillApplied = !m_backgroundFillApplied;
			if (m_backgroundFillApplied)
			{
				m_backgroundStashedScaleX = settings.ScaleX;
				m_backgroundStashedScaleY = settings.ScaleY;
				settings.CameraAdaptive = false;
				settings.UseAbsolute = true;
				settings.ScaleX = size.x;
				settings.ScaleY = size.y;
				settings.AbsoluteX = position.x;
				settings.AbsoluteY = position.y;
			}
			else
			{
				settings.CameraAdaptive = true;
				settings.UseAbsolute = false;
				settings.ScaleX = m_backgroundStashedScaleX;
				settings.ScaleY = m_backgroundStashedScaleY;
			}
			UserSettings.Save();
		}

		private void OnBackgroundProgressDragged(float normalized)
		{
			CustomBackgroundSettings settings = UserSettings.Instance.CustomBackground;
			if (settings == null)
			{
				return;
			}
			if (!CustomBackgroundSystem.TryGetPlayback(out float _, out float duration) || duration <= 0f)
			{
				return;
			}
			settings.SeekTime = Mathf.Clamp01(normalized) * duration;
			settings.Playing = true;
		}

		/// <summary>刷新进度显示 / 状态文字（值绑定本身由 DependencyProxy 负责，这里只刷非绑定项）。</summary>
		private void UpdateBackgroundPlayback()
		{
			string status = BuildBackgroundStatus();
			if (m_backgroundStatusText != null && m_backgroundStatusText.text != status)
			{
				m_backgroundStatusText.text = status;
			}
			bool hasPlayback = CustomBackgroundSystem.TryGetPlayback(out float time, out float duration);
			if (m_backgroundProgressField != null)
			{
				string text = hasPlayback ? (FormatBackgroundTime(time) + " / " + FormatBackgroundTime(duration)) : "00:00.0 / 00:00.0";
				if (m_backgroundProgressField.text != text)
				{
					m_backgroundProgressField.text = text;
				}
			}
			if (m_backgroundProgressSlider != null && hasPlayback && duration > 0f)
			{
				// SetValueWithoutNotify 不会触发 onValueChanged，所以拖动 → seek → 轮询回写不会成环。
				m_backgroundProgressSlider.SetValueWithoutNotify(Mathf.Clamp01(time / duration));
			}
		}

		private static string BuildBackgroundStatus()
		{
			CustomBackgroundSettings settings = UserSettings.Instance?.CustomBackground;
			if (settings == null || string.IsNullOrEmpty(settings.Path))
			{
				return "未加载";
			}
			if (!File.Exists(settings.Path))
			{
				return "文件不存在";
			}
			string kind = CustomBackgroundMediaLoader.IsVideoFile(settings.Path) ? "视频" : "图片";
			return settings.Enabled ? (kind + " · 显示中") : (kind + " · 已关闭");
		}

		private static string FormatBackgroundTime(float totalSeconds)
		{
			if (totalSeconds < 0f)
			{
				totalSeconds = 0f;
			}
			int minutes = (int)(totalSeconds / 60f);
			float seconds = totalSeconds - minutes * 60f;
			return minutes.ToString("D2", CultureInfo.InvariantCulture) + ":" + seconds.ToString("00.0", CultureInfo.InvariantCulture);
		}

		private void OnDestroy()
		{
			m_simulationSettingsProxy.Source.Unregister();
			m_simulationSettingsProxy.Unregister();
			if (m_interactionSettingsProxy != null)
			{
				m_interactionSettingsProxy.Source.Unregister();
				m_interactionSettingsProxy.Unregister();
			}
			if (m_backgroundProxy != null)
			{
				CustomBackgroundSettings settings = m_backgroundProxy.Source;
				if (settings != null)
				{
					settings.PropertyChanged -= OnBackgroundSettingChanged;
				}
				m_backgroundProxy.Unregister();
			}
			if (m_debugConsoleProxy != null)
			{
				// 只解绑这一行控件与属性的双向绑定；不能 Unregister() 整个 DebugConsoleSettings，
				// 那会顺手清掉 DebugConsoleHost 的 PropertyChanged 订阅，之后开关就再也不生效了。
				m_debugConsoleProxy.Unregister();
			}
			UserSettings.Save();
		}
	}
}
