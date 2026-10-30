#if UNITY_EDITOR
using System.Collections;
using System.IO;
using System.Text;
using Bpsim.Parts;
using Bpsim.UI;
using Unity.Entities;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

namespace Bpsim.QA
{
	/// <summary>
	/// quickbuild 三项建造交互（点按旋转 / 长按拖动 / 落位自动对齐）的 Play 模式验收探针。
	///
	///   invoke_static type=Bpsim.QA.QuickBuildInteractionQA method=Run    args=[]
	///   invoke_static type=Bpsim.QA.QuickBuildInteractionQA method=Status args=[]
	///
	/// 覆盖五层：
	/// 1. <see cref="PartAutoAlign" /> 的策略表与朝向求解（纯函数，含八向件）；
	/// 2. <see cref="PartCreationRequest.AutoAlign" /> 的默认值、<c>WithAutoAlign</c> 与链式保持；
	/// 3. <see cref="PartSceneUnmanaged.PlacePartAt" /> 的落位自动对齐（Rotate 与 FlipVertically）；
	/// 4. <see cref="PartSceneUnmanaged.TryStepPartRotation" /> 的点按旋转（普通件前进一格、可翻转件镜像）；
	/// 5. 拖动落位（<see cref="PartSceneUnmanaged.TryMovePartContent" />）后重新对齐。
	///
	/// 断言写的是「端子现在指向邻居」这个不变式（即
	/// <see cref="Bpsim.Parts.Simulation.PartSimulator.CanConnectTo" /> 的 target 侧条件），
	/// 而不是求解器自己的中间结果，所以换了搜索顺序也不会自证通过。
	/// 报告写到 &lt;project&gt;/Logs/bpsim-quickbuildqa.log。
	/// </summary>
	public sealed class QuickBuildInteractionQA : MonoBehaviour
	{
		private static QuickBuildInteractionQA s_instance;

		private static readonly StringBuilder s_report = new StringBuilder();

		private static string s_status = "idle";

		private static int s_passed;

		private static int s_failed;

		public static string Run()
		{
			QuickBuildInteractionQA runner = Ensure();
			s_report.Length = 0;
			s_passed = 0;
			s_failed = 0;
			s_status = "running";
			runner.StartCoroutine(runner.RunChecks());
			return "started";
		}

		public static string Status()
		{
			return s_status + " | pass=" + s_passed + " fail=" + s_failed + "\n" + s_report;
		}

		private static QuickBuildInteractionQA Ensure()
		{
			if (s_instance != null)
			{
				return s_instance;
			}
			GameObject gameObject = new GameObject("[QuickBuildInteractionQA]");
			gameObject.hideFlags = HideFlags.DontSave;
			Object.DontDestroyOnLoad(gameObject);
			s_instance = gameObject.AddComponent<QuickBuildInteractionQA>();
			return s_instance;
		}

		private static void Check(string name, bool ok, string detail = "")
		{
			if (ok)
			{
				s_passed++;
			}
			else
			{
				s_failed++;
			}
			s_report.Append(ok ? "PASS " : "FAIL ").Append(name);
			if (!string.IsNullOrEmpty(detail))
			{
				s_report.Append(" | ").Append(detail);
			}
			s_report.Append('\n');
		}

		private IEnumerator RunChecks()
		{
			for (int i = 0; i < 600 && PartManager.Instance == null; i++)
			{
				yield return null;
			}
			PartManager manager = PartManager.Instance;
			if (manager == null)
			{
				Check("PartManager", false, "未就绪");
				Finish();
				yield break;
			}
			Check("PartManager", true);

			CheckAutoAlignTable();
			CheckSolver();
			CheckRequestFlags(manager);
			CheckPressDecision();

			manager.LoadEmptyScene("QuickBuildInteractionQA");
			yield return null;
			yield return null;
			PartScene scene = manager.HasActiveScene() ? manager.ActiveScene : null;
			Check("active scene", scene != null);
			if (scene == null)
			{
				Finish();
				yield break;
			}

			yield return CheckAutoAlignRotate(scene, manager);
			yield return CheckAutoAlignFlip(scene, manager);
			yield return CheckStepRotation(scene, manager);
			yield return CheckDragAndRealign(scene, manager);

			Finish();
		}

