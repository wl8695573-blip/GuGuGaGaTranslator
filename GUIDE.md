# GuGuGaGaTranslator 维护指南

本文说明常见改动的位置、构建方式和验证入口。功能使用说明请看 [README](README.md)。

## 工程结构

| 目录 | 内容 |
|---|---|
| `src/GuGuGaGaTranslator.Core` | 抓屏、配置、OCR 接口、翻译和轮询逻辑；不依赖 NuGet。 |
| `src/GuGuGaGaTranslator.App` | WPF 主窗口、框选窗口、悬浮层和应用生命周期。 |
| `src/GuGuGaGaTranslator.Ocr.Rapid` | RapidOCR 适配器。 |
| `tools/Probe` | 命令行验证工具。 |
| `tools/SampleWindow` | 用于抓屏和框选的示例窗口。 |
| `tools/Installer` | 当前用户范围的安装程序。 |

配置文件位于 `%APPDATA%\GuGuGaGaTranslator\config.json`。升级时请保留未知字段和已有用户配置。

## 构建和运行

需要 Windows 10 19041 或更新版本，以及 .NET 10 SDK。

```powershell
cd X:\kotoba
.\build.ps1 -Configuration Debug
```

启动调试版本：

```powershell
.\src\GuGuGaGaTranslator.App\bin\Debug\net10.0-windows10.0.19041.0\GuGuGaGaTranslator.exe
```

程序运行时，Windows 会锁定输出文件。构建失败并提示文件被占用时，先退出程序及安装程序再重试。XAML 更改需要重新构建后才能看到效果。

版本统一定义在 `Directory.Build.props`，应用清单与安装器在构建时同步。完整打包命令为 `.\build.ps1 -Publish -SingleFile -Zip -Installer`；提交并验证后运行 `.\tools\publish.ps1` 上传新 Release。

## 修改界面和主题

全局颜色、字体和 WPF 控件样式定义在 `src/GuGuGaGaTranslator.App/App.xaml`。常规界面优先通过资源和样式修改；框选窗口、悬浮层和语言条是无边框窗口，颜色通过 `Theme` 读取相同资源。

常用入口：

- 主界面：`MainWindow.xaml` 和 `MainWindow.xaml.cs`
- 悬浮层：`OverlayWindow.cs`
- 语言条：`LanguageBarWindow.cs`
- 框选窗口：`RegionSelectorWindow.cs`
- 首次设置：`SetupWindow.xaml` 和 `SetupWindow.xaml.cs`

悬浮层的尺寸使用 WPF 设备无关单位；移动窗口时使用的是物理像素。修改位置计算或 DPI 相关代码时，不要混用这两套坐标。

## 修改抓屏和区域逻辑

目标窗口和区域由 `AppSession` 组装，并传给 `TranslationPipeline`。区域保存为目标客户区内的物理像素坐标，因此窗口移动和缩放后可以重新计算屏幕位置。

相关文件：

- `Core/Capture/ScreenCapture.cs`：BitBlt 和 PrintWindow 抓屏。
- `Core/Capture/SelfWindowMask.cs`：在 OCR 前遮盖会出现在截图中的本程序窗口。
- `Core/Capture/FrameHasher.cs`：判断画面是否变化。
- `App/RegionSelectorWindow.cs`：用户框选和坐标转换。

修改后至少用两个显示器缩放比例测试：100% 和非 100%。还应测试窗口移动、最小化、目标窗口被遮挡、以及悬浮层不排除捕获时的行为。

## 修改 OCR

OCR 的公共接口在 `Core/Ocr/ITextRecognizer.cs`。Windows OCR 的实现位于 Core，RapidOCR 的实现位于 `GuGuGaGaTranslator.Ocr.Rapid`。

添加 OCR 引擎时：

1. 实现 `ITextRecognizer`，并正确释放模型或会话资源。
2. 在 `AppSession` 中创建并在停止时释放实例。
3. 为不可用的语言、模型路径和初始化失败提供明确错误。
4. 用 `tools/Probe` 处理一张固定图片，记录识别结果和耗时。

RapidOCR 模型不在源码仓库中。发布包必须包含 `models` 目录及其文件结构。

## 修改翻译和术语表

翻译器实现 `Core/Translation/ITranslator.cs`。OpenAI 兼容接口的提示词和请求组装在 `OpenAiCompatibleTranslator.cs`；术语表解析与强制替换在 `TermSheetBuilder.cs`、`TermEnforcer.cs`。

新增翻译服务时：

1. 不记录 API Key、台词或完整 HTTP 请求到日志。
2. 为超时、非成功状态码和无效 JSON 返回可显示的错误。
3. 保持取消令牌可传递到网络请求。
4. 明确是否支持术语表、世界观和上下文；不支持时不要在界面中暗示这些设置会生效。

修改提示词后，使用“查看提示词”检查系统提示词和用户文本的边界。屏幕文本应始终被当作待翻译内容，而不是指令。

## 修改配置

`Core/Config/AppConfig.cs` 定义默认值，`ConfigStore.cs` 负责读取、迁移和写入。新增字段时使用兼容默认值，不要在加载旧配置时抛出异常。

用户配置中可能包含 API Key。调试时请使用临时配置，提交日志、截图或复现文件前先删除密钥。

## 验证

`tools/Probe` 用于快速验证纯逻辑和外部服务适配，`tools/SampleWindow` 用于验证窗口枚举、抓屏、框选与悬浮层。

建议的最低验证集：

```powershell
dotnet build GuGuGaGaTranslator.slnx -c Release
dotnet run --project tools\Probe -- --help
dotnet run --project tools\SampleWindow
```

手工检查以下路径：

- 首次启动后跳过设置，mock 引擎能完成抓屏和 OCR。
- 配置 OpenAI 兼容接口后，测试连接、翻译和取消操作正常。
- 框选、重新框选、暂停、热键、鼠标穿透和编辑模式可用。
- 100% 与 125% 或 150% 缩放下，框选区域和悬浮层位置一致。
- 安装、卸载、快捷方式和升级安装正常。

## 发布检查

发布包至少应包含应用程序、`models` 目录、许可证和第三方声明。安装程序仅应删除它自己创建的安装目录；安装目录由用户指定时，卸载逻辑必须验证目标属于本产品。

发布前建议执行：

```powershell
.\build.ps1 -Publish -SingleFile
```

随后在干净目录中解压或安装，完成一次首次启动、RapidOCR 识别、mock 预览和卸载测试。发行说明应描述用户可见的变化，不应包含内部调试过程。
