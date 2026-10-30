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
			m_world = World.DefaultGameObjectInjectionWorld;
			m_entityManager = m_world.EntityManager;
			m_entitySpawner = new PartEntitySpawner(m_world);
			m_system = m_world.GetOrCreateSystemManaged<PartSceneSystem>();
			SetFixedDeltaTime(UserSettings.Instance.SimulationSettings.FixedTimeStep);
		}

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

		public void LoadEmptyScene(string name)
		{
			int sceneID = m_sceneID;
			PartScene partScene = CreateScene(sceneID, name, m_entityManager.World);
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
		}

		public void LoadScene(Stream stream, string name, SchematicsFormat format)
		{
			Schematics schematics = Schematics.CreateLoader(format).Read(stream);
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
			m_partDrawer.Update(this);
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
