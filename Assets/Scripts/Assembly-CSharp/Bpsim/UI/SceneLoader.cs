using System;
using System.Collections.Generic;
using System.IO;
using Bpsim.Levels;
using Bpsim.Parts;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;

namespace Bpsim.UI
{
	internal class SceneLoader : InterfaceBase
	{
		[SerializeField]
		private Text m_header;

		[SerializeField]
		private InputField m_path;

		[SerializeField]
		private List<Button> m_browseButtons;

		[SerializeField]
		private Dropdown m_loadFormat;

		[SerializeField]
		private Button m_applyButton;

		[SerializeField]
		private Button m_cancelButton;

		[SerializeField]
		private Text m_state;

		private bool m_dirty;
		private bool m_browsing;
		private FileSelection m_selection;
		private string m_selectionText;

		private SceneLoaderMode m_mode;

		private string m_bpsimDataPath;

		private string m_innovationDataPath;

		// 合并后的下半区：与文件读写控件共用同一个 SceneLoader 面板。
		private RectTransform m_visualSection;
		private Text m_visualSceneName;
		private Text m_visualSceneMeta;
		private Text m_visualEmpty;
		private RawImage m_visualPreview;
		private Texture2D m_visualTexture;
		private string m_visualSignature;
		private float m_visualNextRefresh;

		public SceneLoaderMode Mode
		{
			get
			{
				return m_mode;
			}
			set
			{
				if (m_mode != value)
				{
					m_mode = value;
					SetDefaultFormat();
					SetDirty();
				}
			}
		}

		public void SetDirty()
		{
			m_dirty = true;
		}

		private void Awake()
		{
			m_browseButtons[0].onClick.AddListener(Browse);
			m_browseButtons[1].onClick.AddListener(BrowseBpsimData);
			m_browseButtons[2].onClick.AddListener(BrowseInnovationData);
			m_applyButton.onClick.AddListener(Apply);
			m_cancelButton.onClick.AddListener(Cancel);
			EnsureBpirFormatOption();
			SetDefaultFormat();
			string directoryName = GetDefaultDataDirectory();
			m_bpsimDataPath = Path.Combine(directoryName, "bput");
			m_innovationDataPath = Path.Combine(directoryName, "Rovio");
			BuildVisualSection();
			RefreshVisualSection(force: true);
		}

		/// <summary>
		/// 存档格式下拉框（prefab 里只有 4 项）。BPIR 与自动识别都是后加的，为了不改动
		/// prefab 的 yaml（手改容易破坏序列化），在运行期补上；重复打开界面也不会重复追加。
		/// </summary>
		private const string BpirFormatLabel = "BPIR 存档 (原版 Innovation Rebuild)";

		private const string AutoFormatLabel = "自动识别 (按文件内容)";

		private void SetDefaultFormat()
		{
			if (m_loadFormat == null) return;
			int automatic = m_loadFormat.options.FindIndex(option => option?.text == AutoFormatLabel);
			m_loadFormat.SetValueWithoutNotify(m_mode == SceneLoaderMode.Read && automatic >= 0 ? automatic : 0);
		}

		private void EnsureBpirFormatOption()
		{
			if (m_loadFormat == null)
			{
				return;
			}
			EnsureFormatOption(BpirFormatLabel);
			EnsureFormatOption(AutoFormatLabel);
		}

		private void EnsureFormatOption(string label)
		{
			foreach (Dropdown.OptionData option in m_loadFormat.options)
			{
				if (option != null && option.text == label)
				{
					return;
				}
			}
			m_loadFormat.options.Add(new Dropdown.OptionData(label));
			m_loadFormat.RefreshShownValue();
		}

		// The data folders are looked for two levels above persistentDataPath
		// (%USERPROFILE%\AppData\LocalLow\<company> on Windows).  That layout does not exist on
		// Android, where persistentDataPath is /storage/emulated/0/Android/data/<pkg>/files and
		// the old string manipulation produced an empty parent ("\bput"), which FileAPI then
		// rejected with "ArgumentException: Invalid path".  Fall back to persistentDataPath so
		// the picker always gets a directory that exists.
		private static string GetDefaultDataDirectory()
		{
			string path = Application.persistentDataPath;
			for (int i = 0; i < 2; i++)
			{
				DirectoryInfo parent = Directory.GetParent(path);
				if (parent == null)
				{
					return Application.persistentDataPath;
				}
				path = parent.FullName;
			}
			return string.IsNullOrEmpty(path) ? Application.persistentDataPath : path;
		}

