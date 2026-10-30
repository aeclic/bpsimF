using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Rendering;
using UnityEngine.UI;

namespace Bpsim.UI
{
	/// <summary>
	/// 开场品牌页（原生 uGUI 版，不依赖 WebView）：左侧 Made with Unity 标记，
	/// 右侧 bpsimF 波浪词标——水下上升、波纹横移、气泡上浮、F 的闪电式闪现。
	/// 播完淡出并进入主场景，之后不再出现（开始页的水印已移除）。
	/// </summary>
	public sealed class BrandSplashController : MonoBehaviour
	{
		public string NextScene = "MainScene";

		/// <summary>整段时长（秒，含淡出前的高光时刻）。</summary>
		public float BodyDuration = 2.6f;

		/// <summary>淡出时长（秒）。已不使用：结束直接切预加载好的场景，不再淡到相机底色。</summary>
		public float FadeOut = 0f;

		// ---- SVG 单位下的词标几何（viewBox 为 -8.01 -8 929.25 296）----
		private const float U_W = 929.25f;
		private const float U_H = 296.0f;
		private const float U_X0 = -8.01f;
		private const float U_Y0 = -8.0f;
		private const float WordmarkWidth = 900f;

		private const float FlashTime = 1.5f;

		// 水体几何：与品牌主图/Brand/svg 动画同一套 SVG userSpaceOnUse 渐变（y=-46..270），
		// 所以水体的高度是 316 单元、而不是随意的倍数；起始位置整体下移 310 单元。
		private const float SeaSurfaceY = -46f;
		private const float SeaDeepY = 270f;
		private const float SeaBodyBottomY = 360f;
		private const float SeaRiseUnits = 310f;
		// 字母画布在 SVG 里的纵向范围（letters.png 覆盖 -8..288）
		private const float LettersTopY = -8f;
		private const float LettersHeightUnits = 296f;

		// currents.png 覆盖 SVG x -8..1016（1024 单元），字母只占前 929.25 单元，
		// 余下 95 单元是给横向漂移留的余量（uvRect 永远不取样到边界，不会出现拉伸条纹）。
		private const float CurrentsUnits = 1024f;
		private const float CurrentsDrift = (CurrentsUnits - U_W) / CurrentsUnits;

		// 品牌墨色背景（Brand/README: ink / background #0a0d14）
		private static readonly Color Ink = new Color(0.039215688f, 0.050980393f, 0.078431375f, 1f);

		private CanvasGroup m_group;
		// Unity 自带 Splash（Personal 授权强制显示）结束前不计时，否则整页动画播在它背后

		private RectTransform m_sea;

		private RawImage m_waveA;

		private RawImage m_waveB;

		private Image m_ghost;

		private Image m_f;

		private Image m_dot;

		private Image m_flash;

		private Image m_bloom;

		private readonly List<Bubble> m_bubbles = new List<Bubble>();

		private float m_wordmarkHeight;

		private float m_seaTop;

		private float m_seaBottom;

		private float m_t;

		private bool m_started;

		private bool m_finished;

		/// <summary>品牌页播放期间就在后台把下一场景加载到 0.9，结束时直接激活，避免中间再闪一次黑/白底。</summary>
		private AsyncOperation m_preload;

		private bool m_preloadStarted;

		private sealed class Bubble
		{
			public RectTransform Rect;

			public float X;

			public float Phase;

			public float Speed;

			public float Size;
		}


		// 引擎 Splash 期间第一个场景不会被真正渲染（Splash 是独立一层），所以"第一次把场景渲染出来"
		// 才是可靠的"标志已消失"信号。实测 UnityEngine.SplashScreen / Application.isShowingSplashScreen
		// 这两个 internal API 都会提前返回"已结束"，不能用来计时。
		private static bool s_firstRender;

		private void OnDestroy()
		{
			RenderPipelineManager.endFrameRendering -= OnEndFrameRendering;
			Camera.onPostRender -= OnAnyCameraPostRender;
		}

		private void OnEndFrameRendering(ScriptableRenderContext context, Camera[] cameras)
		{
			s_firstRender = true;
		}

		private void OnAnyCameraPostRender(Camera cam)
		{
			s_firstRender = true;
		}

		private void Awake()
		{
			BuildUi();
			m_started = true;
			s_firstRender = false;
			RenderPipelineManager.endFrameRendering += OnEndFrameRendering;
			Camera.onPostRender += OnAnyCameraPostRender;
		}

