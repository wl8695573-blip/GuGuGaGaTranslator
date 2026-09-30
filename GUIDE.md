# 维护指南

用户操作见 [使用说明](docs/USAGE.md)。本页说明代码入口、构建、检查和发布流程。

## 环境与构建

需要 Windows 10 2004（19041）或更高版本和 .NET 10 SDK。`models/v6` 的模型及字典已提交到仓库，完整克隆后即可构建。

```powershell
git clone https://github.com/wl8695573-blip/GuGuGaGaTranslator.git
cd GuGuGaGaTranslator
.\build.ps1 -Configuration Debug
.\src\GuGuGaGaTranslator.App\bin\Debug\net10.0-windows10.0.19041.0\GuGuGaGaTranslator.exe
```

文件被占用时，先退出使用该输出目录的程序。版本由 `Directory.Build.props` 定义，格式约定见 `.editorconfig`。

## 数据流程

```text
窗口选择和框选 → 客户区坐标换算 → 抓屏与自身窗口遮罩
  → 画面变化判断和 OCR → 文本整理与重复检查
  → 缓存或翻译服务 → 术语校正 → 主界面和悬浮层
```

`AppSession` 组装服务、加载配置并管理生命周期。`TranslationPipeline` 分开执行 OCR 和网络翻译，新台词会取消过期请求。界面通过事件接收状态，后台通知异步转入 WPF Dispatcher，避免与缓存锁相互等待。

## 修改位置

| 内容 | 入口 |
|---|---|
| 主窗口、设置、主题 | `App/MainWindow.xaml`、`MainWindow.xaml.cs`、`SetupWindow.xaml.cs`、`App.xaml`。 |
| 框选和悬浮层 | `App/RegionSelectorWindow.cs`、`OverlayWindow.cs`、`LanguageBarWindow.cs`。 |
| 抓屏和区域 | `Core/Capture/ScreenCapture.cs`、`TargetRegionResolver.cs`、`SelfWindowMask.cs`。 |
| OCR | `Core/Ocr` 定义接口和 Windows 实现；`Ocr.Rapid` 实现 RapidOCR。 |
| 翻译 | `Core/Translation/ITranslator.cs`、`OpenAiCompatibleTranslator.cs`、`ClassicApis.cs`。 |
| 术语与档案 | `TermSheetBuilder.cs`、`TermEnforcer.cs`、`GameProfileArchive.cs`。 |
| 配置与密钥 | `Core/Config/AppConfig.cs`、`ConfigStore.cs`、`SecretProtection.cs`。 |
| 缓存与诊断 | `Core/Translation/TranslationCache.cs`、`Storage.Sqlite`、`Core/Diagnostics`。 |
| 安装与卸载 | `tools/Installer`、`src/Shared/InstallationManifest.cs`、`App/Uninstall.cs`。 |

表中的 `App`、`Core`、`Ocr.Rapid`、`Storage.Sqlite` 对应 `src/GuGuGaGaTranslator.*` 项目。

## 维护约定

- 区分 WPF 设备无关单位与抓屏物理像素；选区带参考客户区尺寸，旧配置继续按像素处理。
- 网络请求传递取消令牌，超时覆盖正文读取；停止时释放 OCR 会话。
- 服务商扩展参数只发给已知支持的地址，通用兼容接口使用标准字段。
- 新配置使用兼容默认值。密钥按账户加密，不写入日志或诊断。
- 诊断采用字段白名单，不直接序列化配置、请求、异常、台词或档案。
- 档案先校验再导入编辑器，保存为副本。格式见 [档案分享](docs/PROFILES.md)。
- 安装和卸载校验登记目录、实例标记及文件清单，保留用户额外文件。
- 注释解释约束、单位和特殊处理，避免重复方法名及没有依据的速度、质量保证。

## 检查入口

```powershell
dotnet build GuGuGaGaTranslator.slnx -c Release
dotnet run --project tools/Regression -c Release
dotnet run --project tools/Benchmark -c Release -- --engine rapid --iterations 3 --check
dotnet run --project tools/UiPreview -c Release -- .artifacts/ui
dotnet run --project tools/InteractionChecks -c Release -- .artifacts/interactions
```

`tools/Probe` 检查窗口、抓屏、识别和翻译；`tools/SampleWindow` 提供合成对话窗口。使用临时配置和合成台词，避免提交真实台词或密钥。报告定义和检查范围见 [验证与发布](docs/VERIFICATION.md)。

`tools/InteractionChecks` 使用真实模态菜单检查选择、取消、失焦和关闭期间的事件顺序，并检查运行中语言切换。窗口位于屏幕外，不读取用户配置或抓取桌面。

辅助脚本：`start-ollama.ps1` 启动已安装的本地服务，`setup-sakura.ps1` 注册 Sakura 模型，`compare-models.ps1` 比较本地模型处理相同文本的结果。可指定路径，使用前确认模型许可。

## 版本发布

1. 更新 `Directory.Build.props`、README 下载链接、CHANGELOG 和 `tools/release-notes.md`。
2. 提交改动，确认 GitHub 检查通过。
3. 执行完整构建和包验收：

   ```powershell
   .\build.ps1 -Publish -SingleFile -Zip -Installer
   .\tools\verify-release.ps1
   ```

4. 将检查通过的提交合并到 `main`，本地检出同一提交。同版本重复构建时，将旧 `dist` 文件移到备份目录。
5. 使用有发布权限的 GitHub 凭据运行 `.\tools\publish.ps1 -SkipPush`。

发布脚本先创建草稿，上传安装器、便携包和校验文件，核对大小与服务端摘要后公开。已公开的同名版本不会被覆盖。发布后检查三项资产和匿名下载。

当前发行包未签名；证书要求和命令见 [签名说明](docs/VERIFICATION.md#正式签名)。SHA256 不能替代发布者签名。