		private void Update()
		{
			if (m_dirty)
			{
				m_dirty = false;
				switch (m_mode)
				{
				case SceneLoaderMode.Read:
					m_header.text = "加载场景";
					m_applyButton.transform.Find("Text").GetComponent<Text>().text = "加载";
					break;
				case SceneLoaderMode.Write:
					m_header.text = "保存场景";
					m_applyButton.transform.Find("Text").GetComponent<Text>().text = "保存";
					break;
				}
			}
			if (m_visualSection != null && Time.unscaledTime >= m_visualNextRefresh)
			{
				m_visualNextRefresh = Time.unscaledTime + 0.5f;
				RefreshVisualSection(force: false);
			}
		}

		private void BuildVisualSection()
		{
			Transform panel = transform.Find("Panel");
			if (panel == null)
			{
				return;
			}

			GameObject sectionObject = new GameObject("SceneVisualSection", typeof(RectTransform), typeof(Image));
			m_visualSection = (RectTransform)sectionObject.transform;
			m_visualSection.SetParent(panel, worldPositionStays: false);
			m_visualSection.anchorMin = m_visualSection.anchorMax = new Vector2(0f, 1f);
			m_visualSection.pivot = new Vector2(0f, 1f);
			m_visualSection.anchoredPosition = new Vector2(120f, -455f);
			// Leave the prefab's state/cancel row visible below the card. The upper
			// loader controls end at -450 and the status row starts at -660.
			m_visualSection.sizeDelta = new Vector2(1360f, 190f);
			Image sectionImage = sectionObject.GetComponent<Image>();
			sectionImage.color = new Color(0.09f, 0.09f, 0.09f, 1f);
			sectionImage.raycastTarget = true;

			CreateVisualText(m_visualSection, "SceneVisualHeader", "场景存档", 22, TextAnchor.MiddleLeft,
				new Vector2(24f, -8f), new Vector2(300f, 42f), UiTheme.Current.Text);
			m_visualEmpty = CreateVisualText(m_visualSection, "SceneVisualEmpty", "暂无当前场景", 16, TextAnchor.MiddleCenter,
				new Vector2(0f, -48f), new Vector2(1360f, 130f), UiTheme.Current.TextMuted);

			GameObject cardObject = new GameObject("CurrentSceneCard", typeof(RectTransform), typeof(Image));
			RectTransform card = (RectTransform)cardObject.transform;
			card.SetParent(m_visualSection, worldPositionStays: false);
			card.anchorMin = card.anchorMax = new Vector2(0f, 1f);
			card.pivot = new Vector2(0f, 1f);
			card.anchoredPosition = new Vector2(10f, -52f);
			card.sizeDelta = new Vector2(1340f, 132f);
			Image cardImage = cardObject.GetComponent<Image>();
			cardImage.color = new Color(0.17f, 0.19f, 0.22f, 1f);
			cardImage.raycastTarget = true;

			GameObject previewObject = new GameObject("Preview", typeof(RectTransform), typeof(RawImage));
			RectTransform previewRect = (RectTransform)previewObject.transform;
			previewRect.SetParent(card, worldPositionStays: false);
			previewRect.anchorMin = previewRect.anchorMax = new Vector2(0f, 0.5f);
			previewRect.pivot = new Vector2(0f, 0.5f);
			previewRect.anchoredPosition = new Vector2(18f, 0f);
			previewRect.sizeDelta = new Vector2(168f, 94f);
			m_visualPreview = previewObject.GetComponent<RawImage>();
			m_visualPreview.color = Color.white;
			m_visualPreview.raycastTarget = false;

			m_visualSceneName = CreateVisualText(card, "Name", "当前场景", 20, TextAnchor.MiddleLeft,
				new Vector2(210f, 34f), new Vector2(560f, 34f), UiTheme.Current.Text);
			m_visualSceneMeta = CreateVisualText(card, "Meta", string.Empty, 14, TextAnchor.MiddleLeft,
				new Vector2(210f, -6f), new Vector2(560f, 28f), UiTheme.Current.TextMuted);

			CreateVisualButton(card, "加载", new Vector2(1080f, 0f), new Vector2(100f, 44f),
				() => SelectModeAndBrowse(SceneLoaderMode.Read));
			CreateVisualButton(card, "保存", new Vector2(1190f, 0f), new Vector2(100f, 44f),
				() => SelectModeAndBrowse(SceneLoaderMode.Write));
		}

