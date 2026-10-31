using System;
using Bpsim.Parts.Simulation;
using Bpsim.Parts.Skin;
using Bpsim.Rendering;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Physics;
using Unity.Rendering;
using Unity.Rendering.Authoring;
using Unity.Transforms;
using UnityEngine;

namespace Bpsim.Parts
{
	internal static class PartEntityFactory
	{
		public class Context
		{
			public EntityManager EntityManager { get; private set; }

			public RenderMeshArray RenderMeshArray { get; private set; }

			public Context(EntityManager entityManager, UnityEngine.Material material, Mesh mesh)
			{
				EntityManager = entityManager;
				RenderMeshArray = new RenderMeshArray(new UnityEngine.Material[1] { material }, new Mesh[1] { mesh });
			}
		}

		public static Entity Create(Context context, GameObject gameObject, bool prefab)
		{
			Entity entity = context.EntityManager.CreateEntity();
			context.EntityManager.SetName(entity, gameObject.name);
			CreateComponents(context, gameObject.GetComponents<Component>(), entity);
			if (prefab)
			{
				context.EntityManager.AddComponent<Prefab>(entity);
			}
			context.EntityManager.AddBuffer<LinkedEntityGroup>(entity).Add(entity);
			// 部件根实体要挂 PropagateLocalToWorld：自由缩放写在根的 PostTransformScale 上，
			// 而 Unity.Transforms 默认的子物体路径只认 WorldTransform 的「等比」Scale，
			// 非等比缩放会被整个丢掉（LocalToWorldSystem.ChildLocalToWorldFromTransformData）。
			// 打了这个标签，子渲染实体才改走矩阵路径，继承根真正的 LocalToWorld。
			// 部件模板的根物体是 ManagedPart 所在的物体（Part_TNT_0 / Part_SpringBoxingGlove_0 …），
			// 拳套、TNT、车轮这些的外观挂在它的子物体上（Visualization / WheelSprite），
			// 缺了标签「整车缩放」在建造界面里就对它们不生效——模拟期由
			// PartPhysics.EnableGraphicalInterpolation 补上，才显得只有运行界面正常。
			context.EntityManager.AddComponent<PropagateLocalToWorld>(entity);
			for (int i = 0; i < gameObject.transform.childCount; i++)
			{
				CreateChild(context, gameObject.transform.GetChild(i).gameObject, entity, entity, prefab);
			}
			BuildCompoundCollider(context, entity);
			EnsureSandbagCollider(context, entity);
			if (context.EntityManager.HasComponent<PartTypeValue>(entity) &&
				FrameShape.Supports(context.EntityManager.GetComponentData<PartTypeValue>(entity).Value) &&
				context.EntityManager.HasComponent<PhysicsCollider>(entity))
			{
				context.EntityManager.AddComponentData(entity, default(FrameShape));
				context.EntityManager.AddComponentData(entity, new FrameShapeOriginalCollider { Value = context.EntityManager.GetComponentData<PhysicsCollider>(entity).Value });
				context.EntityManager.AddComponent<FrameShapeDirty>(entity);
				context.EntityManager.SetComponentEnabled<FrameShapeDirty>(entity, false);
			}
			return entity;
		}

		/// <summary>
		/// 沙袋的预制体里没有任何 Collider，而 <c>PartSimulator</c> 只给「有
		/// <c>PhysicsCollider</c> 的部件」加刚体（<c>PartSimulator.cs:120</c>），
		/// 结果沙袋会变成一个不动的静态件。
		/// </summary>
		/// <remarks>
		/// BPSS <c>Sandbag.Initialize()</c> 里正是这段：
		/// <c>if (!GetComponent&lt;SphereCollider&gt;()) { AddComponent&lt;SphereCollider&gt;().radius = 0.13f;
		/// .center = new Vector3(0f, -0.1f, 0f); }</c> —— 这里把它前移到烘焙阶段，
		/// 让后续的刚体装配（质量 1.1 / 1.5、Constraint2D、图形插值）走既有管线。
		/// </remarks>
		private static void EnsureSandbagCollider(Context context, Entity entity)
		{
			if (context.EntityManager.HasComponent<PhysicsCollider>(entity))
			{
				return;
			}
			if (!context.EntityManager.HasComponent<PartTypeValue>(entity))
			{
				return;
			}
			if (!Sandbag.IsSandbag(context.EntityManager.GetComponentData<PartTypeValue>(entity).Type))
			{
				return;
			}
			context.EntityManager.AddComponentData(entity, new PhysicsCollider
			{
				Value = PartPhysics.CreateSphere(new float3(0f, -0.1f, 0f), 0.13f)
			});
		}

		private static void BuildCompoundCollider(Context context, Entity entity)
		{
			DynamicBuffer<Entity> dynamicBuffer = context.EntityManager.GetBuffer<LinkedEntityGroup>(entity).Reinterpret<Entity>();
			NativeList<CompoundCollider.ColliderBlobInstance> nativeList = new NativeList<CompoundCollider.ColliderBlobInstance>(4, Allocator.TempJob);
			try
			{
				for (int i = 1; i < dynamicBuffer.Length; i++)
				{
					Entity entity2 = dynamicBuffer[i];
					if (context.EntityManager.HasComponent<PhysicsCollider>(entity2))
					{
						PhysicsCollider componentData = context.EntityManager.GetComponentData<PhysicsCollider>(entity2);
						CompoundCollider.ColliderBlobInstance value = new CompoundCollider.ColliderBlobInstance
						{
							CompoundFromChild = RigidTransform.identity,
							Collider = componentData.Value,
							Entity = entity2
						};
						nativeList.Add(in value);
					}
				}
				if (nativeList.Length > 0)
				{
					if (context.EntityManager.HasComponent<PhysicsCollider>(entity))
					{
						PhysicsCollider componentData2 = context.EntityManager.GetComponentData<PhysicsCollider>(entity);
						CompoundCollider.ColliderBlobInstance value = new CompoundCollider.ColliderBlobInstance
						{
							CompoundFromChild = RigidTransform.identity,
							Collider = componentData2.Value,
							Entity = entity
						};
						nativeList.Add(in value);
						BlobAssetReference<Unity.Physics.Collider> value2 = CompoundCollider.Create(nativeList.AsArray());
						context.EntityManager.SetComponentData(entity, new PhysicsCollider
						{
							Value = value2
						});
					}
					else
					{
						BlobAssetReference<Unity.Physics.Collider> value3 = CompoundCollider.Create(nativeList.AsArray());
						context.EntityManager.AddComponentData(entity, new PhysicsCollider
						{
							Value = value3
						});
					}
				}
			}
			finally
			{
				((IDisposable)nativeList/*cast due to .constrained prefix*/).Dispose();
			}
		}

		public static Entity CreateChild(Context context, GameObject gameObject, Entity rootEntity, Entity parentEntity, bool prefab)
		{
			if (gameObject.name == "Foreground")
			{
				return Entity.Null;
			}
			Entity entity = context.EntityManager.CreateEntity();
			context.EntityManager.SetName(entity, gameObject.name);
			context.EntityManager.AddComponentData(entity, new Parent
			{
				Value = parentEntity
			});
			context.EntityManager.GetBuffer<LinkedEntityGroup>(rootEntity).Add(entity);
			CreateChildMarker(context, gameObject.name, entity);
			CreateComponents(context, gameObject.GetComponents<Component>(), entity);
			if (parentEntity == rootEntity && context.EntityManager.HasComponent<CircuitPart>(rootEntity))
			{
				int circuitIndex = context.EntityManager.GetComponentData<CircuitPart>(rootEntity).Index;
				if (((circuitIndex == 1 || circuitIndex == 3) && (gameObject.name == "WireX" || gameObject.name == "WireY")) ||
					((circuitIndex == 38 || circuitIndex == 39) && gameObject.name == "InterfaceSprite"))
					context.EntityManager.AddComponentData(entity, new Circuits.CircuitVisualNode {
						Kind = (byte)(gameObject.name == "WireX" ? 0 : gameObject.name == "WireY" ? 1 : 2), AppliedMode = -1 });
			}
			if (gameObject.name == "Visualization" && parentEntity == rootEntity
				&& context.EntityManager.HasComponent<PartTypeValue>(rootEntity)
				&& context.EntityManager.GetComponentData<PartTypeValue>(rootEntity).Type == PartType.TNT)
				context.EntityManager.AddComponentData(entity, new TNTVisualization { Root = rootEntity, BaseRotation = gameObject.transform.localRotation });
			if (gameObject.name == "UmbrellaVisualization" && parentEntity == rootEntity
				&& context.EntityManager.GetComponentData<PartTypeValue>(rootEntity).Type == PartType.Umbrella)
				context.EntityManager.AddComponentData(entity, new Gameplay.UmbrellaVisual { BasePosition = gameObject.transform.localPosition });
			if (prefab)
			{
				context.EntityManager.AddComponent<Prefab>(entity);
			}
			for (int i = 0; i < gameObject.transform.childCount; i++)
			{
				CreateChild(context, gameObject.transform.GetChild(i).gameObject, rootEntity, entity, prefab);
			}
			return entity;
		}

