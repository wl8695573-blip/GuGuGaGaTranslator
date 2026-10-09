# 验证与发布

需要 Windows 10 19041+ 与 .NET 10 SDK。从仓库根目录执行：

```powershell
dotnet build GuGuGaGaTranslator.slnx -c Release
dotnet run --project tools/Regression -c Release
dotnet run --project tools/Benchmark -c Release -- --engine rapid --iterations 3 --check
dotnet run --project tools/Benchmark -c Release -- --engine windows --iterations 3 --check
dotnet run --project tools/UiPreview -c Release -- .artifacts/ui
dotnet run --project tools/InteractionChecks -c Release -- .artifacts/interactions
```

在具有交互桌面的 Windows 本机追加窗口捕获与完整流程检查：

```powershell
dotnet run --project tools/InteractionChecks -c Release -- .artifacts/interactions --capture
```

`--capture` 会显示临时合成窗口，并使用独立配置和 mock 翻译，不读取现有配置、不调用在线服务。GitHub Actions 默认执行不抓屏的交互检查。

## 界面更新记录（1.6.2，2026-10-09）

- Release 编译通过，0 警告、0 错误。
- 使用独立配置与合成台词渲染实际 WPF 控件，检查 1140 × 780 主界面、900 × 660 最小窗口和悬浮球快捷导航的排版。
- 主界面、最小窗口和导航截图已随源码提交到 `assets`。渲染不打开用户桌面窗口，不读取用户配置或调用翻译服务。

本机此次没有重跑翻译回归或真实游戏交互；下面的翻译检查记录属于注明的历史版本。

## OCR 样本与指标

`tools/Benchmark/fixtures` 已提交 27 张固定 PNG：中、日、英 × 100%/125%/150% × 深底/浅底/低对比度。台词为项目原创合成例句，使用 Microsoft YaHei UI 渲染。重建样本使用 `--generate`，常规回归直接读取固定图片。

每例先预热一次，再测量指定次数。CER 为 NFKC 规范化、忽略空白后的 Unicode 字符编辑距离 / 参考字符数，报告每例最差 CER、中位与 P95 端到端 OCR 延迟；初始化单独计时。默认阈值每例 15%，`--check` 失败返回 1；全部被跳过也返回 1。Windows 未安装的 OCR 语言明确标为 SKIP。P95 在只有三次测量时为最大样本，不能据此推断生产尾延迟。

报告写入 `.artifacts/benchmark-rapid` 或 `.artifacts/benchmark-windows`，含 JSON、Markdown、系统/.NET/逻辑处理器数。可指定 `--fixtures`、`--models`、`--iterations`、`--output` 使用本地语料，清单字段与固定 corpus 一致。自备真实游戏样本应具有可分享的授权，并避免包含用户隐私。

## LCTA 本机记录（1.6.0，2026-10-08）

本次使用隔离配置、合成窗口和模拟 HTTP 服务，没有使用用户密钥或调用付费翻译接口。

| 项目 | 结果 | 范围与限制 |
|---|---|---|
| Release 编译 | 0 警告、0 错误 | Windows 11 / .NET 10.0.401 SDK |
| 回归 | 161 项通过 | 原有四语与术语更新；新增模型列表 GET、认证、地址规范化、缓存关闭／容量／过期／上下文、数据迁移／取消／原目录保留／密钥与词表读取 |
| 交互 | 214 项通过 | 实际菜单关闭、无表面关闭按钮与透明缺口、服务预设与模型选择、目录验证、缓存保存、较小桌面设置窗口；完整捕获 → OCR → mock → WPF |
| 界面 | 实际 WPF 视图渲染检查通过 | 主界面、服务、支持、文件位置、首次设置、悬浮字标、最小尺寸；合成台词与独立路径 |

日志为 `.artifacts/build-1.6-final.txt`、`regression-1.6-final.txt`、`interactions-1.6-final.txt`、`ui-1.6-final.txt`。安装包应使用当前构建执行下列检查，不能沿用历史包的结果：

```powershell
.\build.ps1 -Publish -SingleFile -Zip -Installer
.\tools\verify-release.ps1 -Version 1.6.0 -LegacyInstaller .\dist\LCTA-Setup-1.5.0.exe
```

原生运行库在应用目录，便携包必须完整解压。包验收检查实际 OCR、SQLite、DPAPI、WPF、安装清单、升级与卸载；测试登记和快捷方式在结束后还原。线上服务只验证兼容协议与模拟返回，未验证真实账户权限、网络与译文质量。没有重跑完整 OCR 基准，下面的基准数据属于 1.5.0。

## 历史 LCTA 本机记录（1.5.0，2026-10-08）

使用独立配置、合成窗口和 mock 翻译，没有读取用户密钥或发送付费翻译请求。

| 项目 | 结果 | 范围与限制 |
|---|---|---|
| Release 编译 | 0 警告、0 错误 | Windows 11 / .NET 10.0.401 SDK |
| 回归 | 122 项通过 | 四语方向、韩语标签、选区迁移、个人术语、公共三方合并、保存失败回滚、禁用与删除、版本与 SHA256、安装包取消和坏哈希 |
| 交互 | 202 项通过 | 完整捕获 → OCR → mock → WPF；10 轮关闭重启；悬浮球点击、拖动、关闭重开和提醒；手动选区保存；个人术语管理；同一原文添加、修改、删除后重译 |
| 韩语模型 | 9/9，CER 0%；中位约 289–310 ms | 三次/例，短边 320，合成样本；[指标](benchmark-lcta-korean.json) |
| 自动四语 OCR | 36/36，CER 0%；单次约 516–659 ms | 中日英韩 × 三种 DPI × 三种背景；一次/例，含预热；[指标](benchmark-lcta-auto-four.json)，不能据此推断生产 P95 |
| 界面 | 主窗口、最小尺寸、术语管理、支持页等预览通过 | 修复原文区域被挤没和列宽过窄；880×620 内容区与 150% 像素输出 |
| 包与升级 | 安装、1.3.2 与 1.4.0 升级、再次升级、卸载与便携自检通过 | 包含韩语模型和公共词库；校验 SHA256，保留安装 ID 与用户额外文件；临时目录验收 |
| 词库 | 253 条档案与版本清单校验一致 | 原有英文、日文及中文译名，13 条禁用候选；不等于全部出处已核实或具备完整韩语专名 |

旧线程问题在关闭后重启检查中复现；改为在 UI Dispatcher 上启动并显示错误后，重跑完整检查通过。即时术语检查从实际目标窗口读取未变化的台词，确认更新后当前句重译。

公共更新测试使用本地 HTTP 替身，涵盖正确与错误校验、取消、旧文件保留、三方合并及保存失败回滚。发布后还需核对真实 GitHub 清单和正式版本入口；模拟结果不能代替公开文件验证。

四语测试语料临时生成，不覆盖仓库原有 27 张固定样本：

```powershell
dotnet run --project tools/Benchmark -c Release -- --generate --fixtures .artifacts/ocr-four-language
dotnet run --project tools/Benchmark -c Release -- --engine korean --fixtures .artifacts/ocr-four-language --iterations 3 --check
dotnet run --project tools/Benchmark -c Release -- --engine auto --fixtures .artifacts/ocr-four-language --iterations 1 --check
dotnet run --project tools/TermLibrary -c Release -- terminology --check
```

安装包增加韩语模型、字典及内置词库；构建清单记录韩语模型哈希，包自检实际加载两类 OCR 与词库。1.5.0 最终包的安装与两类旧版升级、公开下载和 SHA256 核对均已通过，记录见 `.artifacts/package-acceptance-1.5-final-132.txt`、`package-acceptance-1.5-final-140.txt` 和 `.artifacts/public-1.5-verification/release-audit.json`。每次构建不能沿用旧包结果。

原始日志位于 `.artifacts/build-1.5-final.txt`、`regression-1.5-final.txt`、`interactions-1.5-final.txt`、`ocr-auto-four.txt`、`ocr-korean.txt`，临时数据不提交。

尚未覆盖真实边狱巴士剧情、真实在线译文质量与费用、混合 DPI 跨屏实机、独占全屏、全部显卡和长时间内存基线。窗口捕获仍受目标绘制状态及受保护内容限制，屏幕捕获无法恢复已被遮住的文字。历史术语逐条出处和图标使用条件仍需维护；没有声称取得合作或未确认的授权。
## 历史 LCTA 本机记录（1.4.0，2026-10-08）

版本状态：**本地测试版，未上传 GitHub**。使用隔离配置、合成窗口和 mock 翻译；没有读取用户密钥或向真实翻译服务发出付费请求。

| 项目 | 结果 | 限制 |
|---|---|---|
| Release 编译 | 0 警告、0 错误 | Windows 11 / .NET 10.0.401 SDK；运行时 10.0.12 |
| 回归 | 82 项通过 | 包含文字稳定门限、逐字字幕、每句最多 3 次自动尝试、过期请求、匹配术语、候选禁用、旧档案导入、目标设置与手动语言优先、密钥和诊断保护 |
| 交互 | 155 项通过 | 20 次外观预设、24 次方向、20 次档案切换；10 轮关闭重启；实际窗口捕获与 RapidOCR → mock 翻译 → WPF；底部避让和 DPI 换算后的拖动 |
| RapidOCR | 27/27，CER 0%；每例中位约 319–398 ms，初始化约 461 ms | CPU、短边 320、3 次/例；中日英合成样本；[完整指标](benchmark-lcta-rapid.json) |
| WPF 界面 | 主窗口、首次设置、两种服务设置、档案、支持页、悬浮层和控制条已渲染检查 | 最小内容区域 880×620，150% 像素输出；不能代替实际游戏中可读性评价 |
| 安装与升级 | 新安装、1.3.2 → LCTA、LCTA 再次升级、卸载通过 | 隔离中文与空格目录；保留安装实例 ID 和用户额外文件，旧可执行入口移除；登记与快捷方式测试结束后还原 |
| 便携包 | SHA256、模型哈希、SQLite / DPAPI / OCR / WPF 自检、拒绝便携卸载通过 | 包含运行时；未签名；独立解压启动检查使用临时配置 |

重试验收先发现旧版升级时安装实例 ID 被重建，修正后重新打包检查通过。最终发行文件仍须每次执行下列包验收命令，不能用旧包结果代替新包结果。

```powershell
.\build.ps1 -Publish -SingleFile -Zip -Installer
.\tools\verify-release.ps1 -LegacyInstaller .\dist\GuGuGaGaTranslator-Setup-1.3.2.exe
```

`-LegacyInstaller` 可省略；省略时只检查当前安装器的安装、升级和卸载。指定的旧包必须来自可信的项目发行文件。测试会临时操作应用登记与快捷方式，结束时还原原有登记和快捷方式，使用独立目录，不删除真实配置。

本机原始日志在 `.artifacts/build-lcta.txt`、`regression-lcta.txt`、`interactions-lcta.txt`、`package-build-lcta.txt`、`package-acceptance-lcta.txt`；临时文件不提交。UI 截图保存在 `assets/screenshot-*.png`。

**尚未覆盖：** 真实《边狱巴士》剧情与英文报纸样本、真实在线服务的译文质量和费用、混合 DPI 跨屏实机、独占全屏、各显卡硬件加速与 PrintWindow 差异、长时间运行与内存基线。历史术语逐条出处和图标使用条件仍需整理；未取得的许可或合作没有作为事实写入文档。Windows OCR 本次未重跑，下面的结果只属于 1.3.2。

## 历史本机记录（1.3.2，2026-10-02）

| 项目 | 结果 | 限制 |
|---|---|---|
| Release 编译 | 0 警告、0 错误 | Windows 11 / .NET 10.0.401 SDK |
| 回归 | 54 项通过 | 包括加密、TTL/LRU、损坏数据回退、隐私、档案冲突、取消、超时和服务商扩展参数 |
| RapidOCR | 27/27 通过，CER 0%，每例中位约 268–335 ms | CPU、短边目标 320、3 次/例；合成样本 |
| Windows OCR | 18/18 可用样本通过，CER 0%，中位约 5–20 ms；日语 9 例 SKIP | 本机缺少日语 OCR 功能 |
| 区域坐标 | 100/125/150/200% 比例、负坐标、边界与旧配置通过 | 纯坐标检查，不是多显示器实机验收 |
| WPF 界面 | 主界面、设置、服务、缓存、档案与悬浮层已渲染检查；首次设置保留自定义值、切换服务清除密钥通过 | 880×520 最小内容区域及 150% 像素输出；不显示桌面窗口 |
| 交互（1.3.2） | 86 项通过：原有菜单和语言切换、外观预设、控制条拖动与关闭、重启、长内容滚动、配置迁移、遮挡捕获、窗口移动/缩放/关闭 | 默认 69 项；额外 17 项通过 `--capture` 使用本机临时测试窗口、实际 RapidOCR 与 mock 翻译；无在线请求 |
| 包与安装 | SHA256/模型哈希、安装、升级、卸载保留用户文件、便携包自检与拒绝卸载通过 | 中文与空格路径；验收包未签名 |

窗口捕获检查包含完全覆盖目标窗口、译文与原文重叠、允许录屏显示译文、目标窗口移动/缩放与关闭；完整流程检查从实际窗口捕获到主界面和悬浮层更新，并测试运行中切换四种外观预设、关闭与重启。测试使用合成英文窗口，未调用收费接口。

真实游戏、混合 DPI 显示器移动、独占全屏、不同显卡的硬件加速抓屏、PrintWindow 兼容性和有凭据的在线翻译，仍需按目标环境验收。请把版本、引擎、缩放、窗口模式、实际现象和诊断 ZIP 附到反馈中。当前结论没有覆盖这些环境。

## 完整包验收

```powershell
.\build.ps1 -Publish -SingleFile -Zip -Installer
.\tools\verify-release.ps1
```

脚本核对发布 SHA256，检查拒绝占用目录、安装文件清单、SQLite/DPAPI/RapidOCR/WPF 无界面自检、覆盖升级、卸载保留用户文件、便携包自检及拒绝卸载。测试使用 `.artifacts/verify-*`，备份并恢复该产品的 HKCU 卸载登记与快捷方式。不要在同一账户同时运行多个安装验收。

LCTA 开发版自检入口为 `LCTA.exe --config-dir <测试目录> --verify-installation <报告.json>`；已发布的 1.3.2 使用 `GuGuGaGaTranslator.exe`。不捕获桌面、不请求翻译服务、不显示窗口，结果通过退出码和 JSON 提供。

推送/PR 的 `build.yml` 执行回归、原有与四语自动 OCR、公共词库校验、WPF 预览和不抓屏的交互检查；`package.yml` 对相关 PR 或手动触发执行完整包验收并上传证据，默认生成未签名验收包。

## 正式签名

发布者在 Windows 当前用户证书存储中配置含私钥且未过期的代码签名证书，安装 Windows SDK 的 SignTool，然后：

```powershell
$env:GGGT_SIGN_CERTIFICATE_THUMBPRINT = '<证书 SHA1 指纹>'
.\build.ps1 -Publish -SingleFile -Zip -Installer -RequireSigning
```

先签名主 exe，再打包，再签名安装器。使用 SHA256 文件摘要、HTTPS RFC3161 时间戳和 SHA256 时间戳摘要，签名后执行 `signtool verify /pa /all`。指纹通过环境变量或 `-SignCertificateThumbprint` 提供，私钥与证书密码不写入源码。没有证书时 `-RequireSigning` 立即失败；未要求签名时明确输出 Unsigned。

ZIP 内的 `build-manifest.json` 记录应用 SHA256、签名状态、模型 SHA256、NuGet 包版本与 SHA512；发布包继续包含许可证。顶层 SHA256SUMS 在签名完成后生成。需要重新构建同版本时，先将已有 dist 中同名发行物移到备份位置，避免误覆盖。SHA256 清单不能替代可信发布者签名。
