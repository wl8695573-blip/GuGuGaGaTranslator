# 游戏档案格式

主界面“游戏模式 → 导入档案”会打开编辑器，保存时生成本机独立 ID。取消时不会加入配置或替换同名档案。“导出当前档案”只导出选中的档案。

```json
{
  "format": "gugugaga-game-profile",
  "schemaVersion": 1,
  "profile": {
    "id": "example",
    "name": "示例作品",
    "author": "示例作者",
    "sourceUrl": "https://example.com/",
    "license": "CC0-1.0",
    "profileVersion": "1.0",
    "windowHints": ["示例作品"],
    "from": "ja",
    "to": "zh-Hans",
    "ocrLanguage": "ja",
    "worldview": "原创示例：旅人在村庄约定再次见面。",
    "styleHint": "使用自然的对话语气。",
    "terms": [
      { "language": "ja", "source": "旅人", "target": "旅人", "forbidden": ["旅行者"] }
    ]
  }
}
```

`author`、`sourceUrl`、`license`、`profileVersion`、设定与语言字段可省略。许可由档案作者填写，不会由程序自动授予；上例许可只用于这份原创示例。

限制：UTF-8 文件最多 1 MB，最多 5000 条术语；名称最多 200 字，世界观、备注与风格各最多 20000 字。来源网址须为不含账户密码的 HTTP/HTTPS。格式版本目前为 1，未知字段会被拒绝，不接受整个 config.json。

翻译语言为 `ja`、`en`、`zh-Hans`，源语言还可用 `auto`；目标语言不接受 `auto`。术语原文可标 `ja`、`en`、`zh`；不标语言保留旧格式“外语原文 → 中文译名”的含义。同一语言中的相同原文不可重复，译名相同也需合并成一条。

档案文件包含你填写的设定、术语及作者信息。分享前可在编辑器查看全部内容。翻译服务地址、账户、API Key 和缓存均不在档案格式中。
