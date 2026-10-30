using System.Collections.Generic;
using Bpsim.Parts.Simulation;
using Bpsim.Physics;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Physics;
using Unity.Rendering;
using Unity.Transforms;
using UnityEngine;

namespace Bpsim.Parts.Gameplay
{
	/// <summary>
	/// Small helpers shared by the runtime part behaviours: entity lookups,
	/// velocity impulses, tether rendering, collider/alpha toggling and
	/// runtime part spawning.
	/// </summary>
	public static class PartGameplayUtil
	{
		private static UnityEngine.Material s_lineMaterial;

		public static EntityManager Em
		{
			get
			{
				PartManager manager = PartManager.Instance;
				if (manager == null || manager.System == null)
				{
					return default(EntityManager);
				}
				return manager.System.EntityManager;
			}
		}

		public static ref PartSimulatorUnmanaged Simulator
		{
			get
			{
				return ref PartManager.Instance.PartSimulator.Unmanaged;
			}
		}

		public static bool Alive(Entity entity)
		{
			return entity != Entity.Null && Em.Exists(entity);
		}

		public static float3 Position(Entity entity)
		{
			if (!Alive(entity))
			{
				return float3.zero;
			}
			EntityManager em = Em;
			if (em.HasComponent<LocalToWorld>(entity))
			{
				return em.GetComponentData<LocalToWorld>(entity).Position;
			}
			if (em.HasComponent<LocalTransform>(entity))
			{
				return em.GetComponentData<LocalTransform>(entity).Position;
			}
			return float3.zero;
		}

		public static quaternion Rotation(Entity entity)
		{
			if (!Alive(entity))
			{
				return quaternion.identity;
			}
			EntityManager em = Em;
			if (em.HasComponent<LocalToWorld>(entity))
			{
				return em.GetComponentData<LocalToWorld>(entity).Rotation;
			}
			if (em.HasComponent<LocalTransform>(entity))
			{
				return em.GetComponentData<LocalTransform>(entity).Rotation;
			}
			return quaternion.identity;
		}

		public static int ConnectedComponent(Entity entity)
		{
			EntityManager em = Em;
			if (!Alive(entity) || !em.HasComponent<PartConnectedComponent>(entity))
			{
				return -1;
			}
			return em.GetComponentData<PartConnectedComponent>(entity).Index;
		}

		public static PartTypeInfo TypeInfo(Entity entity)
		{
			EntityManager em = Em;
			if (!Alive(entity) || !em.HasComponent<PartTypeValue>(entity))
			{
				return new PartTypeInfo(PartType.Unknown, 0);
			}
			return em.GetComponentData<PartTypeValue>(entity).Value;
		}

		public static void AddVelocity(Entity entity, float3 delta)
		{
			EntityManager em = Em;
			if (!Alive(entity) || !em.HasComponent<PhysicsVelocity>(entity))
			{
				return;
			}
			PhysicsVelocity velocity = em.GetComponentData<PhysicsVelocity>(entity);
			velocity.Linear += delta;
			em.SetComponentData(entity, velocity);
		}

		public static float3 LinearVelocity(Entity entity)
		{
			EntityManager em = Em;
			if (!Alive(entity) || !em.HasComponent<PhysicsVelocity>(entity))
			{
				return float3.zero;
			}
			return em.GetComponentData<PhysicsVelocity>(entity).Linear;
		}

		public static bool TryGetGrid(Entity entity, out int x, out int y, out int rotation, out bool flipped)
		{
			x = 0;
			y = 0;
			rotation = 0;
			flipped = false;
			EntityManager em = Em;
			if (!Alive(entity) || !em.HasComponent<PartTransform>(entity))
			{
				return false;
			}
			PartTransform transform = em.GetComponentData<PartTransform>(entity);
			x = transform.X;
			y = transform.Y;
			rotation = transform.Rotation;
			flipped = transform.Flipped;
			return true;
		}

