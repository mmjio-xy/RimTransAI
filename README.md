# RimTransAI - 边缘世界智能汉化工具

RimTransAI 是一款基于大语言模型（LLM）的 RimWorld Mod 自动化汉化工具。它能够智能扫描 Mod 结构，批量提取文本并调用 AI 进行翻译，最后生成带有原文注释的标准语言包文件。

## 本项目仍在初期开发阶段！

## 功能特性

* **多版本支持**：智能识别 Mod 结构，支持扫描根目录及 `1.4`、`1.5`、`Common` 等多版本文件夹。
* **版本筛选**：扫描后可按版本过滤内容，仅翻译特定版本的文本。
* **AI 批量翻译**：支持 OpenAI 接口格式（兼容 DeepSeek、OpenAI、Claude 等），采用 JSON 模式批量处理，保持上下文连贯。
* **智能解析**：同时支持 `DefInjected`（注入式）和 `Keyed`（键值对）两种翻译格式。
* **标准导出**：生成的 XML 文件完全符合 RimWorld 语言包规范，并自动添加 `` 注释，便于校对。
* **现代化 UI**：基于 Avalonia 和 Semi Design 构建，界面美观简洁。

## 技术栈

* **框架**：.NET 9.0
* **UI**：Avalonia UI + Semi.Avalonia
* **架构**：MVVM (CommunityToolkit.Mvvm)
* **依赖注入**：Microsoft.Extensions.DependencyInjection

## 快速开始

### 环境要求

* Windows / macOS / Linux
* [.NET 9.0 SDK](https://dotnet.microsoft.com/download/dotnet/9.0) (仅开发需要，发布版无需)

### 使用步骤

1.  **配置 API**：启动软件，在左侧面板输入支持 OpenAI 格式的 API Key。
2.  **选择 Mod**：点击“选择 Mod 文件夹”，选中包含 `Defs` 或 `Languages` 的 Mod 根目录。
3.  **筛选版本**：在下拉框中选择需要翻译的版本（如 `1.5`）。
4.  **开始翻译**：点击“开始 AI 翻译”，等待进度条完成。
5.  **保存结果**：点击“保存汉化文件”。

### 输出目录

汉化文件将自动生成在 Mod 目录下的 `Languages` 文件夹中：
```text
[ModRoot]/
  ├── 1.5/
  ├── Defs/
  └── Languages/
      └── ChineseSimplified/  <-- 生成位置
          ├── DefInjected/
          └── Keyed/