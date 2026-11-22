gate: passed | mode=改造后做 | oracle=本地源格式的关卡清单、图层及构造网格度量 | criteria=node Tools/RC2Levels/test_core.cjs + node Tools/LevelEditor/verify_rc2_inline.cjs + LevelEditorModQA.StartCanvasPreview + Rc2PlayDiagnostics.StartSweep | round=1 | status=done | date=2026-11-19

# 两种关卡包的显示与网格

- 从关卡包携带的 ID 和关联字段显示 RC2 选关编号。在改名、保存和返回选关后验证编号稳定。依据 F_LEVEL_PACK_ID_20261119。
- 保留源材质队列和深度规则，隔离独立背景相机的深度。在编辑器内显示缺少可绘几何的物品位置。依据 F_LEVEL_PACK_DRAW_20261119。
- 用统一设置改变地图相对建造格的比例。验证出生点、地形、碰撞、编辑参考网格及水域使用一致的坐标。依据 F_LEVEL_PACK_GRID_20261119。

## 验证结果

- 浏览器 17 步通过。合成重叠场景回读前景像素，选关显示区域和任务后缀，物品标记可拾取和隐藏。
- Unity 专项 QA 62 项通过，验证 274 个 BPLE 关卡文档、两种格式的 `1`、`2` 倍网格和游戏内选关卡片的源任务编号。RC2 17 关均完成材质、背景、相机和碰撞载入。
- 完整 QA 另有一项 BPLE 水面合成队列检查失败。RC2 拾取、任务流程及逐帧动画仍有缺口。
