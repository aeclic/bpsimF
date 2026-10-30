using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace Bpsim.Levels
{
	/// <summary>
	/// Runtime-built picker for the BPLE levels that <c>Tools/BPSim/export_levels.py</c>
	/// baked into <c>StreamingAssets/BpsimLevels</c>. The default presets are exactly the
	/// levels BPLE itself ships, grouped by episode.
	/// </summary>
	/// <remarks>
	/// The panel is built in code instead of from a prefab so that it needs no new asset
	/// references and cannot conflict with the hand-authored <c>UserInterface.prefab</c>.
	/// It lives on its own overlay canvas, above the game UI.
	/// The level grid is a square-tile picker modelled on the BPLE level-select screen:
	/// flat single-colour green squares with a dark outline and a bold white number in
	/// the middle (no stars), scrolling vertically when an episode has more levels than fit.
	/// </remarks>
	internal sealed class LevelMenu : MonoBehaviour
	{
		private const int Columns = 8;

		/// <summary>Square level tile edge length.</summary>
		private const float TileSize = 108f;

		private const float TileStepX = 128f;

		private const float TileStepY = 128f;

		/// <summary>Thickness of the dark outline drawn behind each tile.</summary>
		private const float TileOutline = 4f;

		private const float EpisodeButtonWidth = 236f;

		private const float EpisodeButtonHeight = 38f;

		private const float EpisodeButtonStep = 42f;

		private static readonly Color PanelColor = new Color(0.043f, 0.055f, 0.075f, 0.97f);

		private static readonly Color ButtonColor = new Color(0f, 0.196f, 0.4f, 1f);

		private static readonly Color ButtonActiveColor = new Color(0f, 0.482f, 0.721f, 1f);

		private static readonly Color TextColor = new Color(0.9f, 0.94f, 1f, 1f);

		// Tile palette sampled from the BPLE level-select reference: the tiles there are a
		// flat green (#70C030) with a thin dark-green edge — no gradient.
		private static readonly Color TileFaceColor = new Color32(112, 192, 48, 255);

		private static readonly Color TileOutlineColor = new Color32(58, 84, 44, 255);

		private static readonly Color TileNumberColor = Color.white;

		private Font m_font;

		private RectTransform m_panel;

		private RectTransform m_episodeList;

		private RectTransform m_levelGrid;

		private RectTransform m_levelContent;

		private ScrollRect m_levelScroll;

		private Text m_status;

		private Button m_toggleButton;

		private readonly List<Button> m_episodeButtons = new List<Button>();

		private readonly List<RectTransform> m_levelTiles = new List<RectTransform>();

		private string m_episode;

		public static LevelMenu Create(Transform parent)
		{
			GameObject gameObject = new GameObject("BpsimLevelMenu");
			gameObject.transform.SetParent(parent, false);
			LevelMenu menu = gameObject.AddComponent<LevelMenu>();
			menu.Build();
			return menu;
		}

		private void Build()
		{
			m_font = ResolveFont();
			Canvas canvas = base.gameObject.AddComponent<Canvas>();
			canvas.renderMode = RenderMode.ScreenSpaceOverlay;
			canvas.sortingOrder = 32000;
			CanvasScaler scaler = base.gameObject.AddComponent<CanvasScaler>();
			scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
			scaler.referenceResolution = new Vector2(1920f, 1080f);
			scaler.matchWidthOrHeight = 0.5f;
			base.gameObject.AddComponent<GraphicRaycaster>();
			CreateToggleButton(base.transform);
			CreatePanel(base.transform);
			m_panel.gameObject.SetActive(false);
			RefreshStatus();
		}

		private Font ResolveFont()
		{
			Font font = TryLoadFont("SourceHanSansCN-Regular");
			if (font != null)
			{
				return font;
			}
			font = TryLoadFont("SourceHanSansCN-Bold");
			if (font != null)
			{
				return font;
			}
			font = TryLoadFont("DefaultFont-Regular");
			if (font != null)
			{
				return font;
			}
			return Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
		}

		private static Font TryLoadFont(string name)
		{
			try
			{
				return CoreManager.Instance != null ? CoreManager.Instance.Resources.LoadAsset<Font>(name) : null;
			}
			catch (Exception)
			{
				return null;
			}
		}

		private void CreateToggleButton(Transform parent)
		{
			m_toggleButton = CreateButton(parent, "LevelMenuToggle", "关卡", new Vector2(-110f, -46f), new Vector2(150f, 60f), new Vector2(1f, 1f), Toggle);
			m_toggleButton.GetComponent<Image>().color = ButtonColor;
		}

		private void CreatePanel(Transform parent)
		{
			m_panel = CreateRect(parent, "LevelMenuPanel", new Vector2(0f, 0f), new Vector2(1480f, 880f), new Vector2(0.5f, 0.5f));
			Image background = m_panel.gameObject.AddComponent<Image>();
			background.color = PanelColor;
			background.raycastTarget = true;
			CreateLabel(m_panel, "Title", "导入关卡 · BPLE 内置关卡", new Vector2(0f, 400f), new Vector2(1200f, 46f), 30, TextAnchor.MiddleLeft);
			Button close = CreateButton(m_panel, "Close", "关闭", new Vector2(660f, 400f), new Vector2(120f, 46f), new Vector2(0.5f, 0.5f), delegate
			{
				m_panel.gameObject.SetActive(false);
			});
			close.GetComponent<Image>().color = new Color(0.32f, 0.12f, 0.12f, 1f);
			m_episodeList = CreateRect(m_panel, "Episodes", new Vector2(-590f, 0f), new Vector2(260f, 720f), new Vector2(0.5f, 0.5f));
			m_levelGrid = CreateRect(m_panel, "Levels", new Vector2(90f, 0f), new Vector2(1140f, 720f), new Vector2(0.5f, 0.5f));
			CreateLevelScroll(m_levelGrid);
			m_status = CreateLabel(m_panel, "Status", string.Empty, new Vector2(-160f, -400f), new Vector2(1160f, 40f), 22, TextAnchor.MiddleLeft);
			Button unload = CreateButton(m_panel, "Unload", "卸载关卡", new Vector2(560f, -400f), new Vector2(160f, 46f), new Vector2(0.5f, 0.5f), UnloadLevel);
			unload.GetComponent<Image>().color = new Color(0.28f, 0.2f, 0.08f, 1f);
			PopulateEpisodes();
		}

		/// <summary>Wraps the tile area in a vertical scroll view (episodes can exceed one page).</summary>
		private void CreateLevelScroll(RectTransform view)
		{
			GameObject viewportObject = new GameObject("Viewport", typeof(RectTransform));
			viewportObject.transform.SetParent(view, false);
			RectTransform viewport = (RectTransform)viewportObject.transform;
			viewport.anchorMin = Vector2.zero;
			viewport.anchorMax = Vector2.one;
			viewport.pivot = new Vector2(0.5f, 0.5f);
			viewport.anchoredPosition = Vector2.zero;
			viewport.sizeDelta = Vector2.zero;
			viewportObject.AddComponent<RectMask2D>();
			m_levelContent = CreateRect(viewport, "Content", Vector2.zero, new Vector2(0f, TileStepY), new Vector2(0.5f, 1f));
			m_levelContent.pivot = new Vector2(0.5f, 1f);
			m_levelContent.anchorMin = new Vector2(0f, 1f);
			m_levelContent.anchorMax = new Vector2(1f, 1f);
			m_levelContent.anchoredPosition = Vector2.zero;
			m_levelScroll = view.gameObject.AddComponent<ScrollRect>();
			m_levelScroll.viewport = viewport;
			m_levelScroll.content = m_levelContent;
			m_levelScroll.horizontal = false;
			m_levelScroll.vertical = true;
			m_levelScroll.movementType = ScrollRect.MovementType.Clamped;
			m_levelScroll.scrollSensitivity = 60f;
			m_levelScroll.inertia = true;
			m_levelScroll.decelerationRate = 0.135f;
		}

		private void PopulateEpisodes()
		{
			List<LevelEntryJson> levels = LevelCatalog.Levels;
			List<string> episodes = new List<string>();
			foreach (LevelEntryJson level in levels)
			{
				if (!episodes.Contains(level.episode))
				{
					episodes.Add(level.episode);
				}
			}
			for (int i = 0; i < episodes.Count; i++)
			{
				string episode = episodes[i];
				Button button = CreateButton(m_episodeList, "Episode_" + episode, EpisodeLabel(episode), new Vector2(0f, 340f - EpisodeButtonStep * i), new Vector2(EpisodeButtonWidth, EpisodeButtonHeight), new Vector2(0.5f, 0.5f), delegate
				{
					ShowEpisode(episode);
				});
				m_episodeButtons.Add(button);
			}
			if (episodes.Count > 0)
			{
				ShowEpisode(episodes[0]);
			}
		}

		private void ShowEpisode(string episode)
		{
			m_episode = episode;
			for (int i = 0; i < m_episodeButtons.Count; i++)
			{
				Button button = m_episodeButtons[i];
				bool active = button.name == "Episode_" + episode;
				button.GetComponent<Image>().color = active ? ButtonActiveColor : ButtonColor;
			}
			for (int j = 0; j < m_levelTiles.Count; j++)
			{
				UnityEngine.Object.Destroy(m_levelTiles[j].gameObject);
			}
			m_levelTiles.Clear();
			List<LevelEntryJson> levels = LevelCatalog.Levels;
			int index = 0;
			int rows = 0;
			foreach (LevelEntryJson level in levels)
			{
				if (level.episode != episode)
				{
					continue;
				}
				int column = index % Columns;
				int row = index / Columns;
				rows = row + 1;
				float gridWidth = (float)Columns * TileStepX;
				Vector2 position = new Vector2(
					-gridWidth * 0.5f + TileStepX * ((float)column + 0.5f),
					-TileStepY * ((float)row + 0.5f));
				LevelEntryJson captured = level;
				RectTransform tile = CreateTile(m_levelContent, "Level_" + level.scene, (index + 1).ToString(), level.title, position, delegate
				{
					LoadLevel(captured);
				});
				m_levelTiles.Add(tile);
				index++;
			}
			float contentHeight = Mathf.Max((float)rows * TileStepY, m_levelGrid.sizeDelta.y);
			m_levelContent.sizeDelta = new Vector2(0f, contentHeight);
			m_levelContent.anchoredPosition = Vector2.zero;
			if (m_levelScroll != null)
			{
				m_levelScroll.verticalNormalizedPosition = 1f;
			}
		}

		private void LoadLevel(LevelEntryJson entry)
		{
			if (!LevelManager.Instance)
			{
				return;
			}
			if (!LevelManager.Instance.Load(entry, out string error))
			{
				m_status.text = "<color=#FF9090>导入失败：" + error + "</color>";
				m_status.color = TextColor;
				return;
			}
			m_status.text = "已导入 " + entry.title + "（" + entry.scene + "）";
			RefreshStatus();
			m_panel.gameObject.SetActive(false);
		}

		private void UnloadLevel()
		{
			if (LevelManager.Instance != null)
			{
				LevelManager.Instance.Unload();
			}
			RefreshStatus();
		}

		private void RefreshStatus()
		{
			if (m_status == null)
			{
				return;
			}
			if (!LevelCatalog.IsAvailable)
			{
				m_status.text = "<color=#FF9090>找不到 StreamingAssets/BpsimLevels，请先运行 Tools/BPSim/export_levels.py --write</color>";
				return;
			}
			int count = LevelCatalog.Levels.Count;
			string current = LevelManager.Instance != null && LevelManager.Instance.HasLevel ? "　当前：" + LevelManager.Instance.Entry.title : "　当前：无";
			m_status.text = "共 " + count + " 个 BPLE 内置关卡" + current;
			m_status.color = TextColor;
		}

		private void Toggle()
		{
			bool open = !m_panel.gameObject.activeSelf;
			m_panel.gameObject.SetActive(open);
			if (open)
			{
				RefreshStatus();
			}
		}

		private static string EpisodeLabel(string episode)
		{
			// episode_3_cave / episode_3_sandbox / episode_race / episode_mm
			if (episode == "episode_race")
			{
				return "竞速";
			}
			if (episode == "episode_mm")
			{
				return "特别";
			}
			string[] parts = episode.Split('_');
			if (parts.Length < 2 || parts[0] != "episode")
			{
				return episode;
			}
			string label = "第 " + parts[1] + " 章";
			if (parts.Length >= 3)
			{
				if (parts[2] == "cave")
				{
					label += " · 洞穴";
				}
				else if (parts[2] == "sandbox")
				{
					label += " · 沙盒";
				}
				else
				{
					label += " · " + parts[2];
				}
			}
			return label;
		}

		/// <summary>
		/// Square level tile: a flat BPLE-green face over a thin dark outline square, with a
		/// bold white number centred — mirrors the BPLE level-select tiles (no stars).
		/// </summary>
		private RectTransform CreateTile(Transform parent, string name, string number, string title, Vector2 position, UnityAction onClick)
		{
			RectTransform rectTransform = CreateRect(parent, name, position, new Vector2(TileSize, TileSize), new Vector2(0.5f, 1f));
			rectTransform.pivot = new Vector2(0.5f, 0.5f);
			// Dark outline: an oversized square drawn behind the face.
			GameObject outlineObject = new GameObject("Outline", typeof(RectTransform));
			outlineObject.transform.SetParent(rectTransform, false);
			RectTransform outline = (RectTransform)outlineObject.transform;
			outline.anchorMin = Vector2.zero;
			outline.anchorMax = Vector2.one;
			outline.pivot = new Vector2(0.5f, 0.5f);
			outline.anchoredPosition = Vector2.zero;
			outline.sizeDelta = new Vector2(TileOutline * 2f, TileOutline * 2f);
			Image outlineImage = outlineObject.AddComponent<Image>();
			outlineImage.color = TileOutlineColor;
			outlineImage.raycastTarget = false;
			// Flat single-colour face, clickable.
			GameObject faceObject = new GameObject("Face", typeof(RectTransform));
			faceObject.transform.SetParent(rectTransform, false);
			RectTransform face = (RectTransform)faceObject.transform;
			face.anchorMin = Vector2.zero;
			face.anchorMax = Vector2.one;
			face.pivot = new Vector2(0.5f, 0.5f);
			face.anchoredPosition = Vector2.zero;
			face.sizeDelta = Vector2.zero;
			Image faceImage = faceObject.AddComponent<Image>();
			faceImage.color = TileFaceColor;
			faceImage.raycastTarget = true;
			Button button = faceObject.AddComponent<Button>();
			button.targetGraphic = faceImage;
			ColorBlock colors = button.colors;
			colors.normalColor = Color.white;
			colors.highlightedColor = new Color(1.15f, 1.15f, 1.15f, 1f);
			colors.pressedColor = new Color(0.78f, 0.78f, 0.78f, 1f);
			colors.selectedColor = Color.white;
			colors.fadeDuration = 0.08f;
			button.colors = colors;
			if (onClick != null)
			{
				button.onClick.AddListener(onClick);
			}
			// Bold number in the middle. Own GameObject: one Graphic per GameObject.
			GameObject numberObject = new GameObject("Number", typeof(RectTransform));
			numberObject.transform.SetParent(face, false);
			RectTransform numberRect = (RectTransform)numberObject.transform;
			numberRect.anchorMin = Vector2.zero;
			numberRect.anchorMax = Vector2.one;
			numberRect.pivot = new Vector2(0.5f, 0.5f);
			numberRect.anchoredPosition = Vector2.zero;
			numberRect.sizeDelta = Vector2.zero;
			Text text = numberObject.AddComponent<Text>();
			text.font = m_font;
			text.text = number;
			text.fontSize = 58;
			text.fontStyle = FontStyle.Bold;
			text.alignment = TextAnchor.MiddleCenter;
			text.color = TileNumberColor;
			text.horizontalOverflow = HorizontalWrapMode.Overflow;
			text.verticalOverflow = VerticalWrapMode.Overflow;
			text.raycastTarget = false;
			text.supportRichText = false;
			text.resizeTextForBestFit = true;
			text.resizeTextMaxSize = 58;
			text.resizeTextMinSize = 22;
			// Level title stays as the object name for tooling/debug; no on-screen label.
			rectTransform.gameObject.name = name;
			rectTransform.gameObject.AddComponent<TileTooltip>().Title = title;
			return rectTransform;
		}

		private RectTransform CreateRect(Transform parent, string name, Vector2 position, Vector2 size, Vector2 anchor)
		{
			GameObject gameObject = new GameObject(name, typeof(RectTransform));
			gameObject.transform.SetParent(parent, false);
			RectTransform rectTransform = (RectTransform)gameObject.transform;
			rectTransform.anchorMin = anchor;
			rectTransform.anchorMax = anchor;
			rectTransform.pivot = new Vector2(0.5f, 0.5f);
			rectTransform.anchoredPosition = position;
			rectTransform.sizeDelta = size;
			return rectTransform;
		}

		private Text CreateLabel(Transform parent, string name, string value, Vector2 position, Vector2 size, int fontSize, TextAnchor alignment)
		{
			RectTransform rectTransform = CreateRect(parent, name, position, size, new Vector2(0.5f, 0.5f));
			Text text = rectTransform.gameObject.AddComponent<Text>();
			text.font = m_font;
			text.text = value;
			text.fontSize = fontSize;
			text.alignment = alignment;
			text.color = TextColor;
			text.horizontalOverflow = HorizontalWrapMode.Overflow;
			text.verticalOverflow = VerticalWrapMode.Overflow;
			text.raycastTarget = false;
			text.supportRichText = true;
			return text;
		}

		private Button CreateButton(Transform parent, string name, string label, Vector2 position, Vector2 size, Vector2 anchor, UnityAction onClick)
		{
			RectTransform rectTransform = CreateRect(parent, name, position, size, anchor);
			Image image = rectTransform.gameObject.AddComponent<Image>();
			image.color = ButtonColor;
			Button button = rectTransform.gameObject.AddComponent<Button>();
			button.targetGraphic = image;
			ColorBlock colors = button.colors;
			colors.normalColor = Color.white;
			colors.highlightedColor = new Color(1.25f, 1.25f, 1.25f, 1f);
			colors.pressedColor = new Color(0.75f, 0.75f, 0.75f, 1f);
			button.colors = colors;
			if (onClick != null)
			{
				button.onClick.AddListener(onClick);
			}
			Text text = CreateLabel(rectTransform, "Text", label, Vector2.zero, size, 20, TextAnchor.MiddleCenter);
			text.rectTransform.anchorMin = Vector2.zero;
			text.rectTransform.anchorMax = Vector2.one;
			text.rectTransform.sizeDelta = new Vector2(-12f, 0f);
			text.rectTransform.anchoredPosition = Vector2.zero;
			return button;
		}
	}

	/// <summary>Carries the full level title for a tile (kept out of the number-only face).</summary>
	internal sealed class TileTooltip : MonoBehaviour
	{
		public string Title;
	}
}
