using System;
using System.Collections.Generic;
using System.IO;
using Bpsim.Parts.Simulation;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using UnityEngine;

namespace Bpsim.Parts
{
	public class PartManager : UnitySingleton<PartManager>
	{
		[SerializeField]
		private PartFactory m_factory;

		[SerializeField]
		private PartScene m_scenePrefab;

		private bool m_simulating;

		private int m_sceneID;

		private PartScene m_activeScene;

		private Dictionary<int, PartScene> m_sceneMap;

		private PartDrawer m_partDrawer;

		private PartSimulator m_partSimulator;

		private World m_world;

		private PartSceneSystem m_system;

		private EntityManager m_entityManager;

		private PartEntitySpawner m_entitySpawner;

		public const int SimulationSceneID = -1;

		public bool IsSimulating => m_simulating;

		/// <summary>
		/// False until <c>Start()</c> has resolved the ECS world; scene loading has to wait
		/// for it because <see cref="CreateScene"/> instantiates into that world.
		/// </summary>
		public bool IsReady => m_world != null && m_world.IsCreated;

		public PartScene ActiveScene => m_activeScene;

		public PartFactory Factory => m_factory;

		public PartSimulator PartSimulator => m_partSimulator;

		internal PartEntitySpawner EntitySpawner => m_entitySpawner;

		internal PartDrawer PartDrawer => m_partDrawer;

		internal PartSceneSystem System => m_system;

		public new static PartManager Instance => UnitySingleton<PartManager>.Instance;

		public event Action<PartScene> SceneLoaded;

		public event Action<PartScene> SceneUnloaded;

		public event Action<PartScene> SceneSelected;

		protected override void Awake()
		{
			base.Awake();
			m_sceneID = 1;
			m_sceneMap = new Dictionary<int, PartScene>();
			m_partDrawer = new PartDrawer();
		}

		private void Start()
		{
			// 先挂默认「超平坦」场景协程：它独立于 Start 后续代码运行，且自带
			// 等待/重试。即便 ECS World 的解析因 Burst/域重载的瞬时故障失败
			// （Unity 快速反复进出 Play 时偶发），默认场景仍会在 ECS 恢复后建出来。
			if (!Application.isBatchMode)
			{
				StartCoroutine(EnsureDefaultFlatScene());
			}
			EnsureWorld();
		}

		/// <summary>
		/// 幂等地解析 ECS World。快速反复进出 Play 时，Burst 域重载可能让
		/// <see cref="World.DefaultGameObjectInjectionWorld"/> 在某帧为 null；此时返回
		/// false，后续帧（由 <see cref="EnsureDefaultFlatScene"/> 驱动）再重试即可。
		/// </summary>
		private bool EnsureWorld()
		{
			if (m_world != null && m_world.IsCreated)
			{
				return true;
			}
			try
			{
				m_world = World.DefaultGameObjectInjectionWorld;
				if (m_world == null || !m_world.IsCreated)
				{
					m_world = null;
					return false;
				}
				m_entityManager = m_world.EntityManager;
				m_entitySpawner = new PartEntitySpawner(m_world);
				m_system = m_world.GetOrCreateSystemManaged<PartSceneSystem>();
				SetFixedDeltaTime(UserSettings.Instance.SimulationSettings.FixedTimeStep);
				return true;
			}
			catch (Exception e)
			{
				Debug.LogWarning("[PartManager] ECS World 尚未就绪，稍后重试：" + e.Message);
				m_world = null;
				return false;
			}
		}

		private System.Collections.IEnumerator EnsureDefaultFlatScene()
		{
			// 等 ECS World 就绪（最多 ~10 秒），期间每帧重试 EnsureWorld；这样即便
			// Start 那一刻 World 因瞬时故障未建好，也能在其恢复后继续。同时给
			// LevelManager 的会话恢复 / QA 自建场景 / Mod 的 OnLoad 留出先跑的机会。
			for (int i = 0; i < 600 && !EnsureWorld(); i++)
			{
				yield return null;
			}
			for (int i = 0; i < 6; i++)
			{
				yield return null;
			}
			if (Application.isBatchMode || !IsReady || HasActiveScene())
			{
				yield break;
			}
			if (Bpsim.Levels.LevelSession.HasLevel || Bpsim.Levels.LevelSession.HasScene)
			{
				yield break;
			}
			PartScene created = null;
			Action<PartScene> handler = scene => created = scene;
			SceneLoaded += handler;
			try
			{
				LoadEmptyScene("Empty", flatGround: true);
			}
			finally
			{
				SceneLoaded -= handler;
			}
			if (created != null)
			{
				SelectScene(created.SceneID);
				SandboxMap.Sync(true);
			}
		}