		// ---------------------------------------------------------------- 纯函数

		private static void CheckAutoAlignTable()
		{
			Check("mode wings flip", PartAutoAlign.GetMode(new PartTypeInfo(PartType.Wings, 0)) == PartAutoAlignMode.FlipVertically);
			Check("mode metal tail flip", PartAutoAlign.GetMode(new PartTypeInfo(PartType.MetalTail, 0)) == PartAutoAlignMode.FlipVertically);
			Check("mode bellows rotate", PartAutoAlign.GetMode(new PartTypeInfo(PartType.Bellows, 0)) == PartAutoAlignMode.Rotate);
			Check("mode cart wheel rotate", PartAutoAlign.GetMode(new PartTypeInfo(PartType.CartWheel, 0)) == PartAutoAlignMode.Rotate);
			Check("mode metal frame none", PartAutoAlign.GetMode(new PartTypeInfo(PartType.MetalFrame, 0)) == PartAutoAlignMode.None);
			Check("mode pig none", PartAutoAlign.GetMode(new PartTypeInfo(PartType.Pig, 0)) == PartAutoAlignMode.None);
			Check("chassis", PartAutoAlign.IsChassis(new PartTypeInfo(PartType.MetalFrame, 0)) && PartAutoAlign.IsChassis(new PartTypeInfo(PartType.WoodenFrame, 0)) && !PartAutoAlign.IsChassis(new PartTypeInfo(PartType.Wings, 0)));
		}

		private static void CheckSolver()
		{
			Check("solver none terminals", !PartAutoAlign.TrySolveRotation(BitDirection.None, 0, BitDirection.Left, false, out int noneTerminals));
			Check("solver none target", !PartAutoAlign.TrySolveRotation(BitDirection.Right, 0, BitDirection.None, false, out int noneTarget));
			Check("solver any stays", PartAutoAlign.TrySolveRotation(BitDirection.Any, 2, BitDirection.Left, false, out int anyRotation) && anyRotation == 2, "r=" + anyRotation);
			Check("solver rotates toward target", PartAutoAlign.TrySolveRotation(BitDirection.Right, 0, BitDirection.Left, false, out int leftRotation) && leftRotation == 2, "r=" + leftRotation);
			Check("solver keeps matching", PartAutoAlign.TrySolveRotation(BitDirection.Right, 2, BitDirection.Left, false, out int keepRotation) && keepRotation == 2, "r=" + keepRotation);
			Check("solver prefers nearest step", PartAutoAlign.TrySolveRotation(BitDirection.Right | BitDirection.Up, 0, BitDirection.Up, false, out int nearestRotation) && nearestRotation == 0, "r=" + nearestRotation);
			Check("solver eight way", PartAutoAlign.TrySolveRotation(BitDirection.Right, 0, BitDirection.Left, true, out int eightRotation) && eightRotation == 2, "r=" + eightRotation);
			Check("solver normalizes negative", PartAutoAlign.TrySolveRotation(BitDirection.Right, -2, BitDirection.Left, false, out int normalizedRotation) && normalizedRotation == 2, "r=" + normalizedRotation);
		}

		private static void CheckRequestFlags(PartManager manager)
		{
			PartTypeInfo info = new PartTypeInfo(PartType.MetalFrame, 0);
			Entity prefab = manager.EntitySpawner.FindPrefab(info);
			PartCreationRequest plain = new PartCreationRequest(prefab, 1, info, 0, 0, 0, false);
			Check("request autoalign defaults off", !plain.AutoAlign);
			Check("request with autoalign", plain.WithAutoAlign(true).AutoAlign);
			PartCreationRequest chained = plain.WithAutoAlign(true).WithPosition(2, 3).WithRotation(1, true).WithLayer(2);
			Check("request keeps autoalign through chain", chained.AutoAlign && chained.CoordX == 2 && chained.CoordY == 3 && chained.Rotation == 1 && chained.Flipped && chained.Layer == 2);
			PartCreationRequest schematic = new PartCreationRequest(prefab, 1, default(Schematics.Unit));
			Check("request schematic path never autoaligns", !schematic.AutoAlign);
		}

