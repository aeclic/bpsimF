using System.Collections.Generic;
using Bpsim.UI;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;

namespace Bpsim.Parts
{
	public static class BasePart
	{
		public static PartTypeInfo WoodenBox => new PartTypeInfo(PartType.WoodenFrame, 10);

		public static PartTypeInfo SeparatedFrame => new PartTypeInfo(PartType.MetalFrame, 8);

		public static PartTypeInfo LightFrame => new PartTypeInfo(PartType.MetalFrame, 10);

		public static PartTypeInfo AlienMetalFrame => new PartTypeInfo(PartType.MetalFrame, 11);

		public static PartRangeInfo ColoredFrames => new PartRangeInfo(PartType.MetalFrame, 12, 129);

		public static PartTypeInfo MetalBox => new PartTypeInfo(PartType.MetalFrame, 130);

		public static PartTypeInfo BracketFrame => new PartTypeInfo(PartType.MetalFrame, 131);

		public static PartRangeInfo TransparentFrames => new PartRangeInfo(PartType.MetalFrame, 132, 133);

		public static PartTypeInfo OffRoadWheel => new PartTypeInfo(PartType.MotorWheel, 7);

		public static PartTypeInfo AvoidanceRocketA => new PartTypeInfo(PartType.Rocket, 1);

		public static PartTypeInfo AvoidanceRocketB => new PartTypeInfo(PartType.Rocket, 3);

		public static PartTypeInfo TrackingRocketA => new PartTypeInfo(PartType.RedRocket, 1);

		public static PartTypeInfo TrackingRocketB => new PartTypeInfo(PartType.RedRocket, 3);

		public static PartTypeInfo BlasterTNT => new PartTypeInfo(PartType.TNT, 6);

		public static PartRangeInfo HingePlates => new PartRangeInfo(PartType.Rope, 4, 7);

		public static PartTypeInfo AutoGun => new PartTypeInfo(PartType.GrapplingHook, 6);

		public static PartRangeInfo MultipartGenerators => new PartRangeInfo(PartType.GrapplingHook, 8, 10);

		public static PartTypeInfo AutoConnector => new PartTypeInfo(PartType.Kicker, 1);

		public static PartTypeInfo ElasticConnectorA => new PartTypeInfo(PartType.Kicker, 2);

		public static PartTypeInfo Marker => new PartTypeInfo(PartType.Kicker, 3);

		public static PartTypeInfo ElasticConnectorB => new PartTypeInfo(PartType.Kicker, 4);

		public static PartRangeInfo EntityLightsA => new PartRangeInfo(PartType.PointLight, 0, 4);

		public static PartTypeInfo DecelerationLight => new PartTypeInfo(PartType.PointLight, 5);

		public static PartTypeInfo AutoControlLight => new PartTypeInfo(PartType.PointLight, 6);

		public static PartRangeInfo EntityLightsB => new PartRangeInfo(PartType.SpotLight, 0, 3);

		/// <summary>
		/// 变体按它的基部件归类；原版部件原样返回。
		/// </summary>
		/// <remarks>
		/// 下面这些「按索引区间/等值判断部件类别」的函数都先过一道这个，
		/// 否则 Mod 变体（索引 9000+）会掉进所有区间的外面，被当成「不属于彩色框」
		/// —— 实体缺 <c>ColoredFrame</c>、Z 偏移算错、质量查不到，见 <see cref="PartVariants"/>。
		/// </remarks>
		public static PartTypeInfo Resolve(PartTypeInfo info)
		{
			return PartVariants.Resolve(info);
		}

		/// <summary>变体按基部件判断是否落在某个索引区间内。</summary>
		public static bool BelongsTo(PartTypeInfo info, PartRangeInfo range)
		{
			return PartVariants.Resolve(info).BelongsTo(range);
		}

		public static bool IsContainer(in PartAspect part)
		{
			return IsContainer(part.TypeInfo);
		}

		public static bool IsContainer(PartTypeInfo info)
		{
			info = PartVariants.Resolve(info);
			return info.PartType switch
			{
				PartType.WoodenFrame => info.PartIndex != WoodenBox.PartIndex, 
				PartType.MetalFrame => info.PartIndex != MetalBox.PartIndex, 
				_ => false, 
			};
		}