		public static Vector3 DirectionFromRotation(int rotation, bool eightWay)
		{
			if (eightWay)
			{
				int index = ((rotation % 8) + 8) % 8;
				switch (index)
				{
				case 0:
					return Vector3.right;
				case 1:
					return Vector3.up;
				case 2:
					return Vector3.left;
				case 3:
					return Vector3.down;
				case 4:
					return new Vector3(1f, 1f, 0f).normalized;
				case 5:
					return new Vector3(-1f, 1f, 0f).normalized;
				case 6:
					return new Vector3(-1f, -1f, 0f).normalized;
				default:
					return new Vector3(1f, -1f, 0f).normalized;
				}
			}
			switch (((rotation % 4) + 4) % 4)
			{
			case 0:
				return Vector3.right;
			case 1:
				return Vector3.up;
			case 2:
				return Vector3.left;
			default:
				return Vector3.down;
			}
		}

		public static quaternion GridRotation(int rotation, bool eighteenWay)
		{
			if (eighteenWay)
			{
				int index = ((rotation % 8) + 8) % 8;
				float angle = 90f * index - ((index >= 4) ? 315f : 0f);
				return Quaternion.Euler(0f, 0f, angle);
			}
			return Quaternion.Euler(0f, 0f, 90f * (((rotation % 4) + 4) % 4));
		}

		// ------------------------------------------------------------------
		// Tether / tracer rendering
		// ------------------------------------------------------------------

		private static UnityEngine.Material LineMaterial()
		{
			if (s_lineMaterial != null)
			{
				return s_lineMaterial;
			}
			Shader shader = Shader.Find("Sprites/Default");
			if (shader == null)
			{
				shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
			}
			if (shader == null)
			{
				shader = Shader.Find("Unlit/Color");
			}
			s_lineMaterial = new UnityEngine.Material(shader)
			{
				name = "[PartGameplay] Line"
			};
			return s_lineMaterial;
		}

		public static LineRenderer CreateLine(string name, Color color, float width)
		{
			GameObject gameObject = new GameObject(name);
			gameObject.hideFlags = HideFlags.DontSave;
			LineRenderer line = gameObject.AddComponent<LineRenderer>();
			line.useWorldSpace = true;
			line.positionCount = 2;
			line.startWidth = width;
			line.endWidth = width;
			line.numCapVertices = 2;
			line.numCornerVertices = 2;
			line.alignment = LineAlignment.View;
			line.textureMode = LineTextureMode.Stretch;
			line.material = LineMaterial();
			line.startColor = color;
			line.endColor = color;
			line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
			line.receiveShadows = false;
			line.enabled = false;
			return line;
		}

		public static void SetLine(LineRenderer line, float3 from, float3 to, Color color, float z)
		{
			if (line == null)
			{
				return;
			}
			line.enabled = true;
			line.startColor = color;
			line.endColor = color;
			line.SetPosition(0, new Vector3(from.x, from.y, z));
			line.SetPosition(1, new Vector3(to.x, to.y, z));
		}

		public static void DestroyLine(LineRenderer line)
		{
			if (line != null)
			{
				UnityEngine.Object.Destroy(line.gameObject);
			}
		}

		// ------------------------------------------------------------------
		// Void-light helpers
		// ------------------------------------------------------------------

		public static int SetCollisionEnabled(Entity entity, bool enabled, Dictionary<Entity, CollisionFilter> cache)
		{
			EntityManager em = Em;
			if (!Alive(entity) || !em.HasComponent<PhysicsCollider>(entity))
			{
				return 0;
			}
			PhysicsCollider physicsCollider = em.GetComponentData<PhysicsCollider>(entity);
			if (!physicsCollider.Value.IsCreated)
			{
				return 0;
			}
			ref Unity.Physics.Collider collider = ref physicsCollider.Value.Value;
			if (!enabled)
			{
				CollisionFilter current = collider.GetCollisionFilter();
				if (current.CollidesWith != 0)
				{
					cache[entity] = current;
					current.CollidesWith = 0;
					collider.SetCollisionFilter(current);
					return 1;
				}
				return 0;
			}
			if (cache.TryGetValue(entity, out var original))
			{
				collider.SetCollisionFilter(original);
				cache.Remove(entity);
				return 1;
			}
			return 0;
		}