		/// <summary>
		/// 在品牌页动起来的同时把下一场景异步载入（allowSceneActivation=false 会停在 0.9），
		/// 真正切换时只是放行，玩家看不到加载过程，也不会出现加载画面那种黑底/白底。
		/// </summary>
		private void BeginPreload()
		{
			if (m_preloadStarted)
			{
				return;
			}
			m_preloadStarted = true;
			if (string.IsNullOrEmpty(NextScene) || !Application.CanStreamedLevelBeLoaded(NextScene))
			{
				return;
			}
			try
			{
				m_preload = SceneManager.LoadSceneAsync(NextScene);
				if (m_preload != null)
				{
					m_preload.allowSceneActivation = false;
				}
			}
			catch (Exception exception)
			{
				Debug.LogWarning("[BrandSplash] preload failed: " + exception.Message);
				m_preload = null;
			}
		}

		private static Sprite LoadSprite(string name)
		{
			Sprite sprite = Resources.Load<Sprite>("BrandSplash/" + name);
			if (sprite == null)
			{
				Debug.LogWarning("[BrandSplash] missing sprite BrandSplash/" + name);
			}
			return sprite;
		}

		private static Image AddImage(Transform parent, string name, Sprite sprite, Color color)
		{
			GameObject go = new GameObject(name, typeof(RectTransform), typeof(Image));
			RectTransform rect = go.GetComponent<RectTransform>();
			rect.SetParent(parent, false);
			Image image = go.GetComponent<Image>();
			image.sprite = sprite;
			image.type = Image.Type.Simple;
			image.color = color;
			image.raycastTarget = false;
			return image;
		}

		private static RawImage AddRaw(Transform parent, string name, Texture texture, Color color)
		{
			GameObject go = new GameObject(name, typeof(RectTransform), typeof(RawImage));
			RectTransform rect = go.GetComponent<RectTransform>();
			rect.SetParent(parent, false);
			RawImage image = go.GetComponent<RawImage>();
			image.texture = texture;
			image.color = color;
			image.raycastTarget = false;
			return image;
		}

		private static void Stretch(RectTransform rect)
		{
			rect.anchorMin = Vector2.zero;
			rect.anchorMax = Vector2.one;
			rect.offsetMin = Vector2.zero;
			rect.offsetMax = Vector2.zero;
			rect.localScale = Vector3.one;
		}

		/// <summary>把水流层钉在水体的顶部、按字母画布的高度铺开（水体会体比字母高，不能直接 Stretch）。</summary>
		private void PlaceCurrents(RectTransform rect, float height, float offsetY)
		{
			rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 1f);
			rect.pivot = new Vector2(0.5f, 1f);
			rect.sizeDelta = new Vector2(WordmarkWidth + 24f, height);
			rect.anchoredPosition = new Vector2(0f, -offsetY);
			rect.localScale = Vector3.one;
		}

		/// <summary>把 SVG 单位坐标（左上原点，向下为正）换算成以词标中心为原点的 UI 坐标。</summary>
		private Vector2 UnitToLocal(float ux, float uy)
		{
			float k = WordmarkWidth / U_W;
			return new Vector2((ux - U_X0 - U_W * 0.5f) * k, (U_H * 0.5f - (uy - U_Y0)) * k);
		}

