# 工坊仓库与游戏更新

## 创意工坊

Mod 管理器的「创意工坊」页签可在 Gitee 与 GitHub 间切换，也可设置仓库所有者、仓库名、分支和索引路径。仓库配置保存在 `Application.persistentDataPath/ModWorkshop/config.json`。GitHub 测试仓库预设为 `aeclic/bpsimF`，测试索引位于 `workshop/index.json`。Gitee 预设使用 BPIR 工坊仓库 `yctf123123/bple-workshop`。

索引采用 schema 1 的 JSON，`packs` 条目兼容现有 `WorkshopPack` 字段。内容包以 zip 发布，zip 内需要有 `Mod.xml` 或 `manifest.json`。上传会将 zip 放到索引目录下的 `packs/<id>/<version>.zip`，计算 SHA-256 和文件大小，再更新索引。单包上传上限为 20 MB。上传失败时，若 zip 已写入但索引冲突，仓库里可能留有未引用的 zip。

上传需要填写有目标仓库内容写入权限的访问令牌。令牌只保留在当前游戏会话内，不写入仓库配置。

## 游戏更新

更新检查读取 `aeclic/bpsimF` 的 GitHub Releases，仅接受 `BPSIM-<version>` 或 `v<version>` 标签，并要求附件包含 `update-manifest.json` 和当前平台安装包。Release 需要将 `windows-x64` zip 或 `android-arm` APK 一并上传。清单样例如下。

```json
{
  "schema": 1,
  "product": "bpsimF",
  "version": "0.0.13",
  "applicationId": "com.bprc.bpsimF",
  "windowsExecutable": "bpsim.exe",
  "notes": "更新说明",
  "platforms": {
    "windows-x64": {
      "assetName": "bpsim-windows-x64.zip",
      "size": 123456789,
      "sha256": "64 位十六进制 SHA-256"
    },
    "android-arm": {
      "assetName": "bpsim-android-arm.apk",
      "size": 45678901,
      "sha256": "64 位十六进制 SHA-256"
    }
  }
}
```

Windows zip 必须包含 `bpsim.exe` 和完整的 Unity 播放器文件。Android 包必须保持当前应用标识。客户端在安装前校验 Release 版本、产品、包标识、附件大小和 SHA-256。

BPIR 上游使用不同的 Android 包标识和 Windows 可执行文件名，因此上游 BPIR 安装包不能覆盖更新本项目。当前 GitHub 仓库尚无 Release，更新检查会正常提示没有可用更新。