		public static int SetAlpha(Entity part, float alpha, Dictionary<Entity, float> cache)
		{
			EntityManager em = Em;
			if (!Alive(part) || !em.HasBuffer<LinkedEntityGroup>(part))
			{
				return 0;
			}
			int changed = 0;
			DynamicBuffer<LinkedEntityGroup> group = em.GetBuffer<LinkedEntityGroup>(part);
			for (int i = 0; i < group.Length; i++)
			{
				Entity child = group[i].Value;
				if (!em.HasComponent<Unity.Rendering.MaterialColor>(child))
				{
					continue;
				}
				Unity.Rendering.MaterialColor color = em.GetComponentData<Unity.Rendering.MaterialColor>(child);
				if (Mathf.Approximately(color.Value.w, alpha))
				{
					continue;
				}
				if (!cache.ContainsKey(child))
				{
					cache[child] = color.Value.w;
				}
				color.Value.w = alpha;
				em.SetComponentData(child, color);
				changed++;
			}
			return changed;
		}

		public static void RestoreAlphaFor(Entity part, Dictionary<Entity, float> cache)
		{
			EntityManager em = Em;
			if (!Alive(part) || !em.HasBuffer<LinkedEntityGroup>(part))
			{
				return;
			}
			DynamicBuffer<LinkedEntityGroup> group = em.GetBuffer<LinkedEntityGroup>(part);
			for (int i = 0; i < group.Length; i++)
			{
				Entity child = group[i].Value;
				if (!cache.TryGetValue(child, out var alpha))
				{
					continue;
				}
				if (em.Exists(child) && em.HasComponent<Unity.Rendering.MaterialColor>(child))
				{
					Unity.Rendering.MaterialColor color = em.GetComponentData<Unity.Rendering.MaterialColor>(child);
					color.Value.w = alpha;
					em.SetComponentData(child, color);
				}
				cache.Remove(child);
			}
		}

		public static void RestoreAlpha(Dictionary<Entity, float> cache)
		{
			EntityManager em = Em;
			foreach (KeyValuePair<Entity, float> item in cache)
			{
				if (!em.Exists(item.Key) || !em.HasComponent<Unity.Rendering.MaterialColor>(item.Key))
				{
					continue;
				}
				Unity.Rendering.MaterialColor color = em.GetComponentData<Unity.Rendering.MaterialColor>(item.Key);
				color.Value.w = item.Value;
				em.SetComponentData(item.Key, color);
			}
			cache.Clear();
		}

		// ------------------------------------------------------------------
		// Runtime part spawning (multipart generators)
		// ------------------------------------------------------------------

