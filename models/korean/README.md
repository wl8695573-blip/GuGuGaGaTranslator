# 韩语识别模型

模型来自 RapidAI 的官方 [模型列表](https://github.com/RapidAI/RapidOCR/blob/main/python/rapidocr/default_models.yaml)，版本路径为 `v3.9.2`，识别结构为 PaddleOCR 的 PP-OCRv5 mobile。

- 模型：`korean_PP-OCRv5_rec_mobile.onnx`
- SHA256：`cd6e2ea50f6943ca7271eb8c56a877a5a90720b7047fe9c41a2e541a25773c9b`，下载后已核对。
- 字典：同一模型目录的 `ppocrv5_korean_dict.txt`。
- 检测器与方向分类器复用 `models/v6`；韩语识别器和字典来自本目录。

韩语能力说明见 PaddleOCR 的 [多语言识别文档](https://paddlepaddle.github.io/PaddleOCR/latest/en/version3.x/algorithm/PP-OCRv5/PP-OCRv5_multi_languages.html)。许可与第三方说明见项目的 `THIRD_PARTY_NOTICES.md` 与 `licenses` 目录。实际字体与游戏兼容性需单独验收。