		public static bool CanBeContained(in PartAspect part)
		{
			return CanBeContained(part.TypeInfo);
		}

		public static bool CanBeContained(PartTypeInfo info)
		{
			info = PartVariants.Resolve(info);
			return info.PartType switch
			{
				PartType.WoodenFrame => info.PartIndex == WoodenBox.PartIndex, 
				PartType.MetalFrame => info.PartIndex == MetalBox.PartIndex, 
				_ => true, 
			};
		}

		public static bool IsStructural(in PartAspect part)
		{
			return IsStructural(part.TypeInfo);
		}

		public static bool IsStructural(PartTypeInfo info)
		{
			info = PartVariants.Resolve(info);
			switch (info.PartType)
			{
			case PartType.WoodenFrame:
				return info.PartIndex != WoodenBox.PartIndex;
			case PartType.MetalFrame:
				return info.PartIndex != MetalBox.PartIndex;
			case PartType.Spring:
			case PartType.Rope:
				return true;
			default:
				return false;
			}
		}

		public static bool IsLarge(in PartAspect part)
		{
			return IsLarge(part.TypeInfo);
		}

		public static bool IsLarge(PartTypeInfo info)
		{
			PartType partType = info.PartType;
			if (partType == PartType.KingPig || partType == PartType.GoldenPig)
			{
				return true;
			}
			return false;
		}

		public static bool IsEightWay(in PartAspect part)
		{
			return IsEightWay(part.TypeInfo);
		}

		public static bool IsEightWay(PartTypeInfo info)
		{
			info = PartVariants.Resolve(info);
			PartType partType = info.PartType;
			if (partType == PartType.GrapplingHook || partType == PartType.SpotLight || partType == PartType.TNT)
			{
				return true;
			}
			// FuelBox.Initialize has eight orientation modes, including imported diagonal modes.
			if ((partType == PartType.JetEngine && info.PartIndex == 4) || partType == PartType.Kicker) return true;
			return false;
		}

		public static bool CanFlip(in PartAspect part)
		{
			return CanFlip(part.TypeInfo);
		}

		public static bool CanFlip(PartTypeInfo info)
		{
			if (info.PartType == PartType.ElectricalPart)
				return (info.PartIndex >= 24 && info.PartIndex <= 31) || info.PartIndex == 35;
			switch (info.PartType)
			{
			case PartType.Wings:
			case PartType.Tailplane:
				return true;
			case PartType.MetalWing:
			case PartType.MetalTail:
				return true;
			default:
				return false;
			}
		}

		public static RectInt GetGridRect(in PartAspect part)
		{
			return GetGridRect(part.TypeInfo);
		}

		public static RectInt GetGridRect(PartTypeInfo info)
		{
			PartType partType = info.PartType;
			if (partType == PartType.KingPig || partType == PartType.GoldenPig)
			{
				return new RectInt(-1, 0, 3, 2);
			}
			return new RectInt(0, 0, 1, 1);
		}

		public static Vector3 MoveTo(in PartAspect part)
		{
			int coordX = part.CoordX;
			int coordY = part.CoordY;
			return new Vector3(coordX, coordY, -0.1f + GetZOffset(in part));
		}

		public static Quaternion RotateTo(ref PartAspect part)
		{
			int rotation = part.Rotation;
			bool flipped = part.Flipped;
			if (part.PartType == PartType.ElectricalPart)
			{
				rotation = part.Rotation = CircuitPart.NormalizeRotation(part.PartIndex, rotation);
				// Fixed wires and interfaces use rotation as a routing mode; children draw that mode.
				if (part.PartIndex == 1 || part.PartIndex == 3 || part.PartIndex == 38 || part.PartIndex == 39) return Quaternion.identity;
				// Extra meter orientations select probe/ohmmeter mode, keeping physical ports orthogonal.
				if (part.PartIndex == 18 || part.PartIndex == 19) rotation %= 4;
				if (flipped && CanFlip(in part))
					return Quaternion.Euler(rotation % 2 == 0 ? 180f : 0f, rotation % 2 == 1 ? 180f : 0f, rotation * 90f);
				return Quaternion.Euler(0f, 0f, rotation * 90f);
			}
			if (CanFlip(in part))
			{
				rotation = (part.Rotation = (rotation % 4 + 4) % 4);
				int num2 = ((flipped && (rotation == 1 || rotation == 3)) ? 180 : 0);
				int num3 = ((flipped && (rotation == 0 || rotation == 2)) ? 180 : 0);
				int num4 = 90 * rotation;
				return Quaternion.Euler(num2, num3, num4);
			}
			if (IsEightWay(in part))
			{
				rotation = (part.Rotation = (rotation % 8 + 8) % 8);
				// BPSS rotates only TNT's Visualization by the extra 45 degrees.
				if (part.PartType == PartType.TNT) return Quaternion.Euler(0f, 0f, 90 * (rotation % 4));
				int num6 = 90 * rotation + ((rotation >= 4) ? (-315) : 0);
				return Quaternion.Euler(0f, 0f, num6);
			}
			rotation = (part.Rotation = (rotation % 4 + 4) % 4);
			int num8 = 90 * rotation;
			return Quaternion.Euler(0f, 0f, num8);
		}

