# BpsimF

**Bad piggies fan-made game--BPLE's simulator Flash version**

> [!WARNING]
> **本项目近乎全部由 AI 辅助开发，测试而来。**
>
> 本项目由反编译的BPSIM 2022.2.7 工程二次开发而来，参考项目包含 besiege，bple，bpir，bpss，bprc等。
> 然后 AI 就自己源源不断的在缝合代码石块了，测试也不够完全。

> 本仓库与 Rovio Entertainment 及 Bad Piggies **无任何关联**，亦未获其认可。
> "捣蛋猪" / "BP" 等名称仅作技术识别之用，仓库所有者不持有相关商标；
> 产物 DLL/CS 文件名沿用原版名称 仅为交流方便。

> SCOPE 是一个**重建计划**（Project），不是单一引擎实体：把经典 RPG Maker 运行库
> （RGSS1 / RGSS2 / RGSS3 / RM2K）的完整逻辑用 C 源码级还原，产出与原版
> **1:1 行为等价**的同名 drop-in DLL（Game.exe 不动）。壳（ASPack）本身不还原。
>
> 四条运行时线**彼此独立**（仅工具链与逆向思路互参）；`src/shared/` 目前不存在，
> 只有被两条线实测证明一致的代码才会上提。

## 运行时状态矩阵

| 线 | 目标 DLL | 当前进度 | 进度看板 | 报告 |
|----|----------|----------|----------|------|
| rgss2 | RGSS202E.dll（VX, 2010-09-02） | **M5-k**：标题→进图全链路渲染 | [docs/progress/rgss2.md](docs/progress/rgss2.md) | [docs/reports/rgss2/](docs/reports/rgss2/) |
| rgss3 | RGSS300.dll（VX Ace） | M3（Ruby 1.9.2 集成） | [docs/progress/rgss3.md](docs/progress/rgss3.md) | [docs/reports/rgss3/](docs/reports/rgss3/) |
| rm2k | RM2000/2003（Delphi PE） | M3（LCF 数据层） | [docs/progress/rm2k.md](docs/progress/rm2k.md) | [docs/reports/rm2k/](docs/reports/rm2k/) |
| rgss1 | RGSS1xx.dll（XP 系） | M2（模块地图/接口） | [docs/progress/rgss1.md](docs/progress/rgss1.md) | [docs/reports/rgss1/](docs/reports/rgss1/) |

rgss1 尚无重建源码（`src/` 下有意不存在该目录）；其逆向报告与工具（`tools/` 内 `RGSS1_*` 前缀）已就位。

## 仓库边界（为什么有 local/）

本仓库**只收录源码与文档**。以下内容不进版本库，集中放在不入库的工作台 `local/`：

| 位置 | 内容 | 原因 |
|------|------|------|
| `local/backup/` | 各版本原版 RGSS*.dll + 游戏 EXE | Enterbrain 著作权，不公开分发 |
| `local/unpacked/` | 脱壳产物（OEP dump / 重建 PE / 探针版） | 原版 DLL 派生物，同上 |
| `local/build/` | 构建产物（*.o、DLL、Ruby 静态库 ruby-build / rubylib、离线反汇编 full_disasm.txt） | 可由源码重新生成 |
| `local/test/` | 游戏副本（含 RTP 素材、字体、音频，530MB） | 体积 + 著作权 |
| `local/third_party/` | Ruby 1.8.1 / 1.9.2-p0 官方源码树与 tarball | 公开上游，取法与 SHA-256 见 [docs/analysis/common/third_party.md](docs/analysis/common/third_party.md) |
| `local/scripts/` | 从 Scripts.rvdata 反编译的游戏脚本原文 | Enterbrain 脚本库 |

工作台位置可用环境变量 `SCOPE_LOCAL` 覆盖（缺省 `<repo>/local`）。
例外说明：`docs/assets/embedded_rpg_classes.rb`（DLL 内嵌 RPG 类源码）是构建必需输入，
按"可复现性优先"保留在库中——现位于 `src/runtimes/rgss2/embedded/rpg_classes.rb`。

## 目录结构

