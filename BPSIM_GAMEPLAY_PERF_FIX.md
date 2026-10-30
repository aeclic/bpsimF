# Bpsim 大型存档性能：行为层平方级开销的定位与修复

测量环境：Unity China `2022.2.23f1c1`（编辑器 Play Mode），工程 `bpsim-main`，桥接端口 8791。
注意：本工程在**编辑器里 Burst 被强制关闭**（`Tools/BPSim/PatchBurstForEditor.ps1`，见
`Assets/Scripts/BpsimCompat/BpsimPhysicsCompat.cs`），所有 `[BurstCompile]` job 在编辑器内回退成托管执行，
所以下面的绝对值只用于**同环境相对比较**；打包（IL2CPP + Burst）后同一场景会快很多。

本文与 `BPSIM_SPIN_PERF_FIX.md`（自旋越快越卡 = AABB 速度膨胀 → 连通块碰撞过滤）是两个独立的坑：
那篇讲的是**物理接触**，本篇讲的是**托管行为层**。两者叠加才解释“大型存档一进就卡”。

---

## 1. 结论先行

| 场景 | 部件 / 关节 / 连通块 | 修复前 | 修复后 | 倍数 |
| --- | --- | --- | --- | --- |
| 「天使座」前 10 000 行（idle，ω=0） | 10 000 / 30 472 / 242 | **21 257.6 ms/帧** | **856.8 ms/帧** | 24.8× |
| 「天使座」全量（idle，ω=0） | 20 405 / 180 832 / 1 | **106 420.8 ms/帧** | **8 247.3 ms/帧** | 12.9× |
| 「不败荣光」ω=32（回归） | 1 648 / 5 250 / 1 | 31.8 ms（本会话基线，未修） | 7.64 ms avg（4.35 ms p50） | 无回归 |

根因一句话：**`PoweredUmbrellaBehavior` 每个固定步无条件重算一次“整车电力聚合”，而那次重算要把全部部件扫 3 遍；
92 个动力伞 × 3 遍 × 10 000 部件 ≈ 每步 276 万次托管 `EntityManager` 查询。** 单步因此 1.24 s，
再被 fixed step 追赶放大 16 倍，就成了 20 s 一帧。

---

## 2. 怎么定位到的（可复现）

### 2.1 先证明不是物理、不是渲染

10 000 部件存档 bisect（探针 `bisect=1`，每阶段 1 帧）：

| 阶段 | ms | 说明 |
| --- | --- | --- |
| `bisect base w=2` | 20 590 | 基线 |
| `no-render` | 23 085 | 关渲染**无变化** |
| `no-render no-gameplay` | 23 008 | — |
| **`no-gameplay`** | **1 510** | **关掉 `PartGameplayManager` → 掉 13×** |
| `restored` | 1 796 | 仍处于 gameplay 关闭 |
| `no-PartSimulationSystem` | 24 245 | 回到 ~23 s（gameplay 恢复） |
| `no-ConstraintSystem` | 23 267 | 无变化 |
| `no-FrameJointSystem` | 23 786 | 无变化 |
| `no-CustomBackgroundSystem` | 23 889 | 无变化 |
| `no-TransparentFrame` | 22 246 | 无变化 |

（探针的开关在阶段帧**结束**时生效，所以读数要错位一阶段看。）
→ 物理关节求解、DOTS 系统、渲染都不是瓶颈，**托管行为层占 ~93 %**。

### 2.2 再逐个 behavior 计时（新增编辑器探针）

`Assets/Editor/BpsimPerf/BehaviorTimingProbe.cs`（仅编辑器程序集，不随包发布）：

```
invoke_static Bpsim.Perf.BehaviorTimingProbe Run ""        # 对 1270 个 behavior 各单独 Tick 一遍
invoke_static Bpsim.Perf.BehaviorTimingProbe Run "sched"   # 只统计每帧跑了几个 fixed step
invoke_static Bpsim.Perf.BehaviorTimingProbe SetPowerCache true|false   # 本次修复的 A/B 开关
invoke_static Bpsim.Perf.BehaviorTimingProbe SetMaxDelta 0.02           # 限制追赶步数
```

10 000 部件（1 270 个 behavior）修复前的输出：

```
[BehaviorTiming] pass done: calls=1270 enabled=19 fails=0 totalMs=1235.6 maxSingle=21.6ms@PoweredUmbrellaBehavior/PoweredUmbrella
  PoweredUmbrellaBehavior/PoweredUmbrella n=92  ms=1235.3 (100.0%) avg=13.43 max=21.6
  VoidLightBehavior/AutoControlLight      n=78  ms=   0.1 (  0.0%)
  JetEngineBehavior/JetEngine             n=32  ms=   0.0
  ...（其余 1260 个全是 0.0 ms）
```

**一个类型吃掉 100 %。** 92 个动力伞平均 13.4 ms/个。

### 2.3 最后确认放大机制

`Time.maximumDeltaTime = 0.333`、`Fixed Timestep = 0.02` → 单帧最多补 **16.7** 个固定步。
`sched` 实测（20 405 部件）：

