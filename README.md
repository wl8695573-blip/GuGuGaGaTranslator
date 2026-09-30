# GuGuGaGaTranslator

Windows 屏幕翻译工具。选择游戏窗口和文字区域后，程序会识别画面中的文字，将译文显示在置顶悬浮层中。支持中文、日语和英语互译，可用于多款游戏的对话和字幕；字幕固定在底部的游戏可直接使用“底部对话框”预设，再按实际位置调整。

目前内置的游戏术语档案只有《边狱巴士》。其他游戏可使用通用翻译，也可自行添加术语表。

**当前版本：1.3.1** · [下载](https://github.com/wl8695573-blip/GuGuGaGaTranslator/releases/latest) · [使用说明](docs/USAGE.md) · [更新记录](CHANGELOG.md) · [问题反馈](https://github.com/wl8695573-blip/GuGuGaGaTranslator/issues/new/choose)

![主界面](assets/screenshot-main.png)

截图中的窗口和台词为合成示例。

## 适用游戏与术语

- **其他游戏：** 能正常捕获画面、识别选区文字时，就可以尝试通用翻译。底部字幕、固定对话框更便于持续识别；字幕位置变化时需要重新框选。
- **《边狱巴士》：** 附带英文和日文术语档案，可在“游戏模式”中选择并编辑。
- **自定义术语：** 可为其他游戏新建档案，填写角色名、地名和专有名词，也支持导入、导出分享。

翻译其他游戏时，在“游戏模式”选择“不使用 —— 通用翻译”，设置翻译方向，再框选字幕区域。没有术语表也能翻译，但专有名词可能与游戏官方译名不同。实际效果取决于字体、背景、抓屏兼容性和所选翻译服务。

## 补充或纠正术语

欢迎补充《边狱巴士》及其他游戏的专有名词。**用户提交建议，维护者核对后修改术语库。** 不需要修改代码。

1. 登录 GitHub，先搜索 [已有反馈](https://github.com/wl8695573-blip/GuGuGaGaTranslator/issues)，避免重复提交。
2. 打开 [术语补充／纠错表单](https://github.com/wl8695573-blip/GuGuGaGaTranslator/issues/new?template=terminology.yml)，填写原文、建议中文译名和出处，可附截图。
3. 提交后在该 Issue 查看维护者回复；核对完成后会说明处理结果及更新的获取方式。

详细步骤和填写示例见 [术语贡献说明](CONTRIBUTING.md)。修改本机术语只影响自己的配置，程序目前不会自动同步 GitHub 上的术语更新。

## 下载与安装

系统要求：**Windows 10 2004（19041）或更高版本，x64**。安装包包含 .NET 运行时和离线识别模型，无需另外安装运行环境或 OCR 语言包。

| 下载文件 | 使用方式 |
|---|---|
| [GuGuGaGaTranslator-Setup-1.3.1.exe](https://github.com/wl8695573-blip/GuGuGaGaTranslator/releases/download/v1.3.1/GuGuGaGaTranslator-Setup-1.3.1.exe) | 安装版。运行后选择目录；创建快捷方式，并登记到 Windows 应用列表。 |
| [GuGuGaGaTranslator-win-x64-1.3.1.zip](https://github.com/wl8695573-blip/GuGuGaGaTranslator/releases/download/v1.3.1/GuGuGaGaTranslator-win-x64-1.3.1.zip) | 便携版。完整解压后运行 `GuGuGaGaTranslator.exe`，保留同目录的 `models` 和其他文件。 |
| [SHA256SUMS-1.3.1.txt](https://github.com/wl8695573-blip/GuGuGaGaTranslator/releases/download/v1.3.1/SHA256SUMS-1.3.1.txt) | 上述两个文件的 SHA256 校验值。 |

安装到当前用户有写入权限的目录时不需要管理员权限。新安装请选择空目录；升级时先退出旧程序，再安装到原来已登记的目录。便携版建议解压到新目录，避免混用不同版本的文件。用户设置位于 `%APPDATA%\GuGuGaGaTranslator`，升级和正常卸载会保留这些设置。

1.3.1 发行文件未进行代码签名，Windows 可能显示发布者无法验证的提示。请从本仓库 Release 下载并核对校验值：

```powershell
Get-FileHash .\GuGuGaGaTranslator-Setup-1.3.1.exe -Algorithm SHA256
```

GitHub 自动生成的 “Source code” 压缩包只包含源码，不能直接运行。

## 首次使用

1. **配置翻译服务。** 首次启动时选择服务商，填写 API Key、接口地址和模型名，点击“测试连接”。本地模型需先启动兼容服务。暂不配置时可进入预览模式，检查抓屏和识别；预览模式不翻译文字。
2. **选择目标窗口。** 将游戏设为窗口模式或无边框窗口，在左侧列表选中它；找不到时点击“刷新列表”。
3. **选择文字区域。** 点击“框选区域”，拖出对话框范围，按 `Enter` 确认、`Esc` 取消。也可用“底部对话框”预设，再根据游戏布局调整。
4. **开始翻译。** 检查翻译方向后点击“开始翻译”。主界面显示识别原文和译文，悬浮层显示当前译文。
5. **调整悬浮层。** 点击控制条的“编辑”，拖动或缩放翻译框；再次点击恢复鼠标穿透。

新配置默认使用“自动识别 → 中文”和通用翻译。框选只确定位置；已有配置会保留原来的翻译方向。翻译英文报纸等普通内容时，在“游戏模式”选择通用翻译，在悬浮栏选择“英 → 中”或“自动识别 → 中文”。

![首次设置](assets/screenshot-setup.png)

翻译服务需要用户自己的账户或本地模型。连接测试会发送一条示例文本，可能计入用量；费用和模型可用性以服务商控制台为准。

## 功能

| 功能 | 说明 |
|---|---|
| 文字识别 | 默认使用附带模型的 RapidOCR；也支持依赖系统语言功能的 Windows OCR。 |
| 翻译服务 | OpenAI 兼容接口、本地模型，以及彩云、有道、百度接口。 |
| 上下文与术语 | AI 翻译可携带前文、作品设定和术语；传统接口支持本机译后术语校正。 |
| 游戏档案 | 保存作品名称、语言、术语和风格，支持自动匹配、编辑及导入导出；目前仅内置《边狱巴士》术语档案，其他游戏可自行添加。 |
| 悬浮层 | 位置、字号、透明度、原文显示和鼠标穿透均可调整，支持快捷键。 |
| 缓存 | 内存缓存默认启用；可选加密磁盘缓存，设置保留天数和容量。 |
| 诊断 | 手动导出版本、运行状态和耗时；不包含台词、译文、密钥或截图。 |

详细配置见 [使用说明](docs/USAGE.md)，档案文件格式见 [档案分享](docs/PROFILES.md)。

## 使用范围与常见问题

- **识别无需联网。** 在线翻译会把识别文字、所选术语和上下文发送给配置的服务；使用本地服务时由本地模型处理。
- **优先使用窗口模式或无边框窗口。** 独占全屏、最小化窗口、受保护画面和部分硬件加速程序可能无法捕获。
- **默认抓取可见屏幕。** 目标区域被其他程序遮挡时，识别可能不正确。`PrintWindow` 可作为替代后端，但兼容性取决于目标程序。
- **布局变化后重新框选。** 程序按客户区尺寸调整区域；游戏改变对话框位置时仍需重新选择。
- **出现 `[mock …]` 表示预览模式。** 在“识别与翻译”中选择实际翻译引擎，配置并测试后保存。
- **识别不准确时先检查原文。** 缩小选区、增大识别前放大倍数，或调整对比度；Windows OCR 还需检查语言及系统 OCR 功能。
- **译文显示不全时**，将悬浮层“最大行数”设为 `0`，并检查字体、宽度和高度。

问题排查和反馈步骤见 [使用说明](docs/USAGE.md#问题排查)。

## 验证情况

发行前检查覆盖编译、回归逻辑、合成 OCR 样本、WPF 界面渲染，以及安装、升级、卸载和便携版自检。合成样本用于回归比较，不代表所有真实游戏的识别效果。多显示器混合 DPI 和在线账户权限仍取决于使用环境。

复现命令、指标和已知限制见 [验证与发布](docs/VERIFICATION.md)。

## 开发与维护

源码构建需要 Windows 和 **.NET 10 SDK**：

```powershell
git clone https://github.com/wl8695573-blip/GuGuGaGaTranslator.git
cd GuGuGaGaTranslator
.\build.ps1
```

生成安装版、便携版和校验文件：

```powershell
.\build.ps1 -Publish -SingleFile -Zip -Installer
.\tools\verify-release.ps1
```

| 目录 | 内容 |
|---|---|
| `src/GuGuGaGaTranslator.App` | WPF 界面和应用生命周期。 |
| `src/GuGuGaGaTranslator.Core` | 抓屏、配置、OCR 接口、翻译与处理管线。 |
| `src/GuGuGaGaTranslator.Ocr.Rapid` | RapidOCR 适配器。 |
| `src/GuGuGaGaTranslator.Storage.Sqlite` | 加密磁盘缓存。 |
| `src/Shared` | 安装文件清单和路径校验。 |
| `tools` | 安装器、回归检查、OCR 基准、界面预览及发布工具。 |
| `models/v6` | 附带的识别模型和字典。 |
| `docs` | 使用、档案格式和验证文档。 |

开发入口和发布流程见 [维护指南](GUIDE.md)。

## 许可

代码许可见 [LICENSE](LICENSE)。应用图标采用 CC BY-NC-SA 4.0，来源、署名、使用要求及替代图标见 [第三方声明](THIRD_PARTY_NOTICES.md)。依赖与模型的许可证随发行包附带。本项目与所支持的翻译服务商无隶属或背书关系。
