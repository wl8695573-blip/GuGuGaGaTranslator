# Third-party notices / 第三方素材声明

本仓库的**代码**和**里面的图片**是两回事,许可也分开看。下面逐项说明来源、作者与义务。

## 应用图标 / 吉祥物:鲸鱼娘(社区二创)

`src/GuGuGaGaTranslator.App/Assets/icon.ico`(多尺寸 16–256)与 `icon.png`(256×256,窗口图标)是社区二创作品,**不是 DeepSeek 官方素材**。

| 项 | 内容 |
|---|---|
| 来源仓库 | [fornarwhal/deepseek-whale-girl-icon](https://github.com/fornarwhal/deepseek-whale-girl-icon) |
| 角色形象来源 | **上善无形**(原创 OC「溟月」) |
| DeepSeek 元素二创 | **ZipZipPipe**(GPT Image 2) |
| 改进版修复 | **QYQCAMIAO** |
| 许可协议 | **CC BY-NC-SA 4.0**(署名 · 非商用 · 相同方式共享) |

**使用这份图标就等于接受三条义务**:

1. **署名** —— 上面那三行署名必须保留。本文件就是署名处,公开发布时不要删掉;
2. **非商用** —— 不得用于以营利为目的的发布或宣传(收费、广告变现、随硬件搭售等);
3. **相同方式共享** —— 基于这张图改出来的版本(例如你自己重画的),必须以同样的 CC BY-NC-SA 4.0 分发。

> **注意**:该来源仓库自己声明"图片来自网络流传,具体作者未确认;如原作者认为不妥,请联系删除"。也就是说这份图标的**来源链本身是不确定的** —— 它继承了这个不确定性。想彻底避开,用下面那个替代品。

**代码不受这份 CC 协议影响**:CC BY-NC-SA 只覆盖图片本身,不传染到程序代码。

## 免授权的替代图标(本仓库自己画的)

`tools/GuGuGaGaTranslator.Icon` 用 WPF 的矢量 API 画了一只自己的 Q 版鲸鱼(戴女仆头带),输出 `drawn-mascot.ico` / `drawn-mascot.png`,**不涉及任何第三方权利,可以随便用(包括商用)**:

```powershell
dotnet run --project tools/GuGuGaGaTranslator.Icon
Copy-Item src\GuGuGaGaTranslator.App\Assets\drawn-mascot.ico src\GuGuGaGaTranslator.App\Assets\icon.ico -Force
Copy-Item src\GuGuGaGaTranslator.App\Assets\drawn-mascot.png src\GuGuGaGaTranslator.App\Assets\icon.png -Force
```

换完重新 `.\build.ps1 -Publish -SingleFile -Zip` 即可,工程文件里指向的就是 `icon.ico` / `icon.png` 这两个名字,不用改代码。

## DeepSeek 名称与商标

本程序可以调用 DeepSeek 的 API,但**与 DeepSeek 官方没有任何关系,也未获其背书**。"DeepSeek" 名称与标识归其权利人所有 —— DeepSeek 的模型与代码许可是开源的,**其中明确不包含商标许可**,形象与商标不在开源范围内。

## 运行库

发行包中的 `licenses/` 目录包含 NuGet 包及 .NET 运行时自带的许可证和第三方声明。

| 组件 | 版本 | 许可及来源 |
|---|---|---|
| RapidOcrNet | 4.2.0 | Apache-2.0；BobLd/RapidOcrNet |
| Microsoft.ML.OnnxRuntime / Managed | 1.29.0 | MIT；Microsoft/onnxruntime |
| SkiaSharp / NativeAssets.Win32 | 3.119.1 | MIT；mono/SkiaSharp，原生依赖见包内 THIRD-PARTY-NOTICES |
| Clipper2 | 2.0.0 | 见随包 NuGet 元数据及上游许可证 |
| System.Numerics.Tensors | 9.0.0 | MIT；dotnet/runtime |
| .NET / Windows Desktop Runtime | 发布时的 .NET 10 运行时 | 见随包 LICENSE 与 THIRD-PARTY-NOTICES |

## 其他第三方内容

| 内容 | 来源 | 许可 |
|---|---|---|
| RapidOCR / PP-OCRv6 ONNX 模型(`models/v6/`) | [RapidAI/RapidOCR](https://github.com/RapidAI/RapidOCR) | 见其仓库(Apache-2.0) |
| 各翻译服务(DeepSeek、彩云、有道、百度等)的接口 | 各自官方开放平台 | 各自的开发者协议;本程序只按协议调用,不分发其内容 |
