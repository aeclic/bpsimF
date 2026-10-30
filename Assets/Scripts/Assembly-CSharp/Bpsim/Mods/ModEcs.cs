using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;

namespace Bpsim.Mods
{
	// ───────────────────────────── ECS 数据 ─────────────────────────────
	//
	// 设计要点（符合 bpsim 的 ECS 思想）：
	//   · 每个 Mod 是一个 Entity，状态放在组件里，而不是 MonoBehaviour 的字段里。
	//   · 玩法/其它系统查询「某个 Mod 是否启用」时走 EntityQuery / 组件读取，而不是碰托管对象。
	//   · 托管侧（ModManager，负责文件与 Assembly.LoadFrom）只做它非做不可的事，
	//     通过 ModEcsBridge 把结果同步成实体；反过来 ECS 侧想禁用某 Mod 时写命令，
	//     由 ModRegistrySystem 落到组件，ModManager 每帧回读执行实际的卸载。
	//
	// 字符串在 ECS 里用 FixedString，不持有托管引用。

	/// <summary>一个 Mod 的 ECS 镜像。id 与托管侧 <see cref="ModPackage.Id"/> 一一对应。</summary>
	public struct ModEntityComponent : IComponentData
	{
		public FixedString64Bytes Id;
		public FixedString64Bytes Name;
		public FixedString64Bytes Version;
		public byte Enabled;
		public byte HasUi;
		public byte State;   // 对应 ModState
	}

	/// <summary>Mod 生命周期事件（供其它系统观察，例如 “ModLoaded”）。</summary>
	public struct ModEventElement : IBufferElementData
	{
		public FixedString32Bytes Kind;
		public FixedString128Bytes Message;
	}

	/// <summary>ECS → 托管 的命令（例如系统请求禁用某 Mod）。</summary>
	public struct ModCommandElement : IBufferElementData
	{
		public enum Kind : byte
		{
			SetEnabled,
			RequestReload,
			RequestUnload
		}

		public Kind Command;
		public FixedString64Bytes Id;
		public byte Value;
	}

	/// <summary>注册表单例标记。</summary>
	public struct ModRegistryTag : IComponentData
	{
	}

	/// <summary>每帧汇总，供 UI/玩法只读查询。</summary>
	public struct ModSummaryComponent : IComponentData
	{
		public int Total;
		public int Loaded;
		public int Enabled;
		public int Errors;
	}

	// ───────────────────────────── 系统 ─────────────────────────────

	/// <summary>
	/// 维护 Mod 注册表：确保单例存在、消费命令缓冲、刷新汇总统计。
	/// 命令由 <see cref="ModEcsBridge"/> 写入；托管侧每帧回读 <see cref="ModEcsBridge.PendingCommands"/>。
	/// </summary>
	[UpdateInGroup(typeof(SimulationSystemGroup))]
	public partial class ModRegistrySystem : SystemBase
	{
		protected override void OnCreate()
		{
			base.OnCreate();
			EnsureSingleton();
		}

		protected override void OnUpdate()
		{
			EnsureSingleton();

			EntityManager em = EntityManager;
			Entity singleton;
			using (EntityQuery qs = em.CreateEntityQuery(ComponentType.ReadOnly<ModRegistryTag>()))
			{
				if (qs.IsEmptyIgnoreFilter)
				{
					return;
				}
				singleton = qs.GetSingletonEntity();
			}

			DynamicBuffer<ModCommandElement> commands = em.GetBuffer<ModCommandElement>(singleton);
			if (commands.Length > 0)
			{
				NativeArray<ModCommandElement> copy =
					new NativeArray<ModCommandElement>(commands.Length, Allocator.Temp);
				try
				{
					for (int i = 0; i < commands.Length; i++)
					{
						copy[i] = commands[i];
					}
					ApplySetEnabled(em, copy);
					ModEcsBridge.EnqueueCommands(copy);
				}
				finally
				{
					copy.Dispose();
				}
				commands.Clear();
			}

			ModSummaryComponent summary = default;
			using (EntityQuery qm = em.CreateEntityQuery(ComponentType.ReadOnly<ModEntityComponent>()))
			{
				using (NativeArray<ModEntityComponent> comps =
					qm.ToComponentDataArray<ModEntityComponent>(Allocator.Temp))
				{
					for (int i = 0; i < comps.Length; i++)
					{
						summary.Total++;
						if (comps[i].Enabled != 0) summary.Enabled++;
						if (comps[i].State == (byte)ModState.Loaded) summary.Loaded++;
						if (comps[i].State == (byte)ModState.Error
							|| comps[i].State == (byte)ModState.Incompatible) summary.Errors++;
					}
				}
			}
			em.SetComponentData(singleton, summary);
			ModEcsBridge.LatestSummary = summary;
		}

		private static void ApplySetEnabled(EntityManager em, NativeArray<ModCommandElement> commands)
		{
			using (EntityQuery qm = em.CreateEntityQuery(ComponentType.ReadWrite<ModEntityComponent>()))
			{
				using (NativeArray<Entity> ents = qm.ToEntityArray(Allocator.Temp))
				{
					for (int c = 0; c < commands.Length; c++)
					{
						if (commands[c].Command != ModCommandElement.Kind.SetEnabled)
						{
							continue;
						}
						for (int k = 0; k < ents.Length; k++)
						{
							ModEntityComponent mc = em.GetComponentData<ModEntityComponent>(ents[k]);
							if (mc.Id.Equals(commands[c].Id))
							{
								mc.Enabled = commands[c].Value;
								em.SetComponentData(ents[k], mc);
							}
						}
					}
				}
			}
		}