		/// <summary>
		/// 给带特殊含义的子物体打标记组件。名字是模板 JSON 里唯一稳定的信息，
		/// 而运行时 <c>EntityManager.GetName</c> 在发行版里恒为空串，所以必须在建实体时
		/// 把名字翻译成组件，之后由系统按组件查找。
		/// </summary>
		private static void CreateChildMarker(Context context, string name, Entity entity)
		{
			switch (name)
			{
			case "WheelPivot":
				context.EntityManager.AddComponentData(entity, new WheelPivot
				{
					Fake = false
				});
				break;
			case "FakeWheelPivot":
				context.EntityManager.AddComponentData(entity, new WheelPivot
				{
					Fake = true
				});
				break;
			case "ActiveGearboxSprite":
				context.EntityManager.AddComponentData(entity, new GearboxVisual
				{
					Active = true
				});
				break;
			case "InactiveGearboxSprite":
				context.EntityManager.AddComponentData(entity, new GearboxVisual
				{
					Active = false
				});
				break;
			case "ActiveSprite":
				context.EntityManager.AddComponentData(entity, new SwitchVisual
				{
					Active = true
				});
				break;
			case "GateCollider":
				context.EntityManager.AddComponentData(entity, new GateVisual
				{
					Collider = true
				});
				break;
			case "GateRenderer":
				context.EntityManager.AddComponentData(entity, new GateVisual
				{
					Collider = false
				});
				break;
			case "InactiveSprite":
				context.EntityManager.AddComponentData(entity, new SwitchVisual
				{
					Active = false
				});
				break;
			case "SandbagVisualization":
				// BPSS Sandbag.Initialize() 打开的那一支（N 个气球袋）。
				context.EntityManager.AddComponentData(entity, new SandbagVisual
				{
					Actual = true
				});
				break;
			case "Sandbag2Visualization":
			case "Sandbag3Visualization":
				// 网格/图标替身，Initialize() 之后要关掉。
				context.EntityManager.AddComponentData(entity, new SandbagVisual
				{
					Actual = false
				});
				break;
			case "ActivePointLightSprite":
			case "InactivePointLightSprite":
			case "ActiveFullPointLightSprite":
			case "InactiveFullPointLightSprite":
				// BPSS PointLight 的 activeSprite / inactiveSprite；带 Full 的是
				// AutoControlLight（PartIndex 6）斜向型号才用的第二对。
				context.EntityManager.AddComponentData(entity, new PointLightVisual
				{
					Active = name.StartsWith("Active", System.StringComparison.Ordinal),
					Full = name.Contains("Full")
				});
				break;
			case "Container":
				// SpotLight 的光锥容器（BPSS m_lightConeContainer），灯泡开关时缩放它。
				context.EntityManager.AddComponentData(entity, default(SpotLightCone));
				break;
			case "FuelSprite":
			case "Fuel":
				// 燃料箱的液面（BPSS FuelBox.m_fuelSprite）：随油量缩放 + 下移。
				// 燃料箱在 BPSS 里是 JetEngine 组的 _05_SET 皮肤，那一份预制体里子物体叫
				// "Fuel"（名字直接来自原版），独立克隆出来的那份叫 "FuelSprite"，两个都认。
				context.EntityManager.AddComponentData(entity, default(FuelLevelVisual));
				break;
			case "TubeVisual":
				// 燃料管的贴图（自动管按邻居换图 = 写这个实体的 SpriteID）。
				context.EntityManager.AddComponentData(entity, default(FuelTubeVisual));
				break;
			}
		}

		public static void CreateComponents(Context context, Component[] components, Entity entity)
		{
			foreach (Component component in components)
			{
				CreateComponent(context, component, entity);
			}
		}

		public static void CreateComponent(Context context, Component component, Entity entity)
		{
			if (!(component is Transform component2))
			{
				if (!(component is Renderer component3))
				{
					if (!(component is SpriteBase component4))
					{
						if (!(component is UnityEngine.Collider component5))
						{
							if (component is ManagedPart component6)
							{
								CreatePart(context, component6, entity);
							}
						}
						else
						{
							CreateColllider(context, component5, entity);
						}
					}
					else
					{
						CreateSprite(context, component4, entity);
					}
				}
				else
				{
					CreateRenderer(context, component3, entity);
				}
			}
			else
			{
				CreateTransform(context, component2, entity);
			}
		}