		public static float GetZOffset(in PartAspect part)
		{
			// 变体要按基部件算 Z：彩色框是 -0.45，别的才是 0/0.01。
			PartTypeInfo partTypeInfo = PartVariants.Resolve(new PartTypeInfo(part.PartType, part.PartIndex));
			float result;
			switch (partTypeInfo.PartType)
			{
			case PartType.WoodenFrame:
			{
				int partIndex = partTypeInfo.PartIndex;
				return (partIndex >= 5 && partIndex <= 8) ? (-0.45f) : (partTypeInfo.BelongsTo(WoodenBox) ? 0.01f : 0f);
			}
			case PartType.MetalFrame:
				if (!partTypeInfo.BelongsTo(ColoredFrames, TransparentFrames))
				{
					int partIndex = partTypeInfo.PartIndex;
					if (partIndex != 5 && partIndex != 6 && partIndex != 8 && partIndex != 9 && partIndex != 11)
					{
						result = (partTypeInfo.BelongsTo(MetalBox) ? 0.01f : 0f);
						goto IL_00f1;
					}
				}
				result = -0.45f;
				goto IL_00f1;
			case PartType.Pig:
			case PartType.KingPig:
			case PartType.GoldenPig:
				return 0.02f;
			case PartType.Egg:
			case PartType.Pumpkin:
				return 0.02f;
			case PartType.EngineSmall:
			case PartType.Engine:
			case PartType.EngineBig:
				return -0.01f;
			case PartType.Gearbox:
				return -0.5f;
			case PartType.PointLight:
			case PartType.SpotLight:
				return -0.01f;
			default:
				{
					return 0.01f;
				}
				IL_00f1:
				return result;
			}
		}

		public static int GetRenderPriority(in PartTransform partTransform, in WorldTransform worldTransform)
		{
			int x = partTransform.X;
			int y = partTransform.Y;
			return -((int)math.round(1000f * worldTransform.Position.z) << 16) - (2 * x + 3 * y);
		}

		public static int GetRenderPriority(in PartTransform partTransform, in LocalToWorld localToWorld)
		{
			int x = partTransform.X;
			int y = partTransform.Y;
			return -((int)math.round(1000f * localToWorld.Position.z) << 16) - (2 * x + 3 * y);
		}

		public static int GetAttachmentCount(PartAspect part)
		{
			return GetAttachmentCount(part.TypeInfo);
		}

		public static int GetAttachmentCount(PartTypeInfo info)
		{
			switch (info.PartType)
			{
			case PartType.CokeBottle:
			case PartType.SodaBottle:
				return 4;
			case PartType.Rocket:
			case PartType.RedRocket:
				return 4;
			case PartType.TNT:
				return (info.PartIndex == 2) ? 4 : 0;
			case PartType.Wings:
			case PartType.MetalWing:
				return 4;
			case PartType.GrapplingHook:
				return 8;
			case PartType.SpotLight:
				return 8;
			case PartType.JetEngine:
				return 4;
			default:
				return 0;
			}
		}

