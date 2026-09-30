# 验证与发布

需要 Windows 10 19041+ 与 .NET 10 SDK。从仓库根目录执行：

```powershell
dotnet build GuGuGaGaTranslator.slnx -c Release
dotnet run --project tools/Regression -c Release
dotnet run --project tools/Benchmark -c Release -- --engine rapid --iterations 3 --check
dotnet run --project tools/Benchmark -c Release -- --engine windows --iterations 3 --check
dotnet run --project tools/UiPreview -c Release -- .artifacts/ui
```

## OCR 样本与指标

`tools/Benchmark/fixtures` 已提交 27 张固定 PNG：中、日、英 × 100%/125%/150% × 深底/浅底/低对比度。台词为项目原创合成例句，使用 Microsoft YaHei UI 渲染。重建样本使用 `--generate`，常规回归直接读取固定图片。

每例先预热一次，再测量指定次数。CER 为 NFKC 规范化、忽略空白后的 Unicode 字符编辑距离 / 参考字符数，报告每例最差 CER、中位与 P95 端到端 OCR 延迟；初始化单独计时。默认阈值每例 15%，`--check` 失败返回 1；全部被跳过也返回 1。Windows 未安装的 OCR 语言明确标为 SKIP。P95 在只有三次测量时为最大样本，不能据此推断生产尾延迟。

报告写入 `.artifacts/benchmark-rapid` 或 `.artifacts/benchmark-windows`，含 JSON、Markdown、系统/.NET/逻辑处理器数。可指定 `--fixtures`、`--models`、`--iterations`、`--output` 使用本地语料，清单字段与固定 corpus 一致。自备真实游戏样本应具有可分享的授权，并避免包含用户隐私。

## 本次本机记录（2026-09-30）

| 项目 | 结果 | 限制 |
|---|---|---|
| Release 编译 | 0 警告、0 错误 | Windows 11 / .NET 10.0.401 SDK |
| 回归 | 54 项通过 | 包括加密、TTL/LRU、损坏数据回退、隐私、档案冲突、取消、超时和服务商扩展参数 |
| RapidOCR | 27/27 通过，CER 0%，每例中位约 358–589 ms | CPU、短边目标 320、3 次/例；合成样本 |
| Windows OCR | 18/18 可用样本通过，CER 0%，中位约 6–22 ms；日语 9 例 SKIP | 本机缺少日语 OCR 功能 |
| 区域坐标 | 100/125/150/200% 比例、负坐标、边界与旧配置通过 | 纯坐标检查，不是多显示器实机验收 |
| WPF 界面 | 主界面、设置、服务、缓存、档案与悬浮层已渲染检查；首次设置保留自定义值、切换服务清除密钥通过 | 880×520 最小内容区域及 150% 像素输出；不显示桌面窗口 |
| 包与安装 | SHA256/模型哈希、安装、升级、卸载保留用户文件、便携包自检与拒绝卸载通过 | 中文与空格路径；验收包未签名 |

真实游戏、混合 DPI 显示器移动、独占全屏、硬件加速抓屏、PrintWindow 兼容性和有凭据的在线翻译，仍需按目标环境验收。请把版本、引擎、缩放、窗口模式、实际现象和诊断 ZIP 附到反馈中。当前结论没有覆盖这些环境。

## 完整包验收

```powershell
.\build.ps1 -Publish -SingleFile -Zip -Installer
.\tools\verify-release.ps1
```

脚本核对发布 SHA256，检查拒绝占用目录、安装文件清单、SQLite/DPAPI/RapidOCR/WPF 无界面自检、覆盖升级、卸载保留用户文件、便携包自检及拒绝卸载。测试使用 `.artifacts/verify-*`，备份并恢复该产品的 HKCU 卸载登记与快捷方式。不要在同一账户同时运行多个安装验收。

独立自检命令：`GuGuGaGaTranslator.exe --config-dir <测试目录> --verify-installation <报告.json>`。不捕获桌面、不请求翻译服务、不显示窗口，结果通过退出码和 JSON 提供。

推送/PR 的 `build.yml` 执行回归、OCR 和 WPF 预览；`package.yml` 对相关 PR 或手动触发执行完整包验收并上传证据，默认生成未签名验收包。

## 正式签名

发布者在 Windows 当前用户证书存储中配置含私钥且未过期的代码签名证书，安装 Windows SDK 的 SignTool，然后：

```powershell
$env:GGGT_SIGN_CERTIFICATE_THUMBPRINT = '<证书 SHA1 指纹>'
.\build.ps1 -Publish -SingleFile -Zip -Installer -RequireSigning
```

先签名主 exe，再打包，再签名安装器。使用 SHA256 文件摘要、HTTPS RFC3161 时间戳和 SHA256 时间戳摘要，签名后执行 `signtool verify /pa /all`。指纹通过环境变量或 `-SignCertificateThumbprint` 提供，私钥与证书密码不写入源码。没有证书时 `-RequireSigning` 立即失败；未要求签名时明确输出 Unsigned。

ZIP 内的 `build-manifest.json` 记录应用 SHA256、签名状态、模型 SHA256、NuGet 包版本与 SHA512；发布包继续包含许可证。顶层 SHA256SUMS 在签名完成后生成。需要重新构建同版本时，先将已有 dist 中同名发行物移到备份位置，避免误覆盖。SHA256 清单不能替代可信发布者签名。