		private Text CreateVisualText(Transform parent, string name, string value, int size, TextAnchor alignment,
			Vector2 position, Vector2 dimensions, Color color)
		{
			GameObject textObject = new GameObject(name, typeof(RectTransform), typeof(Text));
			RectTransform rect = (RectTransform)textObject.transform;
			rect.SetParent(parent, worldPositionStays: false);
			rect.anchorMin = rect.anchorMax = new Vector2(0f, 1f);
			rect.pivot = new Vector2(0f, 1f);
			rect.anchoredPosition = position;
			rect.sizeDelta = dimensions;
			Text text = textObject.GetComponent<Text>();
			text.font = m_header != null ? m_header.font : Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
			text.fontSize = size;
			text.color = color;
			text.alignment = alignment;
			text.text = value;
			text.raycastTarget = false;
			text.horizontalOverflow = HorizontalWrapMode.Wrap;
			text.verticalOverflow = VerticalWrapMode.Overflow;
			return text;
		}

		private void CreateVisualButton(Transform parent, string label, Vector2 position, Vector2 dimensions, UnityEngine.Events.UnityAction action)
		{
			GameObject buttonObject = new GameObject("Button_" + label, typeof(RectTransform), typeof(Image), typeof(Button));
			RectTransform rect = (RectTransform)buttonObject.transform;
			rect.SetParent(parent, worldPositionStays: false);
			rect.anchorMin = rect.anchorMax = new Vector2(0f, 0.5f);
			rect.pivot = new Vector2(0f, 0.5f);
			rect.anchoredPosition = position;
			rect.sizeDelta = dimensions;
			Image image = buttonObject.GetComponent<Image>();
			image.color = new Color(0f, 0.196875f, 0.4f, 1f);
			Button button = buttonObject.GetComponent<Button>();
			button.targetGraphic = image;
			ColorBlock colors = button.colors;
			colors.highlightedColor = new Color(1.15f, 1.15f, 1.15f, 1f);
			colors.pressedColor = new Color(0.8f, 0.8f, 0.8f, 1f);
			button.colors = colors;
			button.onClick.AddListener(action);
			Text text = CreateVisualText(buttonObject.transform, "Text", label, 16, TextAnchor.MiddleCenter,
				Vector2.zero, dimensions, Color.white);
			text.rectTransform.anchorMin = Vector2.zero;
			text.rectTransform.anchorMax = Vector2.one;
			text.rectTransform.pivot = new Vector2(0.5f, 0.5f);
			text.rectTransform.anchoredPosition = Vector2.zero;
			text.rectTransform.sizeDelta = Vector2.zero;
		}

		private void SelectModeAndBrowse(SceneLoaderMode mode)
		{
			Mode = mode;
			Browse();
		}