		private void EnsureSingleton()
		{
			EntityManager em = EntityManager;
			using (EntityQuery q = em.CreateEntityQuery(ComponentType.ReadOnly<ModRegistryTag>()))
			{
				if (!q.IsEmptyIgnoreFilter)
				{
					return;
				}
			}
			Entity e = em.CreateEntity();
			em.AddComponent<ModRegistryTag>(e);
			em.AddComponent<ModSummaryComponent>(e);
			em.AddBuffer<ModCommandElement>(e);
		}
	}

	// ───────────────────────────── 托管↔ECS 桥 ─────────────────────────────

	/// <summary>
	/// 托管侧访问 Mod ECS 注册表的静态桥。必须在 World 就绪后调用 <see cref="Attach"/>；
	/// 之后 <see cref="SyncAll"/> 把 <see cref="ModPackage"/> 列表同步成实体。
	/// </summary>
	public static class ModEcsBridge
	{
		private static World s_world;
		private static readonly Dictionary<string, Entity> s_byId =
			new Dictionary<string, Entity>(StringComparer.OrdinalIgnoreCase);
		private static readonly List<ModCommandElement> s_pending = new List<ModCommandElement>();

		public static bool Attached => s_world != null && s_world.IsCreated;

		public static ModSummaryComponent LatestSummary { get; internal set; }

		/// <summary>回读 ECS 侧写入的命令，供 ModManager 消费（消费后清空）。</summary>
		public static IReadOnlyList<ModCommandElement> PendingCommands => s_pending;

		internal static void EnqueueCommands(NativeArray<ModCommandElement> cmds)
		{
			for (int i = 0; i < cmds.Length; i++)
			{
				s_pending.Add(cmds[i]);
			}
		}

		public static void ClearPending()
		{
			s_pending.Clear();
		}

		public static void Attach(World world)
		{
			s_world = world;
			s_byId.Clear();
			if (world != null && world.IsCreated)
			{
				world.GetOrCreateSystemManaged<ModRegistrySystem>();
			}
		}

		public static void Detach()
		{
			s_world = null;
			s_byId.Clear();
			s_pending.Clear();
		}

		/// <summary>用最新的托管列表重建所有 Mod 实体（保持单例）。</summary>
		public static void SyncAll(IReadOnlyList<ModPackage> packages)
		{
			if (!Attached)
			{
				return;
			}
			EntityManager em = s_world.EntityManager;
			DestroyAllModEntities(em);
			s_byId.Clear();

			if (packages == null)
			{
				return;
			}
			for (int i = 0; i < packages.Count; i++)
			{
				ModPackage pkg = packages[i];
				if (pkg == null || pkg.Manifest == null)
				{
					continue;
				}
				Entity e = em.CreateEntity();
				em.AddComponentData(e, ToComponent(pkg));
				em.AddBuffer<ModEventElement>(e);
				s_byId[pkg.Id] = e;
			}
		}

		/// <summary>新增或更新单个 Mod 的镜像状态，不重建其它 Mod 的实体。</summary>
		public static void UpdateOne(ModPackage pkg)
		{
			if (!Attached || pkg == null || pkg.Manifest == null)
			{
				return;
			}
			if (!s_byId.TryGetValue(pkg.Id, out Entity e) || !s_world.EntityManager.Exists(e))
			{
				e = s_world.EntityManager.CreateEntity(typeof(ModEntityComponent));
				s_world.EntityManager.AddBuffer<ModEventElement>(e);
				s_byId[pkg.Id] = e;
			}
			s_world.EntityManager.SetComponentData(e, ToComponent(pkg));
		}

		/// <summary>从 ECS 侧请求开关（经命令缓冲排队，由系统落到组件）。</summary>
		public static void RequestSetEnabled(string id, bool enabled)
		{
			if (!Attached)
			{
				return;
			}
			EntityManager em = s_world.EntityManager;
			using (EntityQuery q = em.CreateEntityQuery(ComponentType.ReadOnly<ModRegistryTag>()))
			{
				if (q.IsEmptyIgnoreFilter)
				{
					return;
				}
				Entity singleton = q.GetSingletonEntity();
				DynamicBuffer<ModCommandElement> buf = em.GetBuffer<ModCommandElement>(singleton);
				buf.Add(new ModCommandElement
				{
					Command = ModCommandElement.Kind.SetEnabled,
					Id = FS64(id),
					Value = (byte)(enabled ? 1 : 0)
				});
			}
		}

		/// <summary>读查询：某 Mod 是否启用。玩法代码可直接用这个。</summary>
		public static bool IsEnabled(string id)
		{
			if (!Attached || !s_byId.TryGetValue(id, out Entity e) || !s_world.EntityManager.Exists(e))
			{
				return false;
			}
			return s_world.EntityManager.GetComponentData<ModEntityComponent>(e).Enabled != 0;
		}

		private static ModEntityComponent ToComponent(ModPackage pkg)
		{
			return new ModEntityComponent
			{
				Id = FS64(pkg.Id),
				Name = FS64(pkg.DisplayName),
				Version = FS64(pkg.Version),
				Enabled = (byte)(pkg.Enabled ? 1 : 0),
				HasUi = (byte)(pkg.Manifest.HasPanels ? 1 : 0),
				State = (byte)pkg.State
			};
		}

		private static void DestroyAllModEntities(EntityManager em)
		{
			using (EntityQuery q = em.CreateEntityQuery(ComponentType.ReadOnly<ModEntityComponent>()))
			{
				using (NativeArray<Entity> arr = q.ToEntityArray(Allocator.Temp))
				{
					em.DestroyEntity(arr);
				}
			}
		}

		internal static FixedString64Bytes FS64(string s)
		{
			FixedString64Bytes f = default;
			if (!string.IsNullOrEmpty(s))
			{
				f.CopyFromTruncated(s);
			}
			return f;
		}
	}
}