		private void BuildUi()
		{
			GameObject canvasGo = new GameObject("BrandSplashCanvas", typeof(Canvas), typeof(CanvasScaler),
				typeof(GraphicRaycaster), typeof(CanvasGroup));
			Canvas canvas = canvasGo.GetComponent<Canvas>();
			canvas.renderMode = RenderMode.ScreenSpaceOverlay;
			canvas.sortingOrder = 32000;
			CanvasScaler scaler = canvasGo.GetComponent<CanvasScaler>();
			scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
			scaler.referenceResolution = new Vector2(1920f, 1080f);
			scaler.matchWidthOrHeight = 0.5f;
			m_group = canvasGo.GetComponent<CanvasGroup>();
			canvasGo.transform.SetParent(transform, false);

			// 背景：与引擎 Splash 的底色一致
			Image backdrop = AddImage(canvasGo.transform, "Backdrop", null, Ink);
			Stretch(backdrop.rectTransform);

			m_wordmarkHeight = WordmarkWidth * U_H / U_W;

			float markHeight = 296f;
			float markWidth = markHeight * 260f / 250f;
			float gap = 118f;
			float rowWidth = markWidth + gap + WordmarkWidth;

			GameObject rowGo = new GameObject("Row", typeof(RectTransform));
			RectTransform row = rowGo.GetComponent<RectTransform>();
			row.SetParent(canvasGo.transform, false);
			row.anchorMin = row.anchorMax = new Vector2(0.5f, 0.5f);
			row.pivot = new Vector2(0.5f, 0.5f);
			row.sizeDelta = new Vector2(rowWidth, Mathf.Max(markHeight, m_wordmarkHeight));
			row.anchoredPosition = Vector2.zero;

			// ---- 左：Made with Unity 标记 ----
			Image mark = AddImage(row, "MadeWithUnity", LoadSprite("mwu"), Color.white);
			RectTransform markRect = mark.rectTransform;
			markRect.anchorMin = markRect.anchorMax = new Vector2(0f, 0.5f);
			markRect.pivot = new Vector2(0f, 0.5f);
			markRect.sizeDelta = new Vector2(markWidth, markHeight);
			markRect.anchoredPosition = new Vector2(0f, 0f);

			// ---- 右：词标 ----
			GameObject wmGo = new GameObject("Wordmark", typeof(RectTransform));
			RectTransform wm = wmGo.GetComponent<RectTransform>();
			wm.SetParent(row, false);
			wm.anchorMin = wm.anchorMax = new Vector2(1f, 0.5f);
			wm.pivot = new Vector2(1f, 0.5f);
			wm.sizeDelta = new Vector2(WordmarkWidth, m_wordmarkHeight);
			wm.anchoredPosition = new Vector2(0f, 16f);

			Sprite letters = LoadSprite("letters");
			Sprite seaSprite = LoadSprite("sea");
			Sprite currentsSprite = LoadSprite("currents");
			Sprite bubbleSprite = LoadSprite("bubble");
			Sprite fSprite = LoadSprite("f");
			Sprite dotSprite = LoadSprite("dot");
			Sprite fWhite = LoadSprite("fwhite");
			Sprite bloomSprite = LoadSprite("bloom");

			// 幽灵层（水线以上的字母轮廓）
			m_ghost = AddImage(wm, "Ghost", letters, new Color(0.22f, 0.745f, 0.973f, 0.16f));
			Stretch(m_ghost.rectTransform);

			// 字母遮罩：Image 只写模板不画颜色
			GameObject maskGo = new GameObject("LettersMask", typeof(RectTransform), typeof(Image), typeof(Mask));
			RectTransform maskRect = maskGo.GetComponent<RectTransform>();
			maskRect.SetParent(wm, false);
			Stretch(maskRect);
			Image maskImage = maskGo.GetComponent<Image>();
			maskImage.sprite = letters;
			maskImage.type = Image.Type.Simple;
			maskImage.color = Color.white;
			maskImage.raycastTarget = false;
			Mask mask = maskGo.GetComponent<Mask>();
			mask.showMaskGraphic = false;

			// 水体：从下往上灌满字母
			RawImage sea = AddRaw(maskGo.transform, "Sea", seaSprite != null ? seaSprite.texture : null, Color.white);
			m_sea = sea.rectTransform;
			m_sea.anchorMin = m_sea.anchorMax = new Vector2(0.5f, 0.5f);
			m_sea.pivot = new Vector2(0.5f, 1f);
			float unitPx = WordmarkWidth / U_W;
			float seaHeight = (SeaBodyBottomY - SeaSurfaceY) * unitPx;
			m_sea.sizeDelta = new Vector2(WordmarkWidth + 24f, seaHeight);
			sea.gameObject.AddComponent<RectMask2D>();
			sea.uvRect = new Rect(0f, 0f, 1f, 1f);
			m_seaTop = UnitToLocal(0f, SeaSurfaceY).y;
			m_seaBottom = m_seaTop - SeaRiseUnits * unitPx;
			m_sea.anchoredPosition = new Vector2(0f, m_seaBottom);

			// 水流：四条细带（SVG 里的 c1..c4），两层反向漂移加密度
			float currentsWindow = U_W / CurrentsUnits;
			m_waveA = AddRaw(sea.transform, "CurrentsA",
				currentsSprite != null ? currentsSprite.texture : null, Color.white);
			m_waveB = AddRaw(sea.transform, "CurrentsB",
				currentsSprite != null ? currentsSprite.texture : null, new Color(1f, 1f, 1f, 0.6f));
			// currents.png 是字母画布尺寸：对齐到字母本身（而不是被拉满整块水体）
			float currentsHeight = LettersHeightUnits * unitPx;
			float currentsOffsetY = (SeaSurfaceY - LettersTopY) * unitPx;
			PlaceCurrents(m_waveA.rectTransform, currentsHeight, currentsOffsetY);
			m_waveA.uvRect = new Rect(0f, 0f, currentsWindow, 1f);
			PlaceCurrents(m_waveB.rectTransform, currentsHeight, currentsOffsetY);
			m_waveB.uvRect = new Rect(CurrentsDrift, 0f, currentsWindow, 1f);

			// 气泡
			System.Random rng = new System.Random(20261026);
			for (int i = 0; i < 6; i++)
			{
				float x = Mathf.Lerp(-WordmarkWidth * 0.45f, WordmarkWidth * 0.45f, (float)rng.NextDouble());
				float size = Mathf.Lerp(7f, 15f, (float)rng.NextDouble());
				Image bubble = AddImage(sea.transform, "Bubble" + i, bubbleSprite, new Color(1f, 1f, 1f, 0.55f));
				RectTransform rect = bubble.rectTransform;
				rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
				rect.sizeDelta = new Vector2(size, size);
				m_bubbles.Add(new Bubble
				{
					Rect = rect,
					X = x,
					Phase = (float)rng.NextDouble(),
					Speed = Mathf.Lerp(0.16f, 0.3f, (float)rng.NextDouble()),
					Size = size
				});
			}

			// F 与 i 点：琥珀色实心，位于遮罩之上
			m_f = AddImage(wm, "F", fSprite, new Color(1f, 1f, 1f, 0f));
			Stretch(m_f.rectTransform);

			m_dot = AddImage(wm, "Dot", dotSprite, new Color(1f, 1f, 1f, 0f));
			Stretch(m_dot.rectTransform);

			m_flash = AddImage(wm, "FFlash", fWhite, new Color(1f, 1f, 1f, 0f));
			Stretch(m_flash.rectTransform);

			m_bloom = AddImage(wm, "Bloom", bloomSprite, new Color(1f, 1f, 1f, 0f));
			RectTransform bloomRect = m_bloom.rectTransform;
			Vector2 fLocal = UnitToLocal(843.6f, 107.3f);
			bloomRect.anchorMin = bloomRect.anchorMax = new Vector2(0.5f, 0.5f);
			bloomRect.sizeDelta = new Vector2(m_wordmarkHeight * 1.7f, m_wordmarkHeight * 1.7f);
			bloomRect.anchoredPosition = fLocal;

			// 首帧：水体在字母下方
			m_sea.anchoredPosition = new Vector2(0f, m_seaBottom);

			Canvas.ForceUpdateCanvases();
		}

