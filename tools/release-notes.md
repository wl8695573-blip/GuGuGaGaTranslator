# GuGuGaGaTranslator 1.3.1

本版本修复悬浮栏切换选项时的闪退，并调整普通内容的翻译设置。

## 修复

- 语言和游戏档案选择菜单关闭时，不再因失焦事件重复关闭而闪退。选择、Esc、点击菜单外和外部关闭均有回归检查。
- RapidOCR 切换翻译方向时复用已加载的多语言模型，取消旧请求并清除旧上下文。
- 开启游戏档案自动匹配时，没有匹配档案的窗口会回到通用翻译。关闭自动匹配后保留手动选择。
- 新配置默认“自动识别 → 中文”；自动识别时仍可使用英文和日文档案术语。已有用户的语言设置保留。

## 下载与升级

- `GuGuGaGaTranslator-Setup-1.3.1.exe`：安装版。退出旧程序后安装到原登记目录。
- `GuGuGaGaTranslator-win-x64-1.3.1.zip`：便携版。完整解压到新目录并保留 `models` 等文件。
- `SHA256SUMS-1.3.1.txt`：下载文件的 SHA256。

适用于 Windows 10 2004（19041）及更高版本的 x64 系统，附带 .NET 运行时和 OCR 模型。配置保存在当前用户的应用数据目录；更换电脑或账户后需重新填写密钥。

发行包未进行代码签名。升级后若仍显示“日 → 中”或旧游戏档案，请在悬浮栏选择需要的方向，并在“游戏模式”选择通用翻译。下载后确认程序版本为 1.3.1，避免启动旧目录中的 1.2.0。

[使用说明](https://github.com/wl8695573-blip/GuGuGaGaTranslator/blob/main/docs/USAGE.md) · [更新记录](https://github.com/wl8695573-blip/GuGuGaGaTranslator/blob/main/CHANGELOG.md)
