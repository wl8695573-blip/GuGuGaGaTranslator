# 游戏档案格式

主界面“术语与游戏 → 导入档案”会打开编辑器，保存时生成本机独立 ID。取消时不会加入配置或替换同名档案。“导出当前档案”只导出选中的档案。

```json
{
  "format": "gugugaga-game-profile",
  "schemaVersion": 2,
  "profile": {
    "id": "example",
    "name": "示例作品",
    "author": "示例作者",
    "sourceUrl": "https://example.com/",
    "license": "CC0-1.0",
    "profileVersion": "1.0",
    "windowHints": ["示例作品"],
    "processHints": ["ExampleGame"],
    "from": "ja",
    "to": "zh-Hans",
    "ocrLanguage": "ja",
    "worldview": "原创示例：旅人在村庄约定再次见面。",
    "styleHint": "使用自然的对话语气。",
    "terms": [
      { "language": "ja", "source": "旅人", "target": "旅人", "forbidden": ["旅行者"], "reviewStatus": "已核对", "enabled": true }
    ]
  }
}
```

`author`、`sourceUrl`、`license`、`profileVersion`、设定与语言字段可省略。许可由档案作者填写，不会由程序自动授予；上例许可只用于这份原创示例。

限制：UTF-8 文件最多 1 MB，最多 5000 条术语；名称最多 200 字，世界观、备注与风格各最多 20000 字。来源网址须为不含账户密码的 HTTP/HTTPS。LCTA 1.5.0 导出格式版本 2，仍可导入旧版格式 1；旧程序不能导入新格式。未知字段会被拒绝，不接受整个 config.json。

`processHints` 按进程名精确匹配，优先于标题关键字。浏览器标题不会自动匹配游戏；仍可手动选择档案。

术语新增可选的 `sourceUrl`（出处）和 `reviewStatus`（核对状态），`enabled` 默认 `true`。`enabled: false` 或状态为“待核对”的条目不会参与提示词与译后校正。旧条目备注含“待核对”且没有明确核对状态时，也按待核对处理。

编辑器的等价写法：`en: Name = 译名 | 出处: https://example.com/source | 核对: 待核对 | 启用: 否`。核实后改为“已核对”并启用。个人术语覆盖当前档案；“从内置边狱档案新建副本”不会覆盖自己的修改。

翻译语言为 `ja`、`en`、`zh-Hans`、`ko`，源语言还可用 `auto`；目标语言不接受 `auto`。术语原文可标 `ja`、`en`、`zh`、`ko`；不标语言保留旧格式“外语原文 → 中文译名”的含义。同一语言中的相同原文不可重复，译名相同也需合并成一条。

档案文件包含你填写的设定、术语及作者信息。分享前可在编辑器查看全部内容。翻译服务地址、账户、API Key 和缓存均不在档案格式中。

## 个人术语与公共词库

“即时术语”保存的是按翻译方向区分的个人条目，优先于游戏档案，添加或修改后重新识别当前句。导出当前游戏档案不包含这份独立个人列表；不要把整个配置文件当成档案公开。

官方公共更新只修改内置 ID 为 `limbus-company` 的档案词表，保留个人修改、禁用、删除及自建档案。导入的分享档案保存为独立副本，不会被公共词库覆盖。取消编辑也不产生副本。

公共版本、SHA256 清单和维护流程见 [词库说明](../terminology/README.md)。四语翻译入口不意味着档案必须同时具有四种专名；尚未收录的语言使用通用翻译与个人术语。