		/// <summary>
		/// quickbuild 里「按在已有部件上」之后这一下归谁：长按拖动搬走，还是把点击还给笔刷接着画。
		/// 这是三项交互里唯一一条纯策略，也是最容易改出手感问题的地方，所以逐分支断言。
		/// </summary>
		private static void CheckPressDecision()
		{
			Check("press | idle while still undecided", PartDrawer.DecidePartPress(false, false, false) == PartDrawer.PartPressAction.None);
			Check("press | idle even when paintable", PartDrawer.DecidePartPress(false, false, true) == PartDrawer.PartPressAction.None);
			Check("press | hold grabs unpaintable", PartDrawer.DecidePartPress(false, true, false) == PartDrawer.PartPressAction.Drag);
			Check("press | hold grabs paintable", PartDrawer.DecidePartPress(false, true, true) == PartDrawer.PartPressAction.Drag);
			Check("press | move on free cell grabs", PartDrawer.DecidePartPress(true, false, false) == PartDrawer.PartPressAction.Drag);
			Check("press | move on paintable cell paints", PartDrawer.DecidePartPress(true, false, true) == PartDrawer.PartPressAction.Paint);
			// 手柄（手里选着轮子）压在已有部件上：放不下，移动即搬运。
			Check("press | wheel brush on frame grabs", PartDrawer.DecidePartPress(true, false, BasePart.IsStructural(new PartTypeInfo(PartType.NormalWheel, 0))) == PartDrawer.PartPressAction.Drag);
			// 手里选着铁框压在同一格上：放得下，移动即接着画。
			Check("press | frame brush on frame paints", PartDrawer.DecidePartPress(true, false, BasePart.IsStructural(new PartTypeInfo(PartType.MetalFrame, 0))) == PartDrawer.PartPressAction.Paint);
			Check("press | rope brush on frame paints", PartDrawer.DecidePartPress(true, false, BasePart.IsStructural(new PartTypeInfo(PartType.Rope, 0))) == PartDrawer.PartPressAction.Paint);
		}