```
sched frame#=5526 durMs=7629 fixedSteps=17.00
sched frame#=5527 durMs=7473 fixedSteps=17.00
sched frame#=5528 durMs=7393 fixedSteps=16.00
```

1.235 s/步 × 16 ≈ 20 s/帧，与 10 000 部件的实测帧时间完全吻合。
把 `maximumDeltaTime` 强行压到 0.02（每帧只跑 1 步）后，同一场景帧时间立刻变成 417–600 ms：

```
=== B: maximumDeltaTime=0.020
[SpinPerf] [single w=0.00] frames=2 fps=1.95 ms avg/p50=515.27/592.35
```

这是“越卡越卡”的正反馈：帧长 → 补更多步 → 帧更长。

---

## 3. 根因代码

`Assets/Scripts/Assembly-CSharp/Bpsim/Parts/Gameplay/PartBehaviorsPower.cs`，
`PoweredUmbrellaBehavior.Tick()` 第一行就 `RefreshPower()`（**在 `if (!Enabled) return;` 之前**），
而 `RefreshPower()` 对 `simulator.Parts` 做三遍全量扫描：

1. 累加本连通块的 `EnginePower` / `PowerConsumption`（逐候选 `Alive()` + `ConnectedComponent()`）；
2. `enginePower <= 0` 时的空间兜底再扫一遍（逐候选再算一次距离）；
3. 统计 `m_componentParts` 再扫一遍。

`PartGameplayUtil.Alive()` / `ConnectedComponent()` 每次都是 `EntityManager.HasComponent` + `GetComponentData`
的托管调用（~µs 级），放进 `部件数 × 行为数` 的双层循环就是灾难。

同样的“每 tick 全量扫”模式还存在于（目前量级小，未爆）：
`PartGameplayManager.FindTargetInCone`、`ApplyRadialImpulse`、`VoidLightBehavior.Tick`（两重 for）、
`SpringBehavior` 等 —— 见 `grep -n "Parts.Length" Gameplay/*.cs`。

---

## 4. 修复

### 4.1 已实现（`ComponentPowerCache`，每固定步一遍）

- 新增 `ComponentPowerCache`（同在 `PartBehaviorsPower.cs`）：`AdvanceToken()` 由
  `PartGameplayManager.FixedUpdate` 每个固定步调一次；首个请求时**单遍**遍历 `simulator.Parts`，
  按连通块 index 聚合 `EnginePower / PowerConsumption / Parts`，之后同一步内全部动力伞 O(1) 查表。
- 语义不变：聚合只依赖连通块成员与 extension 值，**同一个固定步内不可能变化**，因此“每步一遍”与
  “每个伞各扫三遍”完全等价（替换段的注释写在代码里；旧路径保留在 `else` 分支里以便 A/B）。
- 空间兜底那段（`enginePower <= 0` 时按 1.8 m 半径找引擎）依赖 `Position(Part)`，与具体伞有关，故**不进缓存**，
  保持原行为。

改动文件：

| 文件 | 改动 |
| --- | --- |
| `Assets/Scripts/Assembly-CSharp/Bpsim/Parts/Gameplay/PartBehaviorsPower.cs` | 新增 `ComponentPowerCache`；`PoweredUmbrellaBehavior.RefreshPower` 改为查缓存 |
| `Assets/Scripts/Assembly-CSharp/Bpsim/Parts/Gameplay/PartGameplayManager.cs` | `FixedUpdate` 里 `ComponentPowerCache.AdvanceToken()` |
| `Assets/Editor/BpsimPerf/BehaviorTimingProbe.cs` | 新增 QA 探针（逐 behavior 计时 / sched / 两个 A/B 开关） |

### 4.2 A/B 证据（同一会话、同一存档，10 000 部件）

```
=== A: power cache OFF（旧行为）
[SpinPerf] [single w=0.00] ... ms=21257.64  joints=30472 comp=242
=== B: power cache ON
[SpinPerf] [single w=0.00] ... ms=1850.63/856.82  joints=30472 comp=242
```

修复后 behavior pass 总耗时 1 235.6 ms → **0.1 ms**（动力伞 1 235.3 → 0.0）。

---

## 5. 修完之后：20 000 部件仍然 8 秒/帧，钱花在哪

逐项实测（20 405 部件、180 832 关节、单连通块；均把 `maximumDeltaTime` 压到 0.02 保证“一帧一步”，
下表是**每固定步**的耗时）：

| 配置 | ms/步 | 相对基线 |
| --- | --- | --- |
| 基线 | 508.8 | — |
| `nocollide=1`（部件间不碰撞） | 455.8 | **−10 %** |
| `solver=1`（默认 8） | 335.9 | **−34 %** |
| `no-render`（帧级，非步级） | 8239 → 7363 /帧 | **−11 % /帧** |

bisect 显示关掉 bpsim 自己的系统（PartSimulationSystem / ConstraintSystem / FrameJointSystem /
背景 / 透明框）**全部无变化**，说明剩下的 ~450 ms/步在 **Unity.Physics 核心**
（BuildPhysicsWorld → 宽相位/窄相位 → 求解器 → ExportPhysicsWorld）加 ECS transform 更新上，
而且这些在编辑器里都是非 Burst 执行。