		public static Direction FindAttachment(in FixedString64Bytes name)
		{
			if (name == ILSpyHelper_AsRefReadOnly((FixedString32Bytes)"RightAttachment"))
			{
				return Direction.Right;
			}
			if (name == ILSpyHelper_AsRefReadOnly((FixedString32Bytes)"TopAttachment") || name == ILSpyHelper_AsRefReadOnly((FixedString32Bytes)"TopFrameSprite"))
			{
				return Direction.Up;
			}
			if (name == ILSpyHelper_AsRefReadOnly((FixedString32Bytes)"LeftAttachment"))
			{
				return Direction.Left;
			}
			if (name == ILSpyHelper_AsRefReadOnly((FixedString32Bytes)"BottomAttachment") || name == ILSpyHelper_AsRefReadOnly((FixedString32Bytes)"BottomFrameSprite"))
			{
				return Direction.Down;
			}
			if (name == ILSpyHelper_AsRefReadOnly((FixedString32Bytes)"TopRightAttachment"))
			{
				return Direction.UpRight;
			}
			if (name == ILSpyHelper_AsRefReadOnly((FixedString32Bytes)"TopLeftAttachment"))
			{
				return Direction.UpLeft;
			}
			if (name == ILSpyHelper_AsRefReadOnly((FixedString32Bytes)"BottomLeftAttachment"))
			{
				return Direction.DownLeft;
			}
			if (name == ILSpyHelper_AsRefReadOnly((FixedString32Bytes)"BottomRightAttachment"))
			{
				return Direction.DownRight;
			}
			return (Direction)(-1);
			static ref readonly T ILSpyHelper_AsRefReadOnly<T>(in T temp)
			{
				//ILSpy generated this function to help ensure overload resolution can pick the overload using 'in'
				return ref temp;
			}
		}

		public static bool IsTriggerable(PartTypeInfo typeInfo)
		{
			typeInfo = PartVariants.Resolve(typeInfo);
			switch (typeInfo.PartType)
			{
			case PartType.Fan:
			case PartType.Propeller:
			case PartType.Rotor:
				return true;
			case PartType.GrapplingHook:
				// 0-5 grappling hooks, 6 auto gun, 8-10 multipart generators.
				return typeInfo.PartIndex <= 6 || (typeInfo.PartIndex >= 8 && typeInfo.PartIndex <= 10);
			case PartType.PointLight:
				return typeInfo.PartIndex >= 0 && typeInfo.PartIndex <= 6;
			case PartType.SpotLight:
			case PartType.Balloon:
			case PartType.Balloons2:
			case PartType.Balloons3:
			case PartType.Umbrella:
			case PartType.PoweredUmbrella:
			case PartType.SpringBoxingGlove:
			case PartType.Bellows:
			case PartType.Pumpkin:
			case PartType.Wings:
			case PartType.Tailplane:
			case PartType.MetalWing:
			case PartType.MetalTail:
				return true;
			case PartType.Kicker:
				return typeInfo.PartIndex != 3;
			case PartType.Rocket:
			case PartType.RedRocket:
			case PartType.CokeBottle:
			case PartType.SodaBottle:
				// 点火后自行推进（One-shot boost）。
				return true;
			case PartType.TNT:
				// Hidden only in the HUD; direct touches/circuits still reach the behavior.
				return true;
			case PartType.MotorWheel:
			case PartType.StickyWheel:
			case PartType.Gearbox:
				// 动力轮 / 粘性轮 / 齿轮箱都有开关键（BPSS HasOnOffToggle => true）。
				// Passive wheels have no switch; SmallWheel#7 is handled separately.
				return true;
			case PartType.SmallWheel:
				return typeInfo.PartIndex == 7;
			case PartType.ElectricalPart:
				return typeInfo.PartIndex == 4 || typeInfo.PartIndex == 5 || typeInfo.PartIndex == 11 || typeInfo.PartIndex == 21 ||
					(typeInfo.PartIndex >= 42 && typeInfo.PartIndex <= 44) || typeInfo.PartIndex == 46 || typeInfo.PartIndex == 47;
			case PartType.MechanicalPart:
				// 0-4 = MechanicalGate1-5：点击开合闸门（BPSS MechanicalGate.OnTouch）。
				return true;
			case PartType.Sandbag:
			case PartType.Sandbag2:
			case PartType.Sandbag3:
				// BPSS Sandbag.GetTriggerButtonInfo() 在 IsAttached() 时给出「放手」按钮。
				return true;
			case PartType.Rope:
				// 0-3 = Rope：BPSS Rope.OnTouch 砍断；4-7 是铰接板（HingePlate），不可点。
				return typeInfo.PartIndex <= 3;
			case PartType.JetEngine:
				// BPSS 把三种件都放在 JetEngine 这一组皮肤里（_01/_02 = 引擎、_03/_04/_06 =
				// FuelTube.cs、_05 = FuelBox.cs），所以能不能点要按索引分：
				// 0/1 是引擎本体（BPSS JetEngine.IsTriggerable() 另外要求组里有燃料箱，
				// 那一条在 JetEngineBehavior.Trigger 里按运行时燃料图判断）；
				// 5 是阀门（BPSS FuelTube.IsTriggerable() 只认阀门）；2/3 普通管/自动管、
				// 4 燃料箱都不可点。
				return typeInfo.PartIndex == 5 || typeInfo.PartIndex <= 1;
			case PartType.Egg:
				// 0 = 普通蛋（BPSS Egg.OnTouch 对 !m_isSpecialEgg 直接 return）；
				// 1-4 = 特殊蛋（重力/失重/穿透/虚空），5 = 异形胶蛋（AlienEgg.OnTouch）。
				return typeInfo.PartIndex >= 1 && typeInfo.PartIndex <= 5;
			default:
				return false;
			}
		}