		public static Entity SpawnPart(PartTypeInfo info, int coordX, int coordY, int rotation, bool flipped, ref PartSimulatorUnmanaged simulator, byte layer = PartBuildLayer.MinLayer)
		{
			EntityManager em = Em;
			PartManager manager = PartManager.Instance;
			Entity prefab = manager.EntitySpawner.FindPrefab(info);
			if (prefab == Entity.Null)
			{
				return Entity.Null;
			}
			em.CompleteAllTrackedJobs();
			PartExtensionData extension;
			if (!manager.Factory.PartExtensionMap.TryGetValue(info, out extension))
			{
				extension = new PartExtensionData(1f, 0f, 0f, false, false, BitDirection.None, 600f);
			}
			bool eightWay = BasePart.IsEightWay(info);
			quaternion rotationQuaternion = GridRotation(rotation, eightWay);
			if (flipped && BasePart.CanFlip(info))
				rotationQuaternion = Quaternion.Euler(rotation % 2 == 1 ? 180f : 0f, rotation % 2 == 0 ? 180f : 0f, rotation * 90f);
			if (info.PartType == PartType.TNT) rotationQuaternion = GridRotation(rotation % 4, false);
			float3 position = new float3(coordX, coordY, -0.09f);
			Entity part = em.Instantiate(prefab);
			em.SetName(part, "GeneratedPart_" + info.PartType + "_" + coordX + "_" + coordY);
			em.SetComponentData(part, new PartTransform
			{
				X = coordX,
				Y = coordY,
				Rotation = rotation,
				Flipped = flipped
			});
			if (!em.HasComponent<PartSceneID>(part))
			{
				em.AddComponentData(part, new PartSceneID
				{
					Value = -1
				});
			}
			else
			{
				em.SetComponentData(part, new PartSceneID
				{
					Value = -1
				});
			}
			if (em.HasComponent<LocalTransform>(part))
			{
				em.SetComponentData(part, LocalTransform.FromPositionRotationScale(position, rotationQuaternion, 1f));
			}
			if (em.HasComponent<LocalToWorld>(part))
			{
				em.SetComponentData(part, new LocalToWorld
				{
					Value = float4x4.TRS(position, rotationQuaternion, new float3(1f, 1f, 1f))
				});
			}
			if (em.HasComponent<PhysicsCollider>(part))
			{
				PhysicsCollider collider = em.GetComponentData<PhysicsCollider>(part);
				if (collider.Value.IsCreated)
				{
					// Prefab colliders are shared. Generated parts need independent filters too.
					var isolated = PartPhysics.CreateScaled(collider.Value, new float2(1f, 1f));
					if (isolated.IsCreated)
					{
						collider.Value = isolated;
						em.SetComponentData(part, collider);
						em.AddComponent<PartIsolatedCollider>(part);
						simulator.TrackScaledCollider(isolated);
					}
					PartPhysics.AddRigidbody(em, part, false, collider.Value.Value.MassProperties, info.PartType == PartType.Umbrella ? .5f : extension.Mass, 0.2f, 0f);
					em.AddComponentData(part, Constraint2D.CreateFromTransform(LocalTransform.FromPositionRotationScale(position, rotationQuaternion, 1f)));
					PartPhysics.EnableGraphicalInterpolation(em, part, new RigidTransform(rotationQuaternion, position));
				}
			}
			em.AddSharedComponent(part, new PhysicsWorldIndex(0u));
			// 生成体落在生成器所在的建造层：PartMap 的第三维是层号，
			// 公共层是 PartBuildLayer.MinLayer = 1（不是 0）。
			PartBuildLayerUtility.SetLayer(em, part, layer);
			simulator.Parts.Add(part);
			simulator.PartMap.Set(coordX, coordY, layer, new PartGridInfo(Entity.Null, part, 0));
			if (info.PartType == PartType.TNT)
			{
				// Simulation-time spawns do not pass through the build-only orientation system.
				var linked = em.GetBuffer<LinkedEntityGroup>(part);
				for (int i = 0; i < linked.Length; i++)
				{
					Entity child = linked[i].Value;
					if (!em.HasComponent<TNTVisualization>(child)) continue;
					var transform = em.GetComponentData<LocalTransform>(child);
					transform.Rotation = math.mul(em.GetComponentData<TNTVisualization>(child).BaseRotation,
						quaternion.RotateZ(rotation >= 4 ? math.PI / 4f : 0f));
					em.SetComponentData(child, transform);
				}
			}
			PartGameplayManager.Instance?.RegisterSpawnedPart(part);
			return part;
		}

		public static Entity CreateFixedJoint(Entity entityA, float3 positionA, quaternion rotationA, Entity entityB, float3 positionB, quaternion rotationB, float maxImpulse)
		{
			EntityManager em = Em;
			RigidTransform transformA = new RigidTransform(rotationA, positionA);
			RigidTransform transformB = new RigidTransform(rotationB, positionB);
			RigidTransform midpoint = new RigidTransform
			{
				rot = quaternion.identity,
				pos = 0.5f * (positionA + positionB)
			};
			BodyFrame frameA = new BodyFrame(math.mul(math.inverse(transformA), midpoint));
			BodyFrame frameB = new BodyFrame(math.mul(math.inverse(transformB), midpoint));
			PhysicsJoint joint = PartPhysics.CreateFixedJoint(frameA, frameB);
			joint.SetImpulseEventThresholdAllConstraints(maxImpulse, float.PositiveInfinity);
			Entity jointEntity = em.CreateEntity();
			em.SetName(jointEntity, "GameplayFixedJoint");
			em.AddSharedComponent(jointEntity, new PhysicsWorldIndex(0u));
			em.AddComponentData(jointEntity, joint);
			em.AddComponentData(jointEntity, new PhysicsConstrainedBodyPair(entityA, entityB, false));
			em.AddComponentData(jointEntity, new PartJointInfo
			{
				Type = PartJointType.Normal,
				State = 0
			});
			return jointEntity;
		}

