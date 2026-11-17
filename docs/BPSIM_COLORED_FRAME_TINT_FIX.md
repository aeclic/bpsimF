# 彩色框（MetalFrame #12-129）渲染成白块的根因与修复（2026-11-16）

## 症状

建造界面里所有染色框（`PartType.MetalFrame`，`PartIndex` 12-129）都画成**扁平白色方块 + 淡色边框**，
颜色选择器选任何色相都没区别。索引 #8（分离框 #8）、#10（灯框）、#11（外星框）、#130（金属箱）、
#131（支架框）外观正常，因为它们走的是各自独立的 sprite，不依赖染色。

`MetalFrame #132/#133`（透明框）同样偏白，但那是 `TransparentFrameSystem` 的另一条路径。

## 定位过程（结论先行）

排查顺序与排除项：

| 怀疑 | 结论 |
|---|---|
| shader 编译错误（历史上有 `redefinition of '_Color'`） | 排除。实时日志 `%LOCALAPPDATA%\Unity\Editor\Editor.log` 里 `Shader error` 计数为 0 |
| `_Color` 没送到 GPU | 排除。`BpsimPlayQA.Run("buffer")` dump BRG 实例缓冲，`_Color` 里有正确值如 `(0.3,0.09,0.16,1)` |
| `SpriteName` 为空 → 没有 `BlendFactor` 组件 | 排除。`PartGroup_6_MetalFrame.prefab` 里 391 个 `MaterialSprite`，`m_name` 全部非空；探针显示渲染实体都带 `BlendFactor` |
| `SceneRenderSystem` 上传错位 | 排除。同一个 memcpy 路径 `_Color` 传对了 |
| **`ColoredFrameSystem` 根本没给顶层彩色框写 `_BlendFactor`** | **确认** |

关键对照：shader 片元是

```hlsl
half3 rgb = tex.rgb * blend.x + tint.rgb * blend.y;   // Bpsim_URP_Unlit_Texture_Instancing.shader:184
```

彩色框应该用 `(x,y) = (0, 0.75)`（纯色 × 0.75，不要白底贴图）。实测缓冲里全是 `(1, 0, 0, 0)`，
即「原样采样那张白色框贴图」—— 于是 118 个索引全白。

## 根因

`Assets/Scripts/Assembly-CSharp/Bpsim/Parts/ColoredFrameSystem.cs`（反编译产物，上游 `f8f55b7` 起就如此）
有两个 job：

- **Job0**：query = `LinkedEntityGroup + PartTag + ColoredFrame(RW)`，只写 `_Color`（`MaterialColor`）。
- **Job1**：query = `LinkedEntityGroup + PartTag + PartContainerValue` + **`WithNone<ColoredFrame>()`** + `WithAspect<PartAspect>`，
  写 `_BlendFactor`。

Job1 的 lambda 里那句 `(0, 0.75, 0, 0)` 只有一个入口：

```csharp
if (partContainer != Entity.Null && ColoredFrameLookup.HasComponent(partContainer))
{
    value  = ColoredFrameLookup[partContainer].Color;
    value2 = new float4(0f, 0.75f, 0f, 0f);
}
```

`part.PartContainer` 是「**我**被谁包住」，由 `PartSceneUnmanaged.UpdateContainer` 只在
「部件被放进另一个部件的格子」时写入。**顶层彩色框自己 `PartContainer == Entity.Null`**，
于是既被 `WithNone<ColoredFrame>()` 排除在查询外、又走不到那个分支。

结果：顶层彩色框（正是玩家在工具栏里放下的那一种）**永远保持 `PartEntityFactory.CreateSprite` 里
给的初值 `BlendFactor(1,0,0,0)`**，只有被塞进另一个彩色框里时才有颜色 —— 明显是移植时的逻辑漏洞，
和旧版 BPRC 的 `ColoredFrame.UpdateRenderers()`（永远用自己的 `m_color` 上色）不一致。

## 修复

`Assets/Scripts/Assembly-CSharp/Bpsim/Parts/ColoredFrameSystem.cs`，两处：

### 1. 让彩色框自己成为颜色来源（`OriginalLambdaBody`）

`LinkedEntityGroup[0]` 恒为该部件自身根实体（`PartEntityFactory.Create` 用 `AddBuffer<LinkedEntityGroup>(entity).Add(entity)`
播种）。据此判断「自己就带 `ColoredFrame`」：

```csharp
bool ownsItsColor = linkedEntityGroup.Length > 0
    && __Bpsim_Parts_ColoredFrame_ComponentLookup.HasComponent(linkedEntityGroup[0].Value);
float4 value  = new float4(1f, 1f, 1f, 1f);
float4 value2 = new float4(1f, 0f, 0f, 0f);
if (ownsItsColor)
{
    value2 = new float4(0f, 0.75f, 0f, 0f);          // 只改 _BlendFactor
}
else if (partContainer != Entity.Null && ...HasComponent(partContainer))
{
    value  = ColoredFrameLookup[partContainer].Color; // 被包住的部件仍取容器颜色
    value2 = new float4(0f, 0.75f, 0f, 0f);
}
foreach (Entity item in linkedEntityGroup.Reinterpret<Entity>())
{
    if (!ownsItsColor && materialColorLookup.HasComponent(item))  // 自身来源时不碰 _Color
        materialColorLookup[item] = new MaterialColor { Value = value };
    if (blendFactorLookup.HasComponent(item))
        blendFactorLookup[item] = new BlendFactor { Value = new float4(value2.xy, blendFactorLookup[item].Value.zw) };
}
```