		// ---------------------------------------------------------------- 场景内
		private IEnumerator CheckAutoAlignRotate(PartScene scene, PartManager manager)
		{
			yield return ClearScene(scene);
			PartTypeInfo frameInfo = new PartTypeInfo(PartType.MetalFrame, 0);
			Entity frame = Place(scene, manager, frameInfo, 0, 0, 0, false, false);
			Check("autoalign rotate | frame seed", frame != Entity.Null);
			if (frame == Entity.Null)
			{
				yield break;
			}

			PartTypeInfo? candidate = FindRotateCandidate(manager);
			Check("autoalign rotate | candidate part", candidate.HasValue, candidate.HasValue ? candidate.Value.PartType + "/" + candidate.Value.PartIndex : "none");
			if (!candidate.HasValue)
			{
				yield break;
			}
			PartTypeInfo info = candidate.Value;
			if (!manager.Factory.PartExtensionMap.TryGetValue(info, out PartExtensionData extension))
			{
				Check("autoalign rotate | extension data", false, info.PartType.ToString());
				yield break;
			}

			int startRotation = -1;
			for (int r = 0; r < 4; r++)
			{
				if ((extension.ConnectionDirection.Rotate(r) & BitDirection.Left) == 0)
				{
					startRotation = r;
					break;
				}
			}
			Check("autoalign rotate | start rotation", startRotation >= 0, "r=" + startRotation);
			if (startRotation < 0)
			{
				yield break;
			}

			// 关掉自动对齐：朝向必须原样保留（读档路径就靠这个不被改写）。
			Entity raw = Place(scene, manager, info, 1, 0, startRotation, false, false);
			EntityManager entityManager = manager.System.EntityManager;
			Check("autoalign rotate | placed without autoalign", raw != Entity.Null && entityManager.GetComponentData<PartTransform>(raw).Rotation == startRotation, "r=" + startRotation);
			yield return ClearScene(scene);

			// 重新摆一次，这次带自动对齐。
			frame = Place(scene, manager, frameInfo, 0, 0, 0, false, false);
			Entity aligned = Place(scene, manager, info, 1, 0, startRotation, false, true);
			Check("autoalign rotate | placed with autoalign", aligned != Entity.Null);
			if (aligned == Entity.Null)
			{
				yield break;
			}
			PartTransform alignedTransform = entityManager.GetComponentData<PartTransform>(aligned);
			Check("autoalign rotate | terminals face neighbour", (extension.ConnectionDirection.Rotate(alignedTransform.Rotation) & BitDirection.Left) != 0, "terminals=" + extension.ConnectionDirection + " " + startRotation + " -> " + alignedTransform.Rotation);

			// 已经对齐的部件再对齐一次不应产生变化（避免每帧抖动）。
			bool realigned = scene.Unmanaged.TryAutoAlignPart(aligned);
			PartTransform afterSecond = entityManager.GetComponentData<PartTransform>(aligned);
			Check("autoalign rotate | idempotent", !realigned && afterSecond.Rotation == alignedTransform.Rotation, "r=" + afterSecond.Rotation);

			// 没有邻居时保持原朝向。
			yield return ClearScene(scene);
			Entity lonely = Place(scene, manager, info, 8, 0, startRotation, false, true);
			Check("autoalign rotate | lonely part keeps rotation", lonely != Entity.Null && entityManager.GetComponentData<PartTransform>(lonely).Rotation == startRotation, "r=" + startRotation);
		}

		private IEnumerator CheckAutoAlignFlip(PartScene scene, PartManager manager)
		{
			yield return ClearScene(scene);
			PartTypeInfo wingInfo = new PartTypeInfo(PartType.Wings, 0);
			Entity wingPrefab = manager.EntitySpawner.FindPrefab(wingInfo);
			Check("autoalign flip | wings prefab", wingPrefab != Entity.Null);
			if (wingPrefab == Entity.Null)
			{
				yield break;
			}
			PartTypeInfo frameInfo = new PartTypeInfo(PartType.MetalFrame, 0);
			EntityManager entityManager = manager.System.EntityManager;

			// 右侧是机架 -> 不镜像（机翼在 x=1，机架必须放在它右边的 x=2）。
			Place(scene, manager, frameInfo, 2, 0, 0, false, false);
			Entity right = Place(scene, manager, wingInfo, 1, 0, 0, true, true);
			Check("autoalign flip | right chassis unflips", right != Entity.Null && !entityManager.GetComponentData<PartTransform>(right).Flipped, "flipped=" + (right != Entity.Null && entityManager.GetComponentData<PartTransform>(right).Flipped));

			// 左侧是机架 -> 镜像。
			yield return ClearScene(scene);
			Place(scene, manager, frameInfo, -1, 0, 0, false, false);
			Entity left = Place(scene, manager, wingInfo, 0, 0, 0, false, true);
			Check("autoalign flip | left chassis flips", left != Entity.Null && entityManager.GetComponentData<PartTransform>(left).Flipped);

			// 两侧都没有机架 -> 保持玩家选择。
			yield return ClearScene(scene);
			Entity alone = Place(scene, manager, wingInfo, 0, 0, 0, true, true);
			Check("autoalign flip | no chassis keeps flip", alone != Entity.Null && entityManager.GetComponentData<PartTransform>(alone).Flipped);
		}