		public static void CreateTransform(Context context, Transform component, Entity entity)
		{
			Vector3 localScale = component.localScale;
			bool flag = System.Math.Abs(localScale.x - localScale.y) < 1E-05f && System.Math.Abs(localScale.y - localScale.z) < 1E-05f && System.Math.Abs(localScale.z - localScale.x) < 1E-05f;
			LocalTransform componentData = LocalTransform.FromPositionRotationScale(component.localPosition, component.localRotation, flag ? localScale.x : 1f);
			context.EntityManager.AddComponentData(entity, componentData);
			context.EntityManager.AddComponentData(entity, new LocalToWorld
			{
				Value = float4x4.identity
			});
			if (!flag)
			{
				context.EntityManager.AddComponentData(entity, new PostTransformScale
				{
					Value = float3x3.Scale(localScale)
				});
			}
		}

		public static void CreateRenderer(Context context, Renderer component, Entity entity)
		{
			Color color = component.sharedMaterial.color;
			Unity.Rendering.Authoring.MaterialColor component2 = component.GetComponent<Unity.Rendering.Authoring.MaterialColor>();
			if (component2 != null)
			{
				color = component2.color;
			}
			context.EntityManager.AddComponentData(entity, new Unity.Rendering.MaterialColor
			{
				Value = new float4(color.r, color.g, color.b, color.a)
			});
			context.EntityManager.AddComponentData(entity, PartLayerTint.White);
			context.EntityManager.AddComponent<PartRenderInfo>(entity);
			if (!component.enabled)
			{
				context.EntityManager.AddComponent<DisableRendering>(entity);
			}
		}

		public static void CreateSprite(Context context, SpriteBase component, Entity entity)
		{
			if (SpriteManager.Instance.TryGetID(component.SpriteName, out var id))
			{
				context.EntityManager.AddComponentData(entity, new SpriteID
				{
					Value = id
				});
				context.EntityManager.AddComponentData(entity, new MeshRect
				{
					Value = new float4(0f, 0f, 1f, 1f)
				});
				context.EntityManager.AddComponentData(entity, new MeshUVRect
				{
					Value = new float4(0f, 0f, 1f, 1f)
				});
				context.EntityManager.AddComponentData(entity, new BlendFactor
				{
					Value = new float4(1f, 0f, 0f, 0f)
				});
			}
		}

		public static void CreateColllider(Context context, UnityEngine.Collider component, Entity entity)
		{
			BlobAssetReference<Unity.Physics.Collider> value;
			if (!(component is UnityEngine.BoxCollider boxCollider))
			{
				if (!(component is UnityEngine.SphereCollider sphereCollider))
				{
					if (!(component is UnityEngine.CapsuleCollider capsuleCollider))
					{
						throw new InvalidCastException();
					}
					float3 @float = new float3(0f, 0.5f * capsuleCollider.height - capsuleCollider.radius, 0f);
					value = PartPhysics.CreateCapsule((float3)capsuleCollider.center + @float, (float3)capsuleCollider.center - @float, capsuleCollider.radius);
				}
				else
				{
					value = PartPhysics.CreateSphere(sphereCollider.center, sphereCollider.radius);
				}
			}
			else
			{
				value = PartPhysics.CreateBox(boxCollider.center, boxCollider.size);
			}
			context.EntityManager.AddComponentData(entity, new PhysicsCollider
			{
				Value = value
			});
		}