		private void EnsureBpleLightPreview() => Gameplay.BpleLightRuntime.Ensure();

		private void SetFixedDeltaTime(float deltaTime)
		{
			m_world.GetOrCreateSystemManaged<FixedStepSimulationSystemGroup>().Timestep = deltaTime;
		}

		public bool HasActiveScene()
		{
			return m_activeScene != null;
		}

		public PartScene FindScene(int id)
		{
			return m_sceneMap[id];
		}

		/// <param name="flatGround">
		/// true = 这是给玩家看的「超平坦」空白场景（taffy 式白底 + 无限灰地面 +
		/// 模拟时的运动学物理地面）。QA / 关卡引擎内部造的空测试场景保持 false，
		/// 不受这块地面的影响。
		/// </param>
		public void LoadEmptyScene(string name, bool flatGround = false)
		{
			int sceneID = m_sceneID;
			PartScene partScene = CreateScene(sceneID, name, m_entityManager.World);
			partScene.State.CameraPosition = Vector2.zero;
			partScene.State.CameraSize = 20f;
			partScene.State.UseFlatGround = flatGround;
			Entity entity = partScene.Unmanaged.FindLastPart(PartType.Pig);
			if (entity != Entity.Null)
			{
				PartTransform componentData = m_entityManager.GetComponentData<PartTransform>(entity);
				partScene.State.CameraPosition = new Vector2(componentData.X, componentData.Y);
			}
			m_sceneMap.Add(sceneID, partScene);
			m_sceneID++;
			if (flatGround)
			{
				SandboxMap.Create();
			}
			this.SceneLoaded?.Invoke(partScene);
		}

		/// <summary>
		/// 读取并放置一个场景。传入 <see cref="SchematicsFormat.Auto" /> 时先按内容判定格式，
		/// 返回值是实际使用的格式（显式格式则原样返回），方便调用方把它记进会话。
		/// </summary>
		public SchematicsFormat LoadScene(Stream stream, string name, SchematicsFormat format)
		{
			MemoryStream bufferedStream = null;
			Stream loadStream = stream;
			try
			{
				if (format == SchematicsFormat.Auto)
				{
					// DetectFormat restores seekable streams, but a network/package stream may not
					// support seeking. Buffer it once so detection and the actual loader see the same bytes.
					if (stream != null && !stream.CanSeek)
					{
						bufferedStream = new MemoryStream();
						stream.CopyTo(bufferedStream);
						bufferedStream.Position = 0L;
						loadStream = bufferedStream;
					}
					format = Schematics.DetectFormat(loadStream);
				}
				Schematics schematics = Schematics.CreateLoader(format).Read(loadStream);
			int sceneID = m_sceneID;
			PartScene partScene = CreateScene(sceneID, name, m_entityManager.World);
			using NativeArray<Schematics.Unit> schematics2 = schematics.ToNative(Allocator.TempJob);
			partScene.Unmanaged.EntityManager.CompleteAllTrackedJobs();
			IJobExtensions.Run(PartSceneJobs.PlaceParts(partScene, overlay: false, schematics2));
			partScene.State.CameraPosition = Vector2.zero;
			partScene.State.CameraSize = 20f;
			Entity entity = partScene.Unmanaged.FindLastPart(PartType.Pig);
			if (entity != Entity.Null)
			{
				PartTransform componentData = m_entityManager.GetComponentData<PartTransform>(entity);
				partScene.State.CameraPosition = new Vector2(componentData.X, componentData.Y);
			}
			m_sceneMap.Add(sceneID, partScene);
			m_sceneID++;
			this.SceneLoaded?.Invoke(partScene);
			return format;
			}
			finally
			{
				bufferedStream?.Dispose();
			}
		}

