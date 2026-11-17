using UnityEngine;

namespace Bpsim
{
	/// <summary>
	/// 移植自 <c>bpsimtaffy</c> 的 <c>Bpsim.SandboxMap</c>：默认「超平坦」场景的
	/// 纯白背景 + 一整块灰色无限地面。原实现只存在于 taffy player 的
	/// <c>Assembly-CSharp.dll</c> 里（主工程没有源码），这里按反编译出的 IL 等价重建。
	/// </summary>
	/// <remarks>
	/// <para>视觉层只负责画；物理地面由 <c>PartSimulator.CreateSandboxGround</c> 在模拟开始时
	/// 生成（运动学盒，顶面 y = -10，正好对齐这里灰色地面的顶边）。</para>
	/// <para>只有「空 / 超平坦」场景会显示它。是否被激活由
	/// <see cref="PartManager"/> 每帧根据当前场景的 <c>PartSceneState.UseFlatGround</c> 同步。</para>
	/// </remarks>
	public static class SandboxMap
	{
		private const string GroundMaterialResource = "SandboxGround";

		private static bool s_created;
		private static bool s_visible;
		private static GameObject s_root;
		private static Material s_groundMaterial;
		private static Camera s_camera;
		private static CameraClearFlags s_previousClearFlags;
		private static Color s_previousBackground;

		/// <summary>场景里是否已经建好了超平坦视觉根节点。</summary>
		public static bool Exists => s_created && s_root != null;

		/// <summary>
		/// 建立白色相机背景与灰色地面精灵。重复调用是安全的（已存在则直接返回）。
		/// </summary>
		public static void Create()
		{
			if (Exists)
			{
				return;
			}
			s_created = true;

			Camera camera = SceneCamera.Instance != null ? SceneCamera.Instance.Camera : Camera.main;
			if (camera != null)
			{
				s_camera = camera;
				s_previousClearFlags = camera.clearFlags;
				s_previousBackground = camera.backgroundColor;
				ApplyCamera(camera);
			}

			Material material = Resources.Load<Material>(GroundMaterialResource);
			if (material == null)
			{
				Shader shader = Shader.Find("Sprites/Default");
				if (shader == null)
				{
					shader = Shader.Find("Unlit/Color");
				}
				material = new Material(shader);
				material.color = new Color(0.7f, 0.7f, 0.7f, 1f);
			}
			s_groundMaterial = material;

			GameObject root = new GameObject("SandboxMap");
			CreateSprite(root.transform, "Ground",
				new Vector3(0f, -10f, 10f), new Vector2(100000f, 10000f),
				material, -1000, new Vector2(0.5f, 1f));
			s_root = root;
			s_visible = true;
			root.SetActive(true);
		}

		private static void ApplyCamera(Camera camera)
		{
			camera.clearFlags = CameraClearFlags.SolidColor;
			camera.backgroundColor = Color.white;
		}

		private static void RestoreCamera(Camera camera)
		{
			camera.clearFlags = s_previousClearFlags;
			camera.backgroundColor = s_previousBackground;
		}

		private static void CreateSprite(Transform parent, string name, Vector3 position,
			Vector2 size, Material material, int sortingOrder, Vector2 pivot)
		{
			Texture2D texture = Texture2D.whiteTexture;
			Sprite sprite = Sprite.Create(texture,
				new Rect(0f, 0f, texture.width, texture.height), pivot, 1f);
			sprite.name = name + "Sprite";
			GameObject go = new GameObject(name);
			go.transform.SetParent(parent, false);
			go.transform.localPosition = position;
			go.transform.localScale = new Vector3(size.x, size.y, 1f);
			SpriteRenderer renderer = go.AddComponent<SpriteRenderer>();
			renderer.sprite = sprite;
			renderer.sharedMaterial = material;
			renderer.sortingOrder = sortingOrder;
			renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
			renderer.receiveShadows = false;
		}

		/// <summary>按当前激活场景是否是超平坦场景，显示/隐藏超平坦视觉并切换相机背景。</summary>
		public static void Sync(bool visible)
		{
			// 懒创建保险：万一某个超平坦场景通过非 LoadEmptyScene 的路径变成激活场景，
			// 也能在第一次需要显示时把视觉根节点补上。
			if (visible && !Exists)
			{
				Create();
			}
			if (!s_created || s_root == null)
			{
				return;
			}
			if (s_visible == visible)
			{
				return;
			}
			s_visible = visible;
			s_root.SetActive(visible);
			if (s_camera != null)
			{
				if (visible)
				{
					ApplyCamera(s_camera);
				}
				else
				{
					RestoreCamera(s_camera);
				}
			}
		}

		/// <summary>网格开关（随包材质有 <c>_GridVisible</c> 时生效）。</summary>
		public static void SetGridVisible(bool visible)
		{
			if (s_groundMaterial == null)
			{
				s_groundMaterial = Resources.Load<Material>(GroundMaterialResource);
			}
			if (s_groundMaterial != null)
			{
				s_groundMaterial.SetFloat("_GridVisible", visible ? 1f : 0f);
			}
		}

		/// <summary>域重载 / 退出 Play Mode 后清理静态状态。</summary>
		public static void ResetStatics()
		{
			s_created = false;
			s_visible = false;
			s_root = null;
			s_groundMaterial = null;
			s_camera = null;
		}
	}
}