		private void RefreshVisualSection(bool force)
		{
			if (m_visualSection == null || PartManager.Instance == null)
			{
				return;
			}
			PartScene scene = PartManager.Instance.HasActiveScene() ? PartManager.Instance.ActiveScene : null;
			string sceneName = scene == null ? string.Empty : scene.SceneID + "|" + scene.SceneName + "|" + scene.Unmanaged.Parts.Length;
			string signature = sceneName + "|" + LevelSession.ScenePath;
			if (!force && signature == m_visualSignature)
			{
				return;
			}
			m_visualSignature = signature;
			bool hasScene = scene != null;
			if (m_visualEmpty != null) m_visualEmpty.gameObject.SetActive(!hasScene);
			if (m_visualSceneName != null) m_visualSceneName.gameObject.SetActive(hasScene);
			if (m_visualSceneMeta != null) m_visualSceneMeta.gameObject.SetActive(hasScene);
			if (m_visualPreview != null) m_visualPreview.gameObject.SetActive(hasScene);
			Transform card = m_visualSection.Find("CurrentSceneCard");
			if (card != null) card.gameObject.SetActive(hasScene);
			if (!hasScene)
			{
				ReleaseVisualTexture();
				return;
			}

			string name = string.IsNullOrEmpty(scene.SceneName) ? "当前场景" : scene.SceneName;
			if (m_visualSceneName != null) m_visualSceneName.text = name;
			string path = LevelSession.ScenePath;
			string file = string.IsNullOrEmpty(path) ? "（未保存）" : Path.GetFileName(path);
			if (m_visualSceneMeta != null) m_visualSceneMeta.text = scene.Unmanaged.Parts.Length + " 部件 · " + file;
			if (m_visualPreview != null)
			{
				ReleaseVisualTexture();
				if (SceneCamera.Instance != null && SceneCamera.Instance.Camera != null)
				{
					try
					{
						m_visualTexture = Screenshot.Capture(SceneCamera.Instance.Camera, 336, 188);
						m_visualPreview.texture = m_visualTexture;
					}
					catch (Exception ex)
					{
						Debug.LogException(ex);
					}
				}
			}
		}

		private void ReleaseVisualTexture()
		{
			if (m_visualTexture != null)
			{
				UnityEngine.Object.Destroy(m_visualTexture);
				m_visualTexture = null;
			}
			if (m_visualPreview != null)
			{
				m_visualPreview.texture = null;
			}
		}

		private void Browse()
		{
			BrowseAsync(null).Forget();
		}

		private void BrowseBpsimData()
		{
			BrowseAsync(m_bpsimDataPath).Forget();
		}

		private void BrowseInnovationData()
		{
			BrowseAsync(m_innovationDataPath).Forget();
		}

		private async UniTask BrowseAsync(string initialDirectory)
		{
			if (m_browsing) return;
#if UNITY_ANDROID && !UNITY_EDITOR
			if (m_mode == SceneLoaderMode.Write)
			{
				await ExportScene(SelectedFormat);
				return;
			}
#endif
			m_browsing = true;
			try
			{
				if (m_mode == SceneLoaderMode.Write)
				{
					string path = await FileAPI.CreateFile(initialDirectory);
					if (this != null && !string.IsNullOrEmpty(path)) m_path.text = path;
					return;
				}
				FileSelection selected = await FileAPI.OpenFile(initialDirectory, keepForSession: true);
				if (selected == null) return;
				if (this == null)
				{
					selected.Dispose();
					return;
				}
				m_selection?.Dispose();
				m_selection = selected;
#if UNITY_ANDROID && !UNITY_EDITOR
				m_selectionText = Path.GetFileName(selected.Path);
#else
				m_selectionText = selected.Path;
#endif
				m_path.text = m_selectionText;
				m_state.text = string.Empty;
			}
			catch (Exception ex)
			{
				Debug.LogException(ex);
				if (this != null) m_state.text = "无法选择文件：" + ex.Message;
			}
			finally { m_browsing = false; }
		}

		private SchematicsFormat SelectedFormat => m_loadFormat.value switch
		{
			0 => SchematicsFormat.Csv,
			1 => SchematicsFormat.Json,
			2 => SchematicsFormat.EncryptedXml,
			3 => SchematicsFormat.Xml,
			4 => SchematicsFormat.BpirCsv,
			5 => SchematicsFormat.Auto,
			_ => SchematicsFormat.None,
		};