		public static bool IsEnabled(Entity part, EntityManager entityManager)
		{
			if (Gameplay.PartGameplayManager.Instance != null && Gameplay.PartGameplayManager.Instance.HasBehavior(part))
			{
				return Gameplay.PartGameplayManager.Instance.IsEnabled(part);
			}
			if (entityManager.HasComponent<FanPropeller>(part))
			{
				return entityManager.GetComponentData<FanPropeller>(part).Enabled;
			}
			if (entityManager.HasComponent<Wheel>(part))
			{
				return entityManager.GetComponentData<Wheel>(part).Enabled;
			}
			if (entityManager.HasComponent<Gearbox>(part))
			{
				return entityManager.GetComponentData<Gearbox>(part).Activated;
			}
			if (entityManager.HasComponent<MechanicalGate>(part))
			{
				// 闸门没有独立的「开/关」贴图，按钮高亮就跟随伸出状态（BPSS 亦然）。
				return entityManager.GetComponentData<MechanicalGate>(part).Enabled;
			}
			if (entityManager.HasComponent<CircuitPart>(part))
			{
				return entityManager.GetComponentData<CircuitPart>(part).Closed;
			}
			if (entityManager.HasComponent<Egg>(part))
			{
				// 普通蛋恒 false（BPSS IsEnabled() 对非特殊蛋返回 false）。
				return entityManager.GetComponentData<Egg>(part).Enabled;
			}
			if (entityManager.HasComponent<FuelTubeLink>(part))
			{
				// 阀门开/关就是按钮高亮的状态（BPSS FuelTube.GetIsEnabled() → m_enabled）；
				// 普通管/自动管没有开关，恒返回默认值 0。
				return entityManager.GetComponentData<FuelTubeLink>(part).Open != 0;
			}
			return false;
		}

		public static IEnumerable<PartTriggerButtonInfo> GetTriggerButtonInfo(PartAspect partAspect)
		{
			PartType partType = partAspect.PartType;
			if (partType == PartType.Fan || partType == PartType.Propeller || partType == PartType.Rotor)
			{
				yield return new PartTriggerButtonInfo(PartButtonType.Trigger, 0, partAspect.PartType, partAspect.Rotation, 0);
			}
			else
			{
				yield return new PartTriggerButtonInfo(PartButtonType.Trigger, 0, partAspect.PartType, partAspect.PartIndex, 0);
			}
		}

		public static IEnumerable<PartSliderButtonInfo> GetSliderButtonInfo(PartAspect partAspect)
		{
			if (partAspect.PartType == PartType.Umbrella && (partAspect.PartIndex == 3 || partAspect.PartIndex == 5))
			{
				var behavior = Gameplay.PartGameplayManager.Instance?.Find(partAspect.Entity) as Gameplay.UmbrellaBehavior;
				if (behavior != null && behavior.Mode != 0)
					yield return new PartSliderButtonInfo(PartButtonType.Slider, 1, PartType.Umbrella, partAspect.PartIndex,
					partAspect.ConnectedComponent, new PartSliderButton.Range(math.degrees(behavior.DeltaAngle), 0, -180, 180, 5, .02f));
			}
		}
	}
}