		/// <summary>
		/// 切换 BPSS <c>PointLight</c> 的 active/inactive 图：点亮时只留
		/// <c>ActivePointLightSprite</c>，熄灭时只留 <c>InactivePointLightSprite</c>。
		/// </summary>
		/// <param name="full">
		/// AutoControlLight（PartIndex 6）用：斜向型号（<c>gridRotation &gt;= 4</c>）走
		/// <c>*FullPointLightSprite</c> 那一对，方型号走普通那一对；两对互斥显示。
		/// 其它灯传 false。
		/// </param>
		/// <returns>本次真的改了可见性的子实体数（便于 QA 断言与 <c>Describe()</c> 统计）。</returns>
		public static int SetPointLightVisual(Entity root, bool activated, bool full)
		{
			EntityManager em = Em;
			if (!em.Exists(root) || !em.HasBuffer<LinkedEntityGroup>(root))
			{
				return 0;
			}
			// 循环里 AddComponent/RemoveComponent 是结构变更，DynamicBuffer 会立刻失效，
			// 所以每轮重新取一次（同 CircuitSystem.ApplySwitchVisual 的写法）。
			int length = em.GetBuffer<LinkedEntityGroup>(root).Length;
			int changed = 0;
			for (int i = 0; i < length; i++)
			{
				DynamicBuffer<LinkedEntityGroup> group = em.GetBuffer<LinkedEntityGroup>(root);
				Entity child = group[i].Value;
				if (!em.Exists(child) || !em.HasComponent<PointLightVisual>(child))
				{
					continue;
				}
				PointLightVisual visual = em.GetComponentData<PointLightVisual>(child);
				bool visible = visual.Full == full && visual.Active == activated;
				bool hidden = em.HasComponent<DisableRendering>(child);
				if (visible && hidden)
				{
					em.RemoveComponent<DisableRendering>(child);
					changed++;
				}
				else if (!visible && !hidden)
				{
					em.AddComponent<DisableRendering>(child);
					changed++;
				}
			}
			return changed;
		}

		/// <summary>
		/// 把 BPSS <c>SpotLight</c> 光锥容器的缩放写成 <paramref name="scale"/>。
		/// </summary>
		/// <remarks>
		/// 原版 <c>m_lightConeContainer.localScale</c> 是 (target, 1, 1) 这种非等比缩放，
		/// ECS 侧 <c>LocalTransform</c> 只能存等比，非等比必须走 <c>PostTransformScale</c>
		/// （和 <c>MechanicalGateSystem.UpdateVisuals</c> 处理闸门子物体的做法相同）。
		/// </remarks>
		public static int SetSpotLightCone(Entity root, float3 scale)
		{
			EntityManager em = Em;
			if (!em.Exists(root) || !em.HasBuffer<LinkedEntityGroup>(root))
			{
				return 0;
			}
			int length = em.GetBuffer<LinkedEntityGroup>(root).Length;
			int changed = 0;
			for (int i = 0; i < length; i++)
			{
				DynamicBuffer<LinkedEntityGroup> group = em.GetBuffer<LinkedEntityGroup>(root);
				Entity child = group[i].Value;
				if (!em.Exists(child) || !em.HasComponent<SpotLightCone>(child))
				{
					continue;
				}
				// 结构变更（AddComponentData）会让上面的 buffer 句柄失效，所以这里只在
				// 缺组件时新增一次；之后都是纯数据写入。
				// 注意 <c>LocalTransform.Scale</c> 必须归 1：prefab 里 Container 的
				// localScale 是 (0,0,0)，LocalToWorld 是「LocalTransform 的缩放 × PostTransformScale」，
				// 留着 0 会让光锥永远是 0 倍（同 MechanicalGateSystem.UpdateVisuals 的写法）。
				if (em.HasComponent<LocalTransform>(child))
				{
					LocalTransform transform = em.GetComponentData<LocalTransform>(child);
					if (transform.Scale != 1f)
					{
						transform.Scale = 1f;
						em.SetComponentData(child, transform);
					}
				}
				PostTransformScale post = new PostTransformScale
				{
					Value = float3x3.Scale(scale)
				};
				if (em.HasComponent<PostTransformScale>(child))
				{
					em.SetComponentData(child, post);
				}
				else
				{
					em.AddComponentData(child, post);
				}
				changed++;
			}
			return changed;
		}