		private void Update()
		{
			if (!m_started || m_finished)
			{
				return;
			}
			// 引擎 Splash 期间场景不渲染：等第一次真正渲染再把整页动画放出来，
			// 这样动画绝不会被 Unity 标志盖掉（两个 internal 的 splash 查询都不可靠）。
			if (!s_firstRender)
			{
				if (Time.realtimeSinceStartup > 25f)
				{
					s_firstRender = true;
				}
				else
				{
					return;
				}
			}
			// 场景此刻已完整载入：Awake 里直接 LoadSceneAsync 会把正在载入的本场景顶掉
			// （开场页被销毁、Update 一次都不跑），所以预加载放到第一帧真正渲染之后开始。
			BeginPreload();
			m_t += Time.unscaledDeltaTime;
			float t = m_t;

			// 1) 潮水上升（0.15s 起步，2.0s 完成，缓出）
			float rise = Mathf.Clamp01((t - 0.15f) / 2.0f);
			float eased = 1f - Mathf.Pow(1f - rise, 3f);
			float y = Mathf.Lerp(m_seaBottom, m_seaTop, eased);
			m_sea.anchoredPosition = new Vector2(0f, y);

			// 2) 水流漂移 + 轻微潮汐（设计里是 ±9 单元，不是大幅晃动）
			float currentsWindow = U_W / CurrentsUnits;
			float drift = Mathf.Repeat(t * 0.035f, CurrentsDrift * 2f);
			float driftA = Mathf.Min(drift, CurrentsDrift * 2f - drift) / 2f;
			m_waveA.uvRect = new Rect(driftA, 0f, currentsWindow, 1f);
			m_waveB.uvRect = new Rect(CurrentsDrift - driftA, 0f, currentsWindow, 1f);
			float tide = Mathf.Sin(t * 0.698f) * 4f;
			((RectTransform)m_waveA.transform).anchoredPosition = new Vector2(0f, tide);
			((RectTransform)m_waveB.transform).anchoredPosition = new Vector2(0f, -tide * 0.7f);

			// 3) 气泡上浮
			float span = m_sea.sizeDelta.y;
			for (int i = 0; i < m_bubbles.Count; i++)
			{
				Bubble b = m_bubbles[i];
				float p = Mathf.Repeat(b.Phase + t * b.Speed, 1f);
				b.Rect.anchoredPosition = new Vector2(b.X, -p * span);
				float a = Mathf.Sin(p * Mathf.PI);
				Color c = b.Rect.GetComponent<Image>().color;
				c.a = 0.55f * a;
				b.Rect.GetComponent<Image>().color = c;
			}

			// 4) F 的闪电式出现
			float fin = Mathf.Clamp01((t - FlashTime) / 0.14f);
			m_f.color = new Color(1f, 1f, 1f, fin);
			float pop = t < FlashTime ? 0.94f : Mathf.Lerp(1.05f, 1f, Mathf.Clamp01((t - FlashTime - 0.16f) / 0.5f));
			m_f.rectTransform.localScale = new Vector3(pop, pop, 1f);

			float flash = 0f;
			if (t > FlashTime)
			{
				float ft = t - FlashTime;
				flash = ft < 0.06f ? Mathf.Lerp(0f, 1f, ft / 0.06f) : Mathf.Pow(Mathf.Clamp01(1f - (ft - 0.06f) / 0.95f), 0.65f);
			}
			m_flash.color = new Color(1f, 1f, 1f, flash * 0.95f);
			float bloom = 0f;
			if (t > FlashTime - 0.04f)
			{
				float bt = t - FlashTime + 0.04f;
				bloom = bt < 0.05f ? Mathf.Lerp(0f, 1f, bt / 0.05f) : Mathf.Pow(Mathf.Clamp01(1f - (bt - 0.05f) / 1.15f), 0.8f);
			}
			m_bloom.color = new Color(1f, 1f, 1f, bloom * 0.9f);
			float bloomScale = Mathf.Lerp(0.75f, 1.15f, Mathf.Clamp01((t - FlashTime + 0.04f) / 0.8f));
			m_bloom.rectTransform.localScale = new Vector3(bloomScale, bloomScale, 1f);

			// 5) i 点：先出现，然后在 2.2s 起跳动
			float dotIn = Mathf.Clamp01((t - FlashTime + 0.2f) / 0.3f);
			m_dot.color = new Color(1f, 1f, 1f, dotIn);
			float hop = 0f;
			if (t > 2.2f)
			{
				float hp = Mathf.Repeat((t - 2.2f) / 2.6f, 1f);
				if (hp < 0.12f) hop = Mathf.Sin(hp / 0.12f * Mathf.PI * 0.5f) * 26f;
				else if (hp < 0.24f) hop = Mathf.Lerp(26f, 0f, (hp - 0.12f) / 0.12f);
				else if (hp < 0.31f) hop = Mathf.Sin((hp - 0.24f) / 0.07f * Mathf.PI) * 10f;
			}
			// dot 贴图本身就是整张词标画布（点已经画在 i 上方的原位），
			// 只能叠加上跳偏移；再给一次 SVG 坐标会把点顶到画布外面去。
			m_dot.rectTransform.anchoredPosition = new Vector2(0f, hop);

			// 6) 结束：淡出并进入主场景
			if (t >= BodyDuration)
			{
				m_finished = true;
				StartCoroutine(Finish());
			}
		}

		/// <summary>
		/// 结束时不淡出到相机底色（那一步就是玩家看到的黑/白底切换），
		/// 品牌页保持不透明，等预加载放行后直接由下一场景接管第一帧。
		/// </summary>
		private IEnumerator Finish()
		{
			if (m_preload == null)
			{
				BeginPreload();
			}
			if (m_preload != null)
			{
				while (m_preload.progress < 0.9f && !m_preload.isDone)
				{
					yield return null;
				}
				m_preload.allowSceneActivation = true;
				yield break;
			}
			if (!string.IsNullOrEmpty(NextScene) && Application.CanStreamedLevelBeLoaded(NextScene))
			{
				SceneManager.LoadScene(NextScene);
				yield break;
			}
			int index = SceneManager.GetActiveScene().buildIndex;
			if (index + 1 < SceneManager.sceneCountInBuildSettings)
			{
				SceneManager.LoadScene(index + 1);
			}
		}
	}
}