		public void UnloadScene(int id)
		{
			if (m_sceneMap.TryGetValue(id, out var value))
			{
				this.SceneUnloaded?.Invoke(value);
				UnityEngine.Object.Destroy(value.gameObject);
				m_sceneMap.Remove(id);
				if (m_activeScene != null && m_activeScene.SceneID == id)
				{
					m_activeScene = null;
				}
			}
		}

		public void SaveScene(Stream stream, int id, SchematicsFormat format)
		{
			if (!m_sceneMap.TryGetValue(id, out var value))
			{
				return;
			}
			ISchematicsLoader schematicsLoader = Schematics.CreateLoader(format);
			Schematics schematics = new Schematics(value.Unmanaged.Parts.Length);
			foreach (Entity part in value.Unmanaged.Parts)
			{
				PartTypeValue componentData = m_entityManager.GetComponentData<PartTypeValue>(part);
				PartTransform componentData2 = m_entityManager.GetComponentData<PartTransform>(part);
				PartFreeTransform freeTransform = m_entityManager.HasComponent<PartFreeTransform>(part) ? PartFreeTransform.Sanitize(m_entityManager.GetComponentData<PartFreeTransform>(part)) : PartFreeTransform.Identity;
				int layer = m_entityManager.HasComponent<PartBuildLayer>(part) ? PartBuildLayer.Clamp(m_entityManager.GetComponentData<PartBuildLayer>(part).Value) : PartBuildLayer.MinLayer;
				schematics.Units.Add(new Schematics.Unit(componentData2.X, componentData2.Y, (int)componentData.Type, componentData.Index, componentData2.Rotation, componentData2.Flipped, freeTransform.Offset.x, freeTransform.Offset.y, freeTransform.RotationDegrees, freeTransform.Scale.x, freeTransform.Scale.y, layer, m_entityManager.HasComponent<FrameShape>(part) ? m_entityManager.GetComponentData<FrameShape>(part).Value : 0));
			}
			schematicsLoader.Write(stream, schematics);
		}

		public void SelectScene(int id)
		{
			if (m_sceneMap.TryGetValue(id, out var value))
			{
				if (m_activeScene != null && id != m_activeScene.SceneID)
				{
					m_activeScene.State.CameraPosition = SceneCamera.Instance.TargetPoint;
					m_activeScene.State.CameraSize = SceneCamera.Instance.TargetSize;
					m_activeScene.gameObject.SetActive(value: false);
				}
				value.gameObject.SetActive(value: true);
				value.State.LastSelectedTime = Time.time;
				m_activeScene = value;
				this.SceneSelected?.Invoke(value);
			}
		}

		public void StartSimulation()
		{
			if (!(m_activeScene == null) && !m_simulating)
			{
				SetFixedDeltaTime(UserSettings.Instance.SimulationSettings.FixedTimeStep);
				m_simulating = true;
				m_world.GetOrCreateSystemManaged<FrameShapeSystem>().ApplyPending();
				m_partSimulator = new PartSimulator();
				m_partSimulator.Run(m_activeScene.UnmanagedRef);
				Gameplay.PartGameplayManager.Ensure().Begin();
			}
		}

		public void EndSimulation()
		{
			if (m_simulating)
			{
				m_simulating = false;
				if (Gameplay.PartGameplayManager.Instance != null)
				{
					Gameplay.PartGameplayManager.Instance.End();
				}
				m_partSimulator.Dispose();
			}
		}

		private void Update()
		{
			if (Gameplay.BpleLightRuntime.Instance == null && m_system != null) EnsureBpleLightPreview();
			m_partDrawer.Update(this);
			// 关卡场景有自己的地形，隐藏超平坦视觉并还原相机背景；空白/超平坦显示它。
			SandboxMap.Sync(m_activeScene != null && m_activeScene.State.UseFlatGround);
		}

		private PartScene CreateScene(int id, string name, World world)
		{
			PartScene partScene = UnityEngine.Object.Instantiate(m_scenePrefab);
			partScene.transform.SetParent(base.transform, worldPositionStays: false);
			partScene.SceneName = name;
			partScene.Initialize(id, world);
			return partScene;
		}
	}
}
