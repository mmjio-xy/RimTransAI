# RimTransAI - 边缘世界智能汉化工具

> RimWorld Mod 智能翻译辅助工具

RimTransAI 是一款专为 RimWorld 游戏 Mod 开发者和汉化人员设计的桌面应用程序。它通过分析 Mod 的程序集（DLL）和 XML 定义文件，自动提取需要翻译的文本，并借助 LLM（大语言模型）API 实现批量智能翻译，最终生成符合 RimWorld 规范的语言文件。

## 功能特性

- **多版本支持**：智能识别 Mod 结构，支持扫描根目录及 `1.4`、`1.5`、`Common` 等多版本文件夹
- **版本筛选**：扫描后可按版本过滤内容，仅翻译特定版本的文本
- **AI 批量翻译**：支持 OpenAI 接口格式（兼容 DeepSeek、OpenAI、Claude 等），采用 JSON 模式批量处理
- **多线程翻译**：支持多线程并发翻译，大幅提升翻译效率
- **自定义提示词**：支持预设模板选择和自定义提示词编辑，灵活控制翻译风格
- **智能解析**：同时支持 `DefInjected`（注入式）和 `Keyed`（键值对）两种翻译格式
- **备份与恢复**：自动备份翻译文件，支持备份管理、搜索筛选、排序和一键恢复
- **标准导出**：生成的 XML 文件完全符合 RimWorld 语言包规范
- **现代化 UI**：基于 Avalonia 和 Semi Design 构建，界面美观简洁

## 快速开始

### 环境要求

- Windows / macOS / Linux
- [.NET 9.0 SDK](https://dotnet.microsoft.com/download/dotnet/9.0) (仅开发需要，发布版无需)

### 使用步骤

1. **配置 API**：启动软件，在左侧面板输入支持 OpenAI 格式的 API Key
2. **选择 Mod**：点击"选择 Mod 文件夹"，选中包含 `Defs` 或 `Languages` 的 Mod 根目录
3. **筛选版本**：在下拉框中选择需要翻译的版本（如 `1.5`）
4. **开始翻译**：点击"开始 AI 翻译"，等待进度条完成
5. **保存结果**：点击"保存汉化文件"

### 输出目录

汉化文件将自动生成在 Mod 目录下的 `Languages` 文件夹中：

```text
[ModRoot]/Languages/ChineseSimplified/
├── DefInjected/
└── Keyed/
```

## 配置说明

应用配置存储在 `settings.json`（与 EXE 同目录）：

| 配置项 | 说明 |
|--------|------|
| `ApiUrl` | LLM API 端点 |
| `ApiKey` | API 密钥 |
| `TargetModel` | 目标模型名称 |
| `TargetLanguage` | 目标语言（ChineseSimplified/ChineseTraditional） |
| `EnableMultiThreadTranslation` | 是否启用多线程翻译 |
| `MaxThreads` | 最大线程数 |
| `EnableAutoBackup` | 是否启用自动备份 |
| `MaxBackupCount` | 每个 Mod 版本最大备份数量 |

## 开发

```bash
# 构建
dotnet build

# 运行
dotnet run --project RimTransAI

# 测试
dotnet test

# 发布
dotnet publish RimTransAI -c Release -r win-x64
```

详细开发文档请参阅 [CLAUDE.md](./CLAUDE.md)。

## 变更记录

| 日期 | 版本 | 变更内容 |
|------|------|----------|
| 2026-01-23 | v1.4 | 备份管理器新增搜索筛选和排序功能 |
| 2026-01-22 | v1.3 | 实现翻译文件备份与恢复功能 |
| 2026-01-21 | v1.2.1 | 添加多线程翻译功能 |
| 2026-01-19 | v1.2 | 添加自定义提示词功能 |
| 2026-01-16 | v1.0 | 项目初始版本 |

## 许可证

本项目采用 [GNU General Public License v3.0](LICENSE) 开源许可证。

## 贡献

欢迎提交 Issue 和 Pull Request！

## 致谢

- [RimWorld](https://rimworldgame.com/) - Ludeon Studios
- [Avalonia UI](https://avaloniaui.net/) - 跨平台 UI 框架
- [Semi Design](https://semi.design/) - 设计系统