		/// <summary>
		/// 把燃料箱的液面摆到 <paramref name="ratio"/>（0..1）对应的位置，返回写入的子实体数。
		/// </summary>
		/// <remarks>
		/// 对应 BPSS <c>FuelBox.UpdateFuelSprite</c>：
		/// <c>localPosition.y = -0.26f * (1 - ratio)</c>、<c>localScale = (1, ratio, 1)</c>。
		/// bpsim 侧的非等比缩放只能走 <c>PostTransformScale</c>（<c>LocalTransform.Scale</c>
		/// 只能存等比），而位置改的是 <c>LocalTransform.Position.y</c>，两者互不干扰。
		/// </remarks>
		public static int SetFuelLevel(Entity root, float ratio)
		{
			EntityManager em = Em;
			if (!em.Exists(root) || !em.HasBuffer<LinkedEntityGroup>(root))
			{
				return 0;
			}
			float clamped = math.saturate(ratio);
			int length = em.GetBuffer<LinkedEntityGroup>(root).Length;
			int changed = 0;
			for (int i = 0; i < length; i++)
			{
				DynamicBuffer<LinkedEntityGroup> group = em.GetBuffer<LinkedEntityGroup>(root);
				Entity child = group[i].Value;
				if (!em.Exists(child) || !em.HasComponent<FuelLevelVisual>(child))
				{
					continue;
				}
				if (em.HasComponent<LocalTransform>(child))
				{
					LocalTransform transform = em.GetComponentData<LocalTransform>(child);
					// prefab 里的 localScale 是 (1,1,1)，这里统一归 1，缩放全部交给 PostTransformScale。
					transform.Scale = 1f;
					transform.Position.y = -0.26f * (1f - clamped);
					em.SetComponentData(child, transform);
				}
				PostTransformScale post = new PostTransformScale
				{
					Value = float3x3.Scale(new float3(1f, math.max(clamped, 0.02f), 1f))
				};
				if (em.HasComponent<PostTransformScale>(child))
				{
					em.SetComponentData(child, post);
				}
				else
				{
					em.AddComponentData(child, post);
				}
				changed++;
			}
			return changed;
		}

		/// <summary>
		/// 换燃料管的贴图（自动管按邻居组合切换），返回写入的子实体数。
		/// </summary>
		/// <remarks>
		/// 换图就是写 <c>SpriteID</c>：<c>SpriteSystem</c> 是 Burst 的 <c>IJobChunk</c>，
		/// 按 <c>SpriteID.Value</c> 把 UV/顶点写进 <c>MeshRect</c>/<c>MeshUVRect</c>，
		/// 所以不需要碰任何 GameObject。
		/// </remarks>
		public static int SetFuelTubeSprite(Entity root, string spriteName)
		{
			EntityManager em = Em;
			if (!em.Exists(root) || !em.HasBuffer<LinkedEntityGroup>(root))
			{
				return 0;
			}
			if (Bpsim.Rendering.SpriteManager.Instance == null || !Bpsim.Rendering.SpriteManager.Instance.TryGetID(spriteName, out int spriteID))
			{
				return 0;
			}
			int length = em.GetBuffer<LinkedEntityGroup>(root).Length;
			int changed = 0;
			for (int i = 0; i < length; i++)
			{
				DynamicBuffer<LinkedEntityGroup> group = em.GetBuffer<LinkedEntityGroup>(root);
				Entity child = group[i].Value;
				if (!em.Exists(child) || !em.HasComponent<FuelTubeVisual>(child) || !em.HasComponent<Bpsim.Rendering.SpriteID>(child))
				{
					continue;
				}
				Bpsim.Rendering.SpriteID id = em.GetComponentData<Bpsim.Rendering.SpriteID>(child);
				if (id.Value != spriteID)
				{
					id.Value = spriteID;
					em.SetComponentData(child, id);
					changed++;
				}
			}
			return changed;
		}
	}
}