```
<repo>\
├── RESTRUCTURE_PLAN.md → docs/adr/0001   结构重构工单（执行完归档为 ADR）
├── README.md / LICENSE                   本文件 / CC0 1.0（附中文参考译文）
├── src\
│   ├── mk\common.mk                      工具链变量与模式规则（无具体目标）
│   └── runtimes\
│       ├── rgss2\                        ★RGSS202E 重建（Makefile = 唯一构建真相）
│       │   ├── export.c classes.c data_classes.c bitmap.c graphics_classes.c
│       │   ├── render.c gamemain.c marshal.c embedded.c regex_stubs.c image.cpp
│       │   ├── exports.def  exports.orig.txt（gen_def.py 输入）
│       │   ├── ext\Win32API.c            （rgss1 建源时是 shared/ 首个候选）
│       │   ├── selftest\                 Ruby 层电池（ruby_battery.c 等）
│       │   └── embedded\rpg_classes.rb   构建输入（objcopy → rpg_src.o）
│       ├── rgss3\300\                    RGSS300 重建（Ruby 1.9.2，rubylib）
│       └── rm2k\                         LCF 数据层 / Delphi 平台层
├── tools\                                自研工具，按流水线分桶
│   ├── unpack\ pe\ analysis\ probe\ verify\
│   └── Makefile                          全部工具的构建入口 → local/build/tools/
├── docs\
│   ├── reports\<线>\                     里程碑叙事报告（M 编号是身份锚点，保留）
│   ├── analysis\<线>\                    可长期引用的逆向事实（API 表/布局/ABI/踩坑）
│   ├── audit\                            白盒一致性审计（会过期的快照判定）
│   ├── progress\<线>.md                  ★唯一进度区
│   ├── adr\                              决策记录
│   └── assets\                           证据截图
└── local\                                ★工作台，整体不入库（见上）
```

## 快速开始（rgss2 线）

依赖：`C:\msys64` 的 mingw32 工具链；`local/build/ruby-build`（Ruby 1.8.1 静态库）；
`local/third_party/ruby-1.8.1`（zlib 扩展源码）。

```bash
make -C src/runtimes/rgss2        # 产出 local/build/rgss2/RGSS202E_rebuild.dll（69 导出）
make -C tools                     # 重建全部工具 → local/build/tools/

# 验证
local/build/tools/smoke.exe local/build/rgss2/RGSS202E_rebuild.dll
local/build/tools/ruby_battery.exe <ruby_test.dll>      # Ruby 层电池 10/10
local/test/Project3_rebuild/Game.exe                    # ★真实游戏（标题→进图）
```

完整构建参数见 `src/runtimes/rgss2/Makefile`；rgss3 线用 `make -C src/runtimes/rgss3/300`
（Ruby 1.9.2 rubylib，与 rgss2 的 ruby-build 是两个不同目录）。

## 测试环境

- 测试游戏副本：`local/test/Project3_orig\`（Game.ini 已指向 RGSS202E.dll，RTP 为空）
- 对照基线：**原版带壳 DLL**（`local/backup/RGSS202E.dll`）
- 已知：游戏目录同名的 RGSS202J_fixed.dll 是坏的，忽略
- 本机 Win11 对 32 位进程强制注入 apphelp 垫片，详见 [docs/reports/rgss2/M1.5_可运行脱壳版报告.md](docs/reports/rgss2/M1.5_可运行脱壳版报告.md) §4

## 文档地图

- `docs/reports/` — 里程碑叙事（当时发生了什么、踩了什么坑；顶部脚注说明重构前路径）
- `docs/analysis/` — 可长期引用的规格与事实：Game.exe 契约、模块地图、Bitmap 布局、
  328 条 API 映射表（`analysis/rgss2/rgss_ruby_api_map.tsv`）、45 条踩坑（`pitfalls.md`）、
  Win32API 调用约定、Window 9 宫格皮肤规格
- `docs/audit/` — 白盒一致性审计 A1–A5 + 总报告（判定枚举：一致/等价/偏差/桩/未映射/无法验证）
- `docs/progress/` — 四线进度看板（**改代码同步更新这里**）
- `docs/adr/` — 决策记录（0001 = 本次结构重构）

## 许可

**CC0 1.0 Universal** — 详见 [LICENSE](LICENSE)（附非官方中文参考译文，以英文原文为准）。
作者**不主张著作权**；任何人无需署名、无需征得同意，即可自由使用、修改、再分发（含商用）。
内容按"现状"提供，无任何担保。

- 本仓库不包含、也不分发任何 Enterbrain / RPG Maker 的原始二进制、游戏数据、素材；
  此类文件位于不入库的 `local/` 工作台，见「仓库边界」一节。
- `docs/` 中的事实性观察记录（内存布局、函数地址、IAT 映射等）不作为版权创作物主张权利。
- `local/third_party/` 的 Ruby 上游（1.8.1 / 1.9.2-p0）按 Ruby License 释出，
  取法与 SHA-256 见 [docs/analysis/common/third_party.md](docs/analysis/common/third_party.md)；
  zlib 按 zlib License。

再次强调：本项目从一句话起步，全部产出（源码、工具、文档）均为 AI 幻觉碰巧幸存的产物，没有人工审查背书，请降低预期后再继续阅读。