		private void Apply()
		{
			if (m_browsing) return;
			string text = m_path.text;
			bool hasSelection = m_selection != null && text == m_selectionText;
			SchematicsFormat format = SelectedFormat;
			// 保存必须先落到具体格式：Auto 只有内容可嗅探，写盘时没有内容可看。
			// 这里拦住并留在对话框里，比让 CreateLoader 抛 InvalidOperationException 友好。
			if (m_mode == SceneLoaderMode.Write && format == SchematicsFormat.Auto)
			{
				m_state.text = "<color=#FFD080>保存需要先选一个具体格式（自动识别只用于读取）</color>";
				return;
			}
#if UNITY_ANDROID && !UNITY_EDITOR
			if (m_mode == SceneLoaderMode.Write && (string.IsNullOrWhiteSpace(text) || hasSelection))
			{
				ExportScene(format).Forget();
				return;
			}
#endif
			Exception ex = null;
			try
			{
				switch (m_mode)
				{
				case SceneLoaderMode.Read:
					LoadFromPath(hasSelection ? m_selection.Path : text, format);
					if (hasSelection) m_selection.Keep();
					break;
				case SceneLoaderMode.Write:
					Save(text, format);
					break;
				}
			}
			catch (Exception ex2)
			{
				Debug.LogException(ex2);
				ex = ex2;
			}
			if (ex == null)
			{
				m_state.text = string.Empty;
				Close();
			}
			else
			{
				m_state.text = $"<color=#FF8080>ERROR: {ex.GetType()}</color>";
			}
		}

#if UNITY_ANDROID && !UNITY_EDITOR
		private async UniTask ExportScene(SchematicsFormat format)
		{
			if (!PartManager.Instance.HasActiveScene())
			{
				m_state.text = "没有可保存的场景。";
				return;
			}
			string extension;
			string mimeType;
			switch (format)
			{
				case SchematicsFormat.Csv:
				case SchematicsFormat.BpirCsv:
					extension = ".csv";
					mimeType = "text/csv";
					break;
				case SchematicsFormat.Json:
					extension = ".json";
					mimeType = "application/json";
					break;
				case SchematicsFormat.Xml:
					extension = ".xml";
					mimeType = "text/xml";
					break;
				case SchematicsFormat.EncryptedXml:
					extension = ".bytes";
					mimeType = "application/octet-stream";
					break;
				default:
					m_state.text = "保存需要先选一个具体格式。";
					return;
			}
			string name = PartManager.Instance.ActiveScene.SceneName.Replace('/', '_').Replace('\\', '_');
			if (string.IsNullOrWhiteSpace(name)) name = "Bpsim-Scene";
			m_browsing = true;
			try
			{
				if (await FileAPI.ExportFile(name + extension, mimeType, path => Save(path, format)) && this != null) Close();
			}
			catch (Exception ex)
			{
				Debug.LogException(ex);
				if (this != null) m_state.text = "保存失败：" + ex.Message;
			}
			finally { m_browsing = false; }
		}
#endif

		/// <summary>
		/// Loads <paramref name="path"/> into a new scene and records it as the session's
		/// scene, so the next launch can put it back. A blank path asks for an empty scene
		/// and forgets the recorded one, exactly like the dialog's blank input.
		/// </summary>
		internal static void LoadFromPath(string path, SchematicsFormat format)
		{
			if (string.IsNullOrEmpty(path))
			{
				// 「加载空场景」= taffy 式超平坦：白底 + 无限灰地面 + 运动学物理地面。
				PartManager.Instance.LoadEmptyScene("Empty", flatGround: true);
				LevelSession.ClearScene();
				return;
			}
			string fileNameWithoutExtension = Path.GetFileNameWithoutExtension(path);
			SchematicsFormat used;
			try
			{
				used = LoadFromFile(path, fileNameWithoutExtension, format);
			}
			catch (Exception) when (format != SchematicsFormat.Auto)
			{
				// 显式格式读不动时再按内容嗅探重试一次：存档文件通常没有扩展名，
				// 用户选错格式是常态，重试比直接把 FormatException 弹给玩家有用。
				used = LoadFromFile(path, fileNameWithoutExtension, SchematicsFormat.Auto);
			}
			LevelSession.RecordScene(path, used);
		}

		private static SchematicsFormat LoadFromFile(string path, string name, SchematicsFormat format)
		{
			using Stream stream = FileAPI.ReadFileAsStream(path);
			return PartManager.Instance.LoadScene(stream, name, format);
		}

		private void Save(string path, SchematicsFormat format)
		{
			if (PartManager.Instance.HasActiveScene())
			{
				using (FileStream stream = new FileStream(path, FileMode.Create, FileAccess.Write))
				{
					int sceneID = PartManager.Instance.ActiveScene.SceneID;
					PartManager.Instance.SaveScene(stream, sceneID, format);
				}
			}
		}

		private void Cancel()
		{
			Close();
		}

		private new void Close()
		{
			UnityEngine.Object.Destroy(base.gameObject);
		}

		private void OnDestroy()
		{
			m_selection?.Dispose();
			ReleaseVisualTexture();
		}
	}
}