规模关系（idle，修复后）：10 000 部件 ≈ 53 ms/步；20 405 部件 ≈ 450–500 ms/步。
部件 ×2、关节 ×5.9（30 472 → 180 832）而时间 ×8.5 —— **超线性**，因为整个存档是**一个 18 万关节的巨型岛**
（`components=1`）。Unity.Physics 的 island 求解在超大单岛上会明显超线性。

---

## 6. 后续优化建议（按杠杆排序）

1. **（已完成）行为层聚合缓存。** 同一模式应推广到其余“每 tick 全量扫”的行为：
   `FindTargetInCone` / `ApplyRadialImpulse` / `VoidLightBehavior` / `SpringBehavior`。
   规则：凡是 `Tick` 里出现 `for (±1e4)` 的，都要么每步缓存一次聚合量，要么改用
   `ComponentLookup<T>` + 批处理（参考 `LaunchBehavior.ThrustBatch` 的做法）。
2. **别让 fixed step 追赶雪崩。** 现在 `Maximum Allowed Timestep = 0.333` 会把任何长帧放大 16 倍，
   形成正反馈（实测 7.39 s/帧 = 16–17 步）。建议把上限压到 0.06–0.10（≈3–5 步），
   或在超预算时直接丢弃积压（宁可“慢动作”也不要把帧时间乘 16）。
   这一项对任何重型存档都立刻见效，且不改语义。
3. **性能评估必须区分“编辑器 / 打包”。** 编辑器内 Burst 被关，`[BurstCompile]` 全走托管。
   在编辑器里测出的 450 ms/步不能代表发布版；要贴近真机请用 `unity-batch.bat` 打包后测。
4. **关节数量本身过大**：20 405 部件产生 180 832 根关节（≈8.9 根/部件）。
   焊接网格里每对相邻件本应只要 1 根；重复关节既拖求解器又占内存。可考虑去重，
   或把同一连通块内的部件合并成 compound collider（一个 body 多 shape），从根本上消掉关节。
5. **拆分巨型单岛**：整个存档 `components=1` 时，求解器看到一个 18 万关节的岛。若允许按结构
   （例如按底盘/挂车/支架）切成若干子岛，成本会从超线性回落到接近线性。
   同时确认 Unity.Physics 的睡眠/休眠阈值没被这类大体量场景绕过（静止时应当休眠而不是每步全解）。
6. **减少托管 ECS 访问**：`PartGameplayUtil.Alive/ConnectedComponent` 每次都是
   `HasComponent` + `GetComponentData` 的托管往返。内层循环里应改为
   `ComponentLookup<T>`（注意必须在 `__AssignHandlesImpl` 里注册，否则每帧 NRE）
   或整体搬进 job。

---

## 7. 复现命令

```bash
API=http://127.0.0.1:8791/api
# 载入 + 单帧采样（10k 子集；完整存档把 perf/angel_10k.csv 换成 perf/angel.csv）
curl -s -X POST -H "Content-Type: application/json" \
  -d '{"method":"invoke_static","params":{"type":"Bpsim.QA.SpinPerfProbe","method":"Run",
       "args":["file=perf/angel_10k.csv;sweep=0;bisect=0;mode=rigid;spin=0;gravity=0;dur=0.1"]}}' $API
```

- `perf/angel.csv` = 完整「天使座」（20 405 行），`perf/angel_10k.csv|5k|2k` = `head -N` 子集，
  `perf/buhai.csv` = 「不败荣光」。**中文文件名经 bash→curl 会乱码，所以统一用 ASCII 副本。**
- `perf/step_profile.sh`、`perf/step_ab.sh`、`perf/step_bisect20k.sh`、`perf/step_catchup.sh`、
  `perf/step_split.sh` 是本轮用到的驱动脚本（进 Play → 载入 → 采样 → 收日志）。
- 编辑器日志：`$LOCALAPPDATA/Unity/Editor/Editor.log`，过滤 `^\[SpinPerf\]` / `\[BehaviorTiming\]`。

## 8. 环境坑（本轮踩到的）

- 桥接 `compile_status` 在编译**失败**时也可能报 `errors: []`，要用 `grep "error CS"` 查
  `Editor.log` 确认；改完脚本必须 `invoke_static <Type> Status` 验证类型真的存在。
- 改脚本会触发域重载 → 自动退出 Play Mode。流程：`playmode exit` → 写文件 → `compile` → 等类型可用 → `playmode enter`。
- 超长帧期间桥接会报 `editor main thread stalled`，dispatch 需要重试循环（本脚本里都是 `ok_or_retry`）。
- `PartGameplayManager.Begin()` 对每个部件打一条 `[PartGameplay] <type>: kind=...` 日志，而本工程
  每次 `Debug.Log` 都会附带调用栈 —— 2 万部件时仅日志就足以让 `StartSimulation` 极慢。测性能时注意这点。