**`!ownsItsColor` 这个条件是必要的**：自身来源时不能写 `_Color`，因为 Job0 已经**逐渲染实体**维护它，
并且保留了 `Background` 子物体各自的 alpha（不透明框 0.6、透明框 0.2）。若在这里统一覆盖，
`Background` 会变成和不透明主框一样，失去背景层。

`.zw` 保持原样同样必要 —— `FrameShapeSystem.ApplyPending()` 往 `blend.Value.z` 写形状、
`blend.Value.w` 写「是否为玻璃填充」，两套系统按 xy / zw 分工。

### 2. 把彩色框纳入 Job1 的查询

```csharp
entityQueryBuilder = entityQueryBuilder
    .WithAll<LinkedEntityGroup>()
    .WithAll<PartTag>()
    .WithAll<PartContainerValue>()
    // 删掉了 WithNone<ColoredFrame>()
    .WithAspect<PartAspect>();
```

## 验证

探针 `Assets/Scripts/Assembly-CSharp/Bpsim/QA/ZzColorFrameProbe.cs`（`Bpsim.QA.ZzColorFrameProbe.DumpBlend()`）
逐部件 dump 每个渲染实体的 `BlendFactor` / `MaterialColor`，通过 DshBridge `invoke_static` 调用。

修复前（任意顶层彩色框）：

```
root 1812 MetalFrame#84  CF container=- color=float4(0.3f,0.09f,0.09f,1f) transparent=False
    [0] Part_MetalFrame_84  blend=float4(1f,0f,0f,0f) mat=float4(0.3f,0.09f,0.09f,1f)   sprite=779
    [1] Background          blend=float4(1f,0f,0f,1f) mat=float4(0.3f,0.09f,0.09f,0.6f) sprite=945
```

修复后：

```
root 1812 MetalFrame#84  CF container=- color=float4(0.3f,0.09f,0.09f,1f) transparent=False
    [0] Part_MetalFrame_84  blend=float4(0f,0.75f,0f,0f) mat=float4(0.3f,0.09f,0.09f,1f)   sprite=779
    [1] Background          blend=float4(0f,0.75f,0f,1f) mat=float4(0.3f,0.09f,0.09f,0.6f) sprite=945
```

离屏截图（`Bpsim.QA.BpsimPlayQA.Capture`，均存于 `Logs/`）：

- `Logs/fix1.png` — 18 个暗色索引（`num4=4`，明度 0.3），色相环从暗红→橄榄→灰绿→青→蓝→紫→品红，全部正确。
  修复前同一场景（`Logs/hue2.png`）是 18 个一模一样的白方块。
- `Logs/fix2.png` — 118 个彩色框全索引（6 圈明度/饱和度 × 18 色相），六圈层次可辨。
- `Logs/fix3.png` — 43 件全外观回归（木框 / 沙袋 / 轮子 / 弹簧 / TNT / 绳 / 机翼 / 引擎 / 气球 / 猪），
  非彩色框部件不受影响。
- `Logs/fix4.png` / `fix7.png` — 透明框 #132/#133 的邻居颜色渗透仍按 16 轮迭代收敛，未因 `_BlendFactor` 改变而失效。
- `Logs/fix6.png` — `FrameShapeSystem` 的圆 / 三角 / 半圆三种形状仍正常（`blend.z` 与 `blend.xy` 互不干扰）。
- `Logs/fix8.png` — 被彩色框包住的沙袋仍取容器颜色（旧的 `PartContainer` 分支未回归）。

## 影响面与已知取舍

- **只影响渲染**：`_BlendFactor.xy` 与 `_Color` 都是 GPU 实例数据，不参与物理、存档或连接。
- **嵌套彩色框的行为变了**：内层彩色框以前会取外层容器的颜色（因为它自己也被 `WithNone` 排除，
  只有容器分支可用），现在保持自己的颜色。这与 BPRC 旧版 `ColoredFrame.UpdateRenderers()`
  永远用自身 `m_color` 的语义一致，也是玩家从颜色选择器里选出来的那个颜色。
- **透明框路径未动**：`TransparentFrameSystem` 仍先于 `ColoredFrameSystem` 跑，把 `TransparentColor`
  写进 `ColoredFrame.Color` 并置 `HasChanged`，再由 Job0 落到 `_Color`。
- **性能**：Job1 的查询多了至多 118 个彩色框根实体，每个 2 个渲染实体，代价可忽略。

## 复现命令（DshBridge）

```powershell
# 载入一份彩色框样板存档（无 BOM UTF-8 CSV）
POST http://127.0.0.1:8790/api
{"method":"invoke_static","params":{"type":"Bpsim.Levels.LevelQa","method":"LoadSceneFile",
  "args":["<project>\\Logs\\tanklab\\palette.csv"]}}

# 逐实体 dump（修复前后对比用）
{"method":"invoke_static","params":{"type":"Bpsim.QA.ZzColorFrameProbe","method":"DumpBlend","args":[]}}

# 离屏截图：file,width,height,camX,camY,orthoSize
{"method":"invoke_static","params":{"type":"Bpsim.QA.BpsimPlayQA","method":"Capture",
  "args":["fix2.png,1600,1000,0,0,9.5"]}}
```

改完 C# 后必须 **退出 Play Mode 才会真正重编译**：`compile_status` 的 `compiling` 在 Play Mode 下恒为 true，
不能当完成信号。流程是 `playmode exit` → 等 `Library/ScriptAssemblies/Assembly-CSharp.dll` 的
`LastWriteTime` 变化 → `playmode enter`（退到编辑态后 EntityWorld 会销毁，探针会返回 `no world`）。
