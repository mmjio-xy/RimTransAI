# RimTransAI Local Translator

这是一个独立的 Windows 桌面翻译服务。它提供配置窗口、Windows 系统托盘驻留和与 RimTransAI 兼容的本地 OpenAI 接口。

## 使用方式

1. 启动 `RimTransAI.LocalTranslator.exe`。
2. 在窗口中选择 Hy-MT2 的 `.gguf` 模型文件，默认基线是 `Hy-MT2-1.8B-Q8_0.gguf`。
3. 选择端口（默认 `18080`）并点击“启动服务”。模型加载期间状态为“正在加载模型”；完成后为“服务已就绪”。
4. 在主程序中填写：`ApiUrl=http://127.0.0.1:18080`、`ApiKey=local`、`TargetModel=hy-mt2-1.8b`、`TargetLanguage=ChineseSimplified`，并关闭自定义 Prompt。

窗口关闭时会隐藏到系统托盘，服务继续运行。通过托盘菜单可以显示窗口、启动/停止服务或退出。选择“退出”才会停止服务并结束进程。

## 本地接口

- `GET /health`：模型状态；就绪时返回 `200`，加载中或失败时返回 `503`。
- `GET /v1/models`：OpenAI 模型列表。
- `POST /v1/chat/completions`：仅支持非流式请求和 `response_format.type=json_object`。输入为 user 消息中的 JSON 对象，输出为可直接反序列化的 `{ 原文: 译文 }` JSON 对象。

服务仅监听 `127.0.0.1`，不会对局域网开放。模型文件不会随程序发布；请在发布目录的 `Models` 文件夹中放置模型，或在界面中选择任意本地路径。
