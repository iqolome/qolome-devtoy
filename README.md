# Qolome Devtoy

[![License](https://img.shields.io/badge/license-Apache--2.0-green.svg)](LICENSE)
[![.NET](https://img.shields.io/badge/.NET-10-512BD4.svg)](https://dotnet.microsoft.com/)
[![WinUI](https://img.shields.io/badge/UI-WinUI%203-0078D4.svg)](https://learn.microsoft.com/windows/apps/winui/)
[![Version](https://img.shields.io/badge/version-1.0.0-blue.svg)](src/Qolome.Devtoy.App/Qolome.Devtoy.App.csproj)

独立 Windows 开发者工具（**WinUI 3 / .NET 10**），对齐 `cloud-workspace/qolome-codegen` IDEA 插件能力，目标：**更独立、更现代 UI、更好性能**（纯 C# 代码生成，不依赖 JVM）。

## 关于

| | |
| --- | --- |
| **产品** | Qolome Devtoy |
| **开发商** | 忻州市尖锋达科技有限公司（Xinzhou jfonda Technology Co., Ltd.） |
| **官网** | [https://www.jfonda.com/](https://www.jfonda.com/) |
| **许可协议** | [Apache License 2.0](LICENSE) |
| **版本** | 以 `src/Qolome.Devtoy.App/Qolome.Devtoy.App.csproj` 的 `<Version>` 为准（当前 `1.0.0`） |

应用内 **关于** 页可查看版本与链接。

## Sonar检测信息

- 工程 key：`webfx:qolome-devtoy`（[`sonar-project.properties`](sonar-project.properties)）
- 仪表盘：[https://sonar.qolome.com:8081/dashboard?id=webfx%3Aqolome-devtoy](https://sonar.qolome.com:8081/dashboard?id=webfx%3Aqolome-devtoy)

本地扫描需已配置环境变量 `SONAR_HOST_URL`、`SONAR_TOKEN`（可选 `SONAR_SCANNER_HOME`）。**务必在本仓库根目录执行**（不要在 `webfx-workspace` 根目录跑）：

```powershell
cd E:\workspace\webfx-workspace\qolome-devtoy
.\scripts\sonar-scan.ps1
```

| 说明 | |
| --- | --- |
| 默认流程 | SonarScanner for .NET：`begin` → 编译 `Qolome.Devtoy.App.csproj`（`Debug\|x64` + `win-x64`，连带 Core）→ `end` |
| 为何不编 `.slnx` | 当前 `Qolome.Devtoy.slnx` 无 `Debug\|x64` 解决方案配置，会报 `MSB4126` |
| CLI 回退 | `.\scripts\sonar-scan.ps1 -ScannerCli`（依赖 `SONAR_SCANNER_HOME`；C# 分析可能不完整） |
| 失败残留 | `Remove-Item -Recurse -Force .\.sonarqube` 后重跑 |
| 徽章 | Quality Gate 徽章需工程徽章 token（`sqb_…`）；具备权限后可用 `qolome-cli` 写入本小节 |

## 环境要求

| 工具 | 说明 |
| --- | --- |
| OS | Windows 10 1809+ / Windows 11（x64） |
| [.NET SDK 10](https://dotnet.microsoft.com/download) | 与 `Qolome.Devtoy.*` 的 `net10.0` 一致 |
| MySQL | 字典 / 表生成需可连通的开发库（见 **数据源设置**） |
| Inno Setup 6 | 可选，制作安装包 |
| 企业代码签名 PFX | 可选，见 [packaging/README.md](packaging/README.md) |
| SonarScanner | 可选；全局工具 `dotnet-sonarscanner`，或 `SONAR_SCANNER_HOME` |

## 仓库结构

| 路径 | 说明 |
| --- | --- |
| `src/Qolome.Devtoy.App` | WinUI 3 壳层：导航、页面、主题 / 图标、控制台、打包入口 |
| `src/Qolome.Devtoy.Core` | 业务核心：模块扫描、配置、JDBC、字典、`CodegenEngine`（纯 C#） |
| `templates/` | 默认 Velocity 模板（`.vm`），可复制到本机后在「模板管理」编辑 |
| `sql/` | 初始化 DDL 等（如 `qolome_devtoy_mysql_init.sql`） |
| `icons/` | 多主题应用图标 variant（复制到输出目录） |
| `packaging/` | `package.ps1`、MSIX / ZIP / Inno 与签名脚本 |
| `scripts/sonar-scan.ps1` | 本地 Sonar 扫描 |
| `config/` | 示例配置（如 `signing.example.properties`，勿提交真实密钥） |
| `sonar-project.properties` | SonarQube 工程配置 |
| `dist/` | **本地打包输出**（已在 `.gitignore` 中忽略） |

解决方案：[`Qolome.Devtoy.slnx`](Qolome.Devtoy.slnx)（IDE 打开用；命令行构建 / Sonar 请编 App 工程）。

## 功能概览

| 导航 | 能力 |
| --- | --- |
| **Java代码生成** | 可搜索 Java 目标模块；填写 / 保存 **模块名** 与 Dao·Service·Controller **习惯**；查询库表（含表注释）、勾选后生成；生成中按钮 loading 禁点，完成后弹框提示 |
| **字典管理** | 加载 `qolome_dict_*`、按编码/名称查询、增删改、排序、逻辑删除（二次确认）、字典值维护；可生成 Java 枚举 / TS 字典 |
| **模块管理** | **多工作区叠加**扫描 + 指定工程；类型 **Java / Web / Flutter**；忽略列表（仍显示但不参与生成）；**生成配置**（模块名 + 勾选习惯）；查询有生成配置的模块；双击路径打开资源管理器 |
| **配置 · 主题设置** | **界面主题**（跟随系统 / 浅色 / 深色）；**图标主题**（跟随系统时锁定，随系统明暗切换） |
| **配置 · 代码生成配置** | 作者 / 邮箱、`basePackage`、`enumPackagePath`、逻辑删除 / 多租户 / 审计字段、主键 `IdType`（见下表） |
| **配置 · 数据源** | JDBC URL / 分项、MySQL 测试连接；**生成初始化 SQL** / **初始化**（表已存在则报错中止） |
| **配置 · 模板管理** | 从仓库默认 `.vm` 复制到本机，可编辑 / 还原 |
| **配置 · 导入导出** | 全局配置导入导出 |
| **关于** | 产品、版本、官网、许可 |

界面约定：按钮 **图标 + 文字** 同字号；列表 **表头冻结**、列宽可拖；底部控制台可显隐；浅色主题下自定义标题栏系统按钮配色。

生成逻辑在 Core 的 `CodegenEngine` + `Codegen/*Generator` 中实现（对照 `qolome-codegen`，NVelocity 渲染同款 `.vm`）。

### 代码生成配置说明

持久化于 `%LocalAppData%\Qolome\Devtoy\settings.json`，生成时写入实体注解与包路径。作者有邮箱时，注释自动为 `<a href="mailto:邮箱">作者</a>`。

| 项 | 默认 | 说明 |
| --- | --- | --- |
| 作者 / 邮箱 | `Qolome` / `qolome@outlook.com` | 写入 `@author` 注释 |
| `basePackage` | `com.qolome.starter` | Java 根包；与模块名拼接为业务包 |
| `enumPackagePath` | `com.qolome.common.dict.biz.enums` | 业务枚举包 |
| 逻辑删除字段 | `deleted` | `@TableLogic` |
| 多租户字段 | `tenant_id` | `FieldFill.INSERT` |
| 创建日期 / 创建人 | `create_time` / `creater_id` | `FieldFill.INSERT` |
| 修改日期 / 修改人 | `modify_time` / `modifier_id` | `FieldFill.INSERT_UPDATE` |
| 主键生成类型 | `ASSIGN_ID` | 对齐 MyBatis-Plus `IdType`：`AUTO` / `NONE` / `INPUT` / `ASSIGN_ID` / `ASSIGN_UUID` |

模块级覆盖：在 **模块管理 → 生成配置** 中为单个 Java 模块设置模块名与 Dao/Service/Controller 勾选习惯；生成页可 **保存习惯** 写回。

### 模块扫描约定

- 工作区可 **叠加多个根目录**，扫描结果合并去重。
- 带 `packaging=pom` 的 Maven 聚合模块本身不作为生成目标，但仍会下钻子模块。
- Gradle 工程按插件识别；无源码的包装工程会跳过。
- **忽略** 的模块仍出现在「忽略列表」，但不出现在代码生成 / 字典生成的目标下拉中。

## 常用命令

```powershell
cd E:\workspace\webfx-workspace\qolome-devtoy

# 编译（与 Sonar 脚本一致）
dotnet build src\Qolome.Devtoy.App\Qolome.Devtoy.App.csproj -c Debug -p:Platform=x64 -r win-x64

# 运行
dotnet run --project src\Qolome.Devtoy.App\Qolome.Devtoy.App.csproj -p:WinAppRunDevTools=off

# Sonar
.\scripts\sonar-scan.ps1

# 打包（ZIP / MSIX / Inno）
.\packaging\package.ps1
.\packaging\package.ps1 -Target Zip
.\packaging\package.ps1 -RequireSigning
```

也可打开 `Qolome.Devtoy.slnx` 后在 Visual Studio / Rider 中调试。

## 开发与运行

| 路径 | 用途 |
| --- | --- |
| `%LocalAppData%\Qolome\Devtoy\settings.json` | 用户配置（主题、数据源、代码生成、模块目录等） |
| `%LocalAppData%\Qolome\Devtoy\crash.log` | 崩溃日志 |
| `%LocalAppData%\Qolome\Devtoy\` 下模板目录 | 「模板管理」落地的可编辑 `.vm` |

字典 / 表生成前请先在 **数据源设置** 中配置可连通的 MySQL（例如 `qolome-epsims` 开发库），可用 **初始化** 写入字典相关表结构。仓库内参考 DDL：[`sql/qolome_devtoy_mysql_init.sql`](sql/qolome_devtoy_mysql_init.sql)。

## 打包发布

详见 **[packaging/README.md](packaging/README.md)**。图标主题说明见 [icons/README.md](icons/README.md)。

| 产物 | 说明 |
| --- | --- |
| 便携 ZIP | 解压即用 |
| MSIX | 可侧载安装 / 卸载 |
| Inno Setup | 安装向导 + 卸载项 |

签名与 `qolome_epsims_app` 共用企业 PFX 时使用 `-RequireSigning`。

## 关联仓库

| 仓库 | 路径（本机示例） | 关系 |
| --- | --- | --- |
| 后端 / codegen 参考 | `E:\workspace\cloud-workspace`（`qolome-codegen`） | 生成逻辑与 `.vm` 对齐来源 |
| Web / 字典包 | `E:\workspace\webfx-workspace`（如 `qolome-webfx-dict`） | TS 字典消费方 |
| 本工具 | `E:\workspace\webfx-workspace\qolome-devtoy` | 当前仓库 |

跨仓联调上下文见 webfx 仓 `.cursor/qolome-sibling-workspaces.md`（由 `qolome-cli scan` 维护）。

## 贡献与版本控制

- 勿提交 `bin/`、`obj/`、`dist/`、`.sonarqube/`、`*.pfx`、`signing.properties`；规则见根目录 **[.gitignore](.gitignore)**
- 勿把 `SONAR_TOKEN`、签名口令等写入仓库或 README（徽章用的 `sqb_…` 可公开）
- 版本号变更时请同步：
  - `src/Qolome.Devtoy.App/Qolome.Devtoy.App.csproj` 的 `<Version>`
  - `sonar-project.properties` 的 `sonar.projectVersion`
  - 本 README 顶部 Version 徽章