		public static void CreatePart(Context context, ManagedPart component, Entity entity)
		{
			PartTypeInfo partTypeInfo = new PartTypeInfo(component.PartType, component.PartIndex);
			context.EntityManager.AddComponent(entity, GetPartComponentTypes(partTypeInfo));
			context.EntityManager.SetComponentData(entity, new PartTypeValue
			{
				Type = partTypeInfo.PartType,
				Index = partTypeInfo.PartIndex
			});
			context.EntityManager.SetComponentData(entity, new PartExtensionComponent
			{
				// 变体的索引不在 PartExtensionMap 的区间里，必须走 GetExtension 回退到基部件。
				Value = PartManager.Instance.Factory.GetExtension(partTypeInfo)
			});
			if (BasePart.BelongsTo(partTypeInfo, BasePart.ColoredFrames))
			{
				context.EntityManager.SetComponentData(entity, new ColoredFrame
				{
					IsTransparent = false,
					HasChanged = true,
					Color = ColoredFrame.GetColor(partTypeInfo)
				});
			}
			else if (BasePart.BelongsTo(partTypeInfo, BasePart.TransparentFrames))
			{
				context.EntityManager.SetComponentData(entity, new ColoredFrame
				{
					IsTransparent = true,
					HasChanged = true,
					Color = ColoredFrame.GetColor(partTypeInfo)
				});
			}
			if (IsWheelPart(partTypeInfo.PartType))
			{
				context.EntityManager.SetComponentData(entity, Wheel.Create(partTypeInfo));
			}
			else if (partTypeInfo.PartType == PartType.Gearbox)
			{
				context.EntityManager.SetComponentData(entity, default(Gearbox));
			}
			else if (partTypeInfo.PartType == PartType.MechanicalPart)
			{
				// 机械闸门：不是电路元件，自带展开/收回状态（见 MechanicalGateSystem）。
				context.EntityManager.SetComponentData(entity, MechanicalGate.Create(partTypeInfo));
			}
			else if (IsCircuitPart(partTypeInfo.PartType))
			{
				context.EntityManager.SetComponentData(entity, CircuitPart.Create(partTypeInfo));
			}
			else if (Sandbag.IsSandbag(partTypeInfo.PartType))
			{
				// 沙袋：BPSS 在 Initialize() 里自建 SpringJoint 吊在上级框架上（见 SandbagSystem）。
				context.EntityManager.SetComponentData(entity, Sandbag.Create(partTypeInfo));
			}
			else if (Rope.IsRope(partTypeInfo))
			{
				// 绳索：节点链在 RopeSystem 首次更新时按部件朝向铺开（BPSS 的 Rope.Create）。
				context.EntityManager.SetComponentData(entity, Rope.Create(partTypeInfo));
				DynamicBuffer<RopeNode> nodes = context.EntityManager.GetBuffer<RopeNode>(entity);
				nodes.ResizeUninitialized(Rope.SegmentCount);
			}
			else if (partTypeInfo.PartType == PartType.Egg)
			{
				// 鸟蛋：0 普通；1 重力 / 2 失重 / 3 穿透 / 4 虚空（BPSS Egg.cs）；
				// 5 异形胶（BPSS AlienEgg.cs）。效果由 EggSystem 每帧调和。
				context.EntityManager.SetComponentData(entity, Egg.Create(partTypeInfo));
			}
			else if (partTypeInfo.PartType == PartType.SkinPart)
			{
				// 蒙皮：形状是纯数据组件，网格/凸包由 SkinSheetSystem 按 Version 重建。
				context.EntityManager.SetComponentData(entity, SkinSheetDefaults.Create());
				context.EntityManager.SetComponentData(entity, new SkinSheetMesh
				{
					Handle = -1,
					BuiltVersion = int.MinValue,
					BuiltScale = new float2(1f, 1f)
				});
			}
			else if (partTypeInfo.PartType == PartType.JetEngine)
			{
				// 燃料管是 JetEngine 组的皮肤（BPSS _03/_04/_06_SET = FuelTube.cs），那一份
				// prefab 把贴图挂在部件根上（没有独立的 TubeVisual 子物体），所以标记也加在根上：
				// 阀门开合换图时 FuelTubeBehavior → SetFuelTubeSprite 找的就是「带
				// FuelTubeVisual + SpriteID」的实体。索引 2/3/5 之外的皮肤不是管，不加。
				if (partTypeInfo.PartIndex == 2 || partTypeInfo.PartIndex == 3 || partTypeInfo.PartIndex == 5)
				{
					context.EntityManager.AddComponentData(entity, default(FuelTubeVisual));
				}
			}
		}

		/// <summary>轮子族：BPSS 侧分别由 CartWheel.cs / MotorWheel.cs / StickyWheel.cs / OffRoadWheel.cs 驱动。</summary>
		public static bool IsWheelPart(PartType partType)
		{
			return partType == PartType.CartWheel || partType == PartType.NormalWheel || partType == PartType.SmallWheel || partType == PartType.MotorWheel || partType == PartType.StickyWheel;
		}

