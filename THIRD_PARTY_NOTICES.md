# Third-party notices / 第三方素材声明

代码、图标和第三方依赖分别适用各自的许可。

## LCTA“边·译”图标

当前图标采用维护者选定的黑、橙、白“边·译”字标，由图像生成工具按维护者的构图、字形和修改要求制作。设计参考了 Project Moon《边狱巴士》中 H 公司的标志，重新组合了文字和连接结构。

- 原始字标：`src/GuGuGaGaTranslator.App/Assets/lcta-wordmark.png`。
- 窗口和程序图标：同目录下的 `icon.png` 与 `icon.ico`。
- 转换工具：`tools/Icon`；按原比例居中生成 16、24、32、48、64、128、256 像素图标。

该设计不代表与 Project Moon 或零协会合作，也不表示获得其背书。本仓库的代码许可不授予 Project Moon 原标志及其他第三方内容的权利；免费、开源及重新绘制不自动解决相关授权问题。

## 旧版社区图标

已发布的 GuGuGaGaTranslator 1.3.2 等旧版使用鲸鱼娘社区图标。为保留历史署名，记录如下；它不再是当前开发版的应用图标。

| 项 | 内容 |
|---|---|
| 来源仓库 | [fornarwhal/deepseek-whale-girl-icon](https://github.com/fornarwhal/deepseek-whale-girl-icon) |
| 角色形象来源 | 上善无形，原创 OC“溟月” |
| DeepSeek 元素二创 | ZipZipPipe，GPT Image 2 |
| 改进版修复 | QYQCAMIAO |
| 来源标示的许可 | CC BY-NC-SA 4.0，署名、非商业使用、相同方式共享 |

该来源仓库亦说明图片的原作者未完全确认。保留本记录不等于确认其全部权利链。使用或改编旧图标时，应核实来源并遵守适用许可；当前代码许可不替代图标许可。

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
| Microsoft.Data.Sqlite / Core | 10.0.12 | MIT；dotnet/efcore，随包 NuGet 许可证 |
| SQLitePCLRaw / SQLite 原生库 | 以 build-manifest.json 为准 | 随包 NuGet 元数据及许可证；SQLite 为公共领域 |
| .NET / Windows Desktop Runtime | 发布时的 .NET 10 运行时 | 见随包 LICENSE 与 THIRD-PARTY-NOTICES |

## 其他第三方内容

| 内容 | 来源 | 许可 |
|---|---|---|
| RapidOCR / PP-OCRv6 ONNX 模型(`models/v6/`) | [RapidAI/RapidOCR](https://github.com/RapidAI/RapidOCR) | 见其仓库(Apache-2.0) |
| 韩语 PP-OCRv5 mobile ONNX 与字典 (`models/korean/`) | [RapidAI 官方模型列表](https://github.com/RapidAI/RapidOCR/blob/main/python/rapidocr/default_models.yaml)、[PaddleOCR](https://github.com/PaddlePaddle/PaddleOCR)；下载路径与哈希见 `models/korean/README.md` | Apache-2.0；随包保留来源与现有许可文件 |
| 各翻译服务(DeepSeek、彩云、有道、百度等)的接口 | 各自官方开放平台 | 各自的开发者协议;本程序只按协议调用,不分发其内容 |
