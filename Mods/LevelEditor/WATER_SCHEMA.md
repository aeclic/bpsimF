# 水域数据 — schema 2

完整关卡仍为 `format: "bpsim.level", version: 1`，增加 `water_schema_version: 2`。
`water_bodies`、地形 `.bytes`、资源映射及未知扩展字段一起写入 `.bpslevel`。
加载 schema 1 自动补字段，但不覆盖已有值、不自动启用旧关卡水力。未知未来 schema 拒绝加载。

| 参数 | 默认值 | 单位 / 意义 |
|---|---:|---|
| `x`, `surface_y`, `width`, `depth` | 必填 | BPLE 坐标：左边、水位、宽度、向下深度 |
| `color`, `opacity` | 必填 | HTML 颜色、0–1 水体色层强度 |
| `wave_amplitude`, `wave_speed` | 必填 | BPLE 振幅、弧度 / 秒 |
| `wave_length` | 2.5 | BPLE 波长 |
| `physics_enabled` | false（旧关卡）；true（新建） | 是否对游戏部件施力 |
| `density` | 1 | BPLE 基准水密度；按游戏单位二维排水截面积计算 |
| `buoyancy_multiplier` | 1 | 浮力倍率 |
| `linear_drag`, `angular_drag` | 0.5、0 | 阻力系数；线性阻力按浸没比例指数衰减 |
| `flow_x`, `flow_y` | 0 | BPLE 单位 /秒；导入游戏乘 8 |
| `flow_acceleration` | 0 | 沿水流方向的 BPLE 单位 /秒²；导入游戏乘 8 |
| `ripples_enabled` | true | 接触触发涟漪 |
| `ripple_strength` | 0.06 | BPLE 涟漪强度 |
| `ripple_speed` | 3 | BPLE 单位 /秒 |
| `ripple_damping` | 2 | /秒 |
| `refraction_strength` | 0.05 | BPLE 单位的图像扰动幅度，同时决定水波法线强度 |
| `caustics_strength` | 0.35 | 纹理焦散亮度（0.3.0 起由 Foam_02 丝网遮罩驱动） |
| `reflection_strength` | 0.663 | 0–1 屏幕空间平面反射强度；默认取自资产材质 |
| `foam_strength` | 1 | 水线泡沫亮度（纹理泡沫 + 涟漪浪花） |

顶层 `parallax_enabled` 默认 true，只控制此 Mod 导入游戏时的背景视差；上游画布本身已有原版主题视差。
每片水域必须有唯一非空 GUID `id` 和 1–80 字符 `name`，最多 64 片。
有限数值、尺寸、开关类型在进入物理系统前验证；全部字段范围可在 `Code/WaterSettings.cs` 查到。

近似浮力：

`冲量 = -重力 × 水密度 × 浸没投影面积 × 浮力倍率 × dt`

浮力按 BPLE 的部件包围截面积计算，并作用于各部件质心，不额外制造单件扭矩。
网页预览浮块只做说明用；真实载具、连接、碰撞与巨构以游戏模拟为准。