		/// <summary>
		/// 电路部件：BPSS 侧是 20 多个独立脚本（WirePart / ResistorPart / VccPart / …），
		/// bpsim 把整族压进 <see cref="PartType.ElectricalPart"/>（0-48）。
		/// <see cref="PartType.MechanicalPart"/>（0-4）<b>不是</b>电路元件：BPSS 的
		/// <c>MechanicalPart</c> 只是空壳，闸门行为全在 <c>MechanicalGate</c> 里。
		/// </summary>
		public static bool IsCircuitPart(PartType partType)
		{
			return partType == PartType.ElectricalPart;
		}

		private static ComponentTypeSet GetPartComponentTypes(PartTypeInfo typeInfo)
		{
			FixedList128Bytes<ComponentType> types = new FixedList128Bytes<ComponentType>
			{
				ComponentType.ReadOnly<PartTag>(),
				ComponentType.ReadOnly<PartTypeValue>(),
				ComponentType.ReadWrite<PartTransform>(),
				ComponentType.ReadWrite<ContainedPart>(),
				ComponentType.ReadWrite<PartContainerValue>(),
				ComponentType.ReadWrite<PartConnectedComponent>(),
				ComponentType.ReadWrite<PartBuildLayer>(),
				ComponentType.ReadWrite<PartExtensionComponent>()
			};
			if (BasePart.BelongsTo(typeInfo, BasePart.ColoredFrames))
			{
				types.Add(ComponentType.ReadWrite<ColoredFrame>());
			}
			else if (BasePart.BelongsTo(typeInfo, BasePart.TransparentFrames))
			{
				types.Add(ComponentType.ReadWrite<ColoredFrame>());
				types.Add(ComponentType.ReadWrite<TransparentFrame>());
			}
			else
			{
				PartType partType = typeInfo.PartType;
				if (partType == PartType.Fan || partType == PartType.Propeller || partType == PartType.Rotor)
				{
					types.Add(ComponentType.ReadWrite<FanPropeller>());
				}
				else
				{
					partType = typeInfo.PartType;
					if (partType == PartType.CokeBottle || partType == PartType.SodaBottle || partType == PartType.Rocket || partType == PartType.RedRocket)
					{
						types.Add(ComponentType.ReadWrite<Rocket>());
					}
					else if (typeInfo.PartType == PartType.GrapplingHook)
					{
						types.Add(ComponentType.ReadWrite<GrapplingHook>());
					}
					else if (typeInfo.PartType == PartType.Kicker)
					{
						types.Add(ComponentType.ReadWrite<Separator>());
					}
					else if (IsWheelPart(typeInfo.PartType))
					{
						types.Add(ComponentType.ReadWrite<Wheel>());
					}
					else if (typeInfo.PartType == PartType.Gearbox)
					{
						types.Add(ComponentType.ReadWrite<Gearbox>());
					}
					else if (typeInfo.PartType == PartType.MechanicalPart)
					{
						types.Add(ComponentType.ReadWrite<MechanicalGate>());
					}
					else if (IsCircuitPart(typeInfo.PartType))
					{
						types.Add(ComponentType.ReadWrite<CircuitPart>());
					}
					else if (Sandbag.IsSandbag(typeInfo.PartType))
					{
						types.Add(ComponentType.ReadWrite<Sandbag>());
					}
					else if (Rope.IsRope(typeInfo))
					{
						// 绳索 0-3：BPSS 在 Contraption 装配阶段调 Rope.Create(left, right)
						// 生成 8 个 RopeSegment 刚体 + HingeJoint 链；这里改成
						// 「Rope 组件 + RopeNode 缓冲区」，由 RopeSystem 做 Verlet 求解。
						types.Add(ComponentType.ReadWrite<Rope>());
						types.Add(ComponentType.ReadWrite<RopeNode>());
					}
					else if (typeInfo.PartType == PartType.Egg)
					{
						// 鸟蛋 0-5：BPSS 的 Egg.cs（1-4 特殊蛋）与 AlienEgg.cs（05 = 索引 5）。
						// 效果记录在蛋自己的 EggEffect 缓冲区里，见 EggSystem。
						types.Add(ComponentType.ReadWrite<Egg>());
						types.Add(ComponentType.ReadWrite<EggEffect>());
					}
					else if (typeInfo.PartType == PartType.SkinPart)
					{
						// 蒙皮：形状参数 + 托管网格句柄。两者都留在实体上，
						// 这样存档/QA/编辑器都能直接读部件形状，不依赖任何 MonoBehaviour。
						types.Add(ComponentType.ReadWrite<SkinSheet>());
						types.Add(ComponentType.ReadWrite<SkinSheetMesh>());
					}
				}
			}
			return new ComponentTypeSet(in types);
		}
	}
}