		private IEnumerator CheckStepRotation(PartScene scene, PartManager manager)
		{
			yield return ClearScene(scene);
			EntityManager entityManager = manager.System.EntityManager;
			PartTypeInfo frameInfo = new PartTypeInfo(PartType.MetalFrame, 0);
			Entity frame = Place(scene, manager, frameInfo, 0, 0, 0, false, false);
			Check("step rotation | frame seed", frame != Entity.Null);
			if (frame == Entity.Null)
			{
				yield break;
			}
			int before = entityManager.GetComponentData<PartTransform>(frame).Rotation;
			bool stepped = scene.Unmanaged.TryStepPartRotation(frame);
			int after = entityManager.GetComponentData<PartTransform>(frame).Rotation;
			Check("step rotation | advances one step", stepped && after == before + 1, before + " -> " + after);

			PartTypeInfo wingInfo = new PartTypeInfo(PartType.Wings, 0);
			if (manager.EntitySpawner.FindPrefab(wingInfo) != Entity.Null)
			{
				Entity wing = Place(scene, manager, wingInfo, 3, 0, 0, false, false);
				PartTransform wingBefore = entityManager.GetComponentData<PartTransform>(wing);
				bool wingStepped = scene.Unmanaged.TryStepPartRotation(wing);
				PartTransform wingAfter = entityManager.GetComponentData<PartTransform>(wing);
				Check("step rotation | flippable part flips", wingStepped && wingAfter.Flipped == !wingBefore.Flipped && wingAfter.Rotation == wingBefore.Rotation, "flip=" + wingBefore.Flipped + " -> " + wingAfter.Flipped + " r=" + wingAfter.Rotation);
			}

			Check("step rotation | rejects null", !scene.Unmanaged.TryStepPartRotation(Entity.Null));
		}

		private IEnumerator CheckDragAndRealign(PartScene scene, PartManager manager)
		{
			yield return ClearScene(scene);
			PartTypeInfo frameInfo = new PartTypeInfo(PartType.MetalFrame, 0);
			Place(scene, manager, frameInfo, 0, 0, 0, false, false);
			PartTypeInfo? candidate = FindRotateCandidate(manager);
			Check("drag | candidate part", candidate.HasValue);
			if (!candidate.HasValue)
			{
				yield break;
			}
			PartTypeInfo info = candidate.Value;
			if (!manager.Factory.PartExtensionMap.TryGetValue(info, out PartExtensionData extension))
			{
				Check("drag | extension data", false, info.PartType.ToString());
				yield break;
			}
			int startRotation = -1;
			for (int r = 0; r < 4; r++)
			{
				if ((extension.ConnectionDirection.Rotate(r) & BitDirection.Left) == 0)
				{
					startRotation = r;
					break;
				}
			}
			if (startRotation < 0)
			{
				Check("drag | start rotation", false);
				yield break;
			}

			Entity part = Place(scene, manager, info, 5, 0, startRotation, false, false);
			Check("drag | part seed", part != Entity.Null);
			if (part == Entity.Null)
			{
				yield break;
			}
			EntityManager entityManager = manager.System.EntityManager;

			bool moved = scene.Unmanaged.TryMovePartContent(5, 0, 1, 0);
			bool found = scene.Unmanaged.FindPartAt(1, 0, out Entity movedPart) && movedPart == part;
			Check("drag | moves content and keeps entity", moved && found);
			Check("drag | grid updated", !scene.Unmanaged.FindPartGridAt(5, 0, out var sourceGrid) || !(sourceGrid.HasPart || sourceGrid.HasPartContainer), "source still occupied");
			PartTransform movedTransform = entityManager.GetComponentData<PartTransform>(part);
			Check("drag | move keeps rotation", movedTransform.X == 1 && movedTransform.Y == 0 && movedTransform.Rotation == startRotation, "(" + movedTransform.X + "," + movedTransform.Y + ") r=" + movedTransform.Rotation);

			bool realigned = scene.Unmanaged.TryAutoAlignPart(part);
			PartTransform realignedTransform = entityManager.GetComponentData<PartTransform>(part);
			Check("drag | realigns after drop", realigned && (extension.ConnectionDirection.Rotate(realignedTransform.Rotation) & BitDirection.Left) != 0, "terminals=" + extension.ConnectionDirection + " " + startRotation + " -> " + realignedTransform.Rotation);

			// 目标格被占用时拒绝移动。
			Place(scene, manager, frameInfo, 1, 3, 0, false, false);
			Check("drag | rejects occupied destination", !scene.Unmanaged.TryMovePartContent(1, 0, 1, 3));

			// 结构件可以叠到有占用的格上（木框/铁框是 structural）。
			Entity extraFrame = Place(scene, manager, frameInfo, 3, 3, 0, false, false);
			Check("drag | structural part places on occupied cell", extraFrame != Entity.Null, "frame=" + extraFrame);
		}

		// ---------------------------------------------------------------- 工具

		private static PartTypeInfo? FindRotateCandidate(PartManager manager)
		{
			PartType[] candidates =
			{
				PartType.Bellows,
				PartType.Fan,
				PartType.Propeller,
				PartType.Rotor,
				PartType.Umbrella,
				PartType.PoweredUmbrella,
				PartType.TimeBomb,
				PartType.CartWheel,
				PartType.NormalWheel,
				PartType.SmallWheel,
				PartType.MotorWheel,
				PartType.StickyWheel
			};
			for (int c = 0; c < candidates.Length; c++)
			{
				for (int index = 0; index < 12; index++)
				{
					PartTypeInfo info = new PartTypeInfo(candidates[c], index);
					if (PartAutoAlign.GetMode(info) != PartAutoAlignMode.Rotate)
					{
						continue;
					}
					if (!manager.Factory.PartExtensionMap.TryGetValue(info, out PartExtensionData extension))
					{
						continue;
					}
					// 只挑「自己不作为连接源、且只有一个固定端子」的件：
					// 这样朝向是否对齐完全由它的端子掩码决定，断言才不依赖邻居的朝向。
					if (extension.IsConnectionSource || extension.ConnectionDirection.BitCount() != 1)
					{
						continue;
					}
					if (manager.EntitySpawner.FindPrefab(info) == Entity.Null)
					{
						continue;
					}
					return info;
				}
			}
			return null;
		}

		private static Entity Place(PartScene scene, PartManager manager, PartTypeInfo info, int x, int y, int rotation, bool flipped, bool autoAlign)
		{
			Entity prefab = manager.EntitySpawner.FindPrefab(info);
			if (prefab == Entity.Null)
			{
				return Entity.Null;
			}
			PartCreationRequest request = new PartCreationRequest(prefab, scene.SceneID, info, x, y, rotation, flipped);
			if (autoAlign)
			{
				request = request.WithAutoAlign(true);
			}
			if (!scene.Unmanaged.PlacePartAt(in request, false))
			{
				return Entity.Null;
			}
			return scene.Unmanaged.FindPartAt(x, y, out Entity part) ? part : Entity.Null;
		}

		private static IEnumerator ClearScene(PartScene scene)
		{
			for (int i = 0; i < 60 && scene.Unmanaged.Parts.Length != 0; i++)
			{
				scene.Unmanaged.ClearParts();
				yield return null;
			}
		}

		private void Finish()
		{
			s_status = ((s_failed == 0) ? "done:PASS" : "done:FAIL") + " pass=" + s_passed + " fail=" + s_failed;
			string logPath = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Logs", "bpsim-quickbuildqa.log"));
			try
			{
				Directory.CreateDirectory(Path.GetDirectoryName(logPath));
				File.WriteAllText(logPath, Status());
			}
			catch (System.Exception exception)
			{
				Debug.LogWarning("[QuickBuildInteractionQA] 写报告失败: " + exception.Message);
			}
			Debug.Log("[QuickBuildInteractionQA] " + Status());
		}
	}
}
#endif
