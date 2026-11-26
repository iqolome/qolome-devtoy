# Qolome DevToy

[![License](https://img.shields.io/badge/license-Apache--2.0-green.svg)](LICENSE)
[![.NET](https://img.shields.io/badge/.NET-10-512BD4.svg)](https://dotnet.microsoft.com/)
[![WinUI](https://img.shields.io/badge/UI-WinUI%203-0078D4.svg)](https://learn.microsoft.com/windows/apps/winui/)
[![Version](https://img.shields.io/badge/version-1.1.1-blue.svg)](src/Qolome.Devtoy.App/Qolome.Devtoy.App.csproj)

独立 Windows 开发者工具（**WinUI 3 / .NET 10**），对齐 `cloud-workspace/qolome-codegen` IDEA 插件能力，并内嵌局域网扫描 / 资产库存（代码自 `qolome-nets` 复制，两仓独立演进）。目标：**更独立、更现代 UI、更好性能**（纯 C# 代码生成，不依赖 JVM）。

## 代码质量（可选）

在自建 SonarQube 上扫描时，配置环境变量 `SONAR_HOST_URL`、`SONAR_TOKEN`（可选 `SONAR_SCANNER_HOME`），**在本仓库根目录**执行：

```powershell
.\scripts\sonar-scan.ps1
```

| 说明 | |
| --- | --- |
| 工程配置 | [`sonar-project.properties`](sonar-project.properties) |
| 默认流程 | SonarScanner for .NET：`begin` → 编译 `Qolome.Devtoy.App.csproj`（`Debug\|x64` + `win-x64`）→ `end` |
| 为何不编 `.slnx` | 当前 `Qolome.Devtoy.slnx` 无 `Debug\|x64` 解决方案配置，会报 `MSB4126` |
| CLI 回退 | `.\scripts\sonar-scan.ps1 -ScannerCli`（依赖 `SONAR_SCANNER_HOME`） |
| 失败残留 | `Remove-Item -Recurse -Force .\.sonarqube` 后重跑 |

勿将 Sonar 分析 Token 或徽章 Token 写入仓库。

## 关于

| | |
| --- | --- |
| **产品** | Qolome DevToy |
| **开发商** | Qolome |
| **官网** | [https://www.qolome.com](https://www.qolome.com) |
| **许可协议** | [Apache License 2.0](LICENSE) |
| **版本** | 以 `src/Qolome.Devtoy.App/Qolome.Devtoy.App.csproj` 的 `<Version>` 为准（当前 `1.1.1`） |

应用内 **关于** 页可查看版本、服务协议与链接。

## 环境要求

| 工具 | 说明 |
| --- | --- |
| OS | Windows 10 1809+ / Windows 11（x64） |
| [.NET SDK 10](https://dotnet.microsoft.com/download) | 与 `Qolome.Devtoy.*` 的 `net10.0` 一致 |
| 数据库 | **本机 SQLite** 仅存应用配置；**远程库**（数据源设置）为唯一业务库（网络日常读写当前重点支持 MySQL） |
| Inno Setup 6 | 可选，制作安装包 |
| 企业代码签名 PFX | 可选，见 [packaging/README.md](packaging/README.md) |
| SonarScanner | 可选；全局工具 `dotnet-sonarscanner`，或 `SONAR_SCANNER_HOME` |

### 内置数据库依赖版本

数据源 `DatabaseKind`：MySQL / PostgreSQL / Oracle / SQLite / **Odbc**。统一经 `DbConnectionFactory` 打开 ADO.NET 连接；ODBC 使用本机已安装的 ODBC 驱动（DSN 或 `Driver={...}` 连接串）。

| 数据库 | 本工具 .NET 包 | 版本 | 默认端口 | 状态 |
| --- | --- | --- | --- | --- |
| MySQL | [MySqlConnector](https://www.nuget.org/packages/MySqlConnector) | `3.0.0` | `3306` | 测试连接、字典、表列表、代码生成、初始化 SQL；**网络业务读写** |
| PostgreSQL | [Npgsql](https://www.nuget.org/packages/Npgsql) | `9.0.3` | `5432` | 测试连接、表列表、表结构读取 |
| Oracle | [Oracle.ManagedDataAccess.Core](https://www.nuget.org/packages/Oracle.ManagedDataAccess.Core) | `23.7.0` | `1521` | 测试连接、表列表、表结构读取 |
| SQLite | [Microsoft.Data.Sqlite](https://www.nuget.org/packages/Microsoft.Data.Sqlite) | `9.0.0` | — | **本机配置库**；亦可作数据源测连 / 列库表 |
| ODBC | [System.Data.Odbc](https://www.nuget.org/packages/System.Data.Odbc) | `9.0.0` | — | 测试连接；表列表走 `information_schema`（依赖目标驱动） |

对齐参考：`qolome-codegen` IDEA 插件 JDBC（`build.gradle.kts`）：`mysql-connector-j:9.1.0` / `postgresql:42.7.4` / `ojdbc11:23.6.0.24.10` / `sqlite-jdbc:3.47.1.0`。

说明：字典 CRUD 与「初始化」DDL 以 MySQL 方言为主，表名与业务库 / Java codegen 一致为 `qolome_dict_*`；网络库存、拓扑、OUI 等业务数据一律读写 **数据源设置** 中的远程库，**不再**做本地 ↔ 远程整库同步。

## 数据与表约定

| 项 | 约定 |
| --- | --- |
| 本机库文件 | `%LocalAppData%\Qolome\Devtoy\qolome-devtoy.db` |
| 本机配置 | `devtoy_doc`（应用 / codegen / 数据源 JSON）；`devtoy_workspace`（工作区根）；`devtoy_module`（忽略、指定工程、覆盖项、创建/修改时间） |
| 业务库 | 「配置管理 → 数据源设置」配置的远程库；网络库存、拓扑、OUI、字典、代码生成均使用该库 |
| 字典表 | `qolome_dict_item` / `qolome_dict_val`（对齐 `qolome-dict` / Java codegen，**无** `devtoy_` 前缀） |
| 网络业务表 | 远程库中的 `devtoy_device*`、拓扑、OUI 等（由网络模块 Schema 维护） |
| 导入导出 | **仅**本机配置 SQL（`devtoy_doc` / `devtoy_workspace` / `devtoy_module`），不含远程业务表 |
| 与独立 Q网络 | **不**共用 `%LocalAppData%\qolome\qolome-nets`；`qolome-nets` 仓代码独立演进 |

模板文件仍落在 `%LocalAppData%\Qolome\Devtoy\templates\`（非数据库）。

```text
本机 SQLite                         远程业务库（数据源设置）
├─ devtoy_doc                       ├─ qolome_dict_*
├─ devtoy_workspace                 ├─ 网络库存 / 拓扑 / OUI …
└─ devtoy_module                    └─ （代码生成读表结构）
```

## 仓库结构

| 路径 | 说明 |
| --- | --- |
| `src/Qolome.Devtoy.App` | WinUI 3 壳层：导航、页面、主题 / 图标、控制台、打包入口 |
| `src/Qolome.Devtoy.App/Nets/` | 网络工具 UI（自 `qolome-nets` 复制，命名空间 `Qolome.Nets.App`）及 DeviceTypes / Oui / Topology 资源 |
| `src/Qolome.Devtoy.App/Assets/Fonts/` | **Fluent UI System Icons** 字体（`Regular` / `Filled`），侧栏等 `FontIcon` 使用 |
| `src/Qolome.Devtoy.Core` | 业务核心：模块扫描、本机配置（SQLite）、ADO 数据源、字典、`CodegenEngine`（纯 C#） |
| `src/Qolome.Nets.Core` | 网络扫描 / 库存 / 拓扑；本机网卡与硬件采集（`LocalNicInventory` / `LocalHardwareInventory` / 出口网络查询；读写远程业务库；自 `qolome-nets` 复制并适配） |
| `templates/` | 默认 Velocity 模板（`.vm`），可复制到本机后在「模板管理」编辑 |
| `sql/` | 初始化 DDL（如 `qolome_devtoy_mysql_init.sql`，字典表为 `qolome_dict_*`） |
| `icons/` | 应用图标 variant；`icons/svg/` 备用 SVG 资源 |
| `docs/segoe-fluent-icons.md` | Segoe Fluent Icons 离线清单（与应用内 **图标库** 同源） |
| `packaging/` | `package.ps1`、MSIX / ZIP / Inno 与签名脚本 |
| `scripts/sonar-scan.ps1` | 本地 Sonar 扫描 |
| `config/` | 示例配置（如 `signing.example.properties`，勿提交真实密钥） |
| `%LocalAppData%/Qolome/Devtoy/` | 运行时：`datasource.dat`（加密连接）、`devtoy.db`（Sqlite 模式业务库）、模板、崩溃日志 |
| `sonar-project.properties` | SonarQube 工程配置 |
| `dist/release/` | **发布制品**（ZIP / MSIX / Setup；`dist/publish` 为中间产物，已在 `.gitignore` 中忽略） |

解决方案：[`Qolome.Devtoy.slnx`](Qolome.Devtoy.slnx)（IDE 打开用；命令行构建 / Sonar 请编 App 工程）。

## 功能概览

侧栏分组：**开发工具** → **网络工具** → **配置管理** → **关于**。界面主题统一在 **配置管理 → 主题设置** 维护。

| 分组 | 导航 | 能力 |
| --- | --- | --- |
| 开发工具 | **Java代码生成** | 可搜索 Java **输出项目**；填写 / 保存 **模块名** 与 Dao·Service·Controller **习惯**；查询库表（含表注释）、勾选后生成；生成中按钮 loading 禁点，完成后弹框提示 |
| 开发工具 | **字典管理** | 读写远程 `qolome_dict_*`、按编码/名称查询、增删改、排序、逻辑删除（**二次确认**，删除钮为红色）、字典值维护；可生成 Java 枚举 / TS 字典 |
| 开发工具 | **项目管理** | **多工作区叠加**扫描 + 指定工程；类型 **Java / Web / Flutter**；标签；忽略列表；列排序（含创建/修改时间）；名称 / 类型 / 标签筛选 |
| 开发工具 | **图标库** | 浏览 Segoe Fluent Icons；可筛 **仅 Symbol 枚举**；优先复制 `Symbol.Xxx`，否则 `\uXXXX` |
| 配置管理 | **主题设置** | **界面主题**（跟随系统 / 浅色 / 深色）；**图标主题**；**性能**（硬件加速 / GPU 合成、采集并行、表格富视觉） |
| 配置管理 | **代码生成配置** | 可折叠分栏：基本信息 / Enum / Dao / TypeScript；Dao 含 `entitySuperClass`、包名与审计字段、`IdType` |
| 配置管理 | **数据源设置** | 多库种、测试连接；**生成初始化 SQL** / **初始化**；保存后准备远程业务库 Schema |
| 配置管理 | **模板管理** | 从仓库默认 `.vm` 复制到本机，可编辑 / 还原 |
| 配置管理 | **导入导出** | 本机配置 SQL（`devtoy_doc` / `devtoy_workspace` / `devtoy_module`）导入 / 导出 |
| 网络工具 | **本机信息** | **硬件信息**（设备 / CPU / 内存 / OS / 硬盘类型 / 显卡与显存）；**网络信息**（出口 IPv4/IPv6、ASN、ISP、位置）；**网卡信息**（连接名 / SSID、类型、速率、IP、网关、MAC；启用 / 禁用 / 删除软网卡） |
| 网络工具 | **扫描** | 本机网卡 / CIDR / IP 段主机发现、TCP·UDP 端口、ICMP；勾选写入远程库存 |
| 网络工具 | **设备列表** | 按网络环境管理设备、分组、刷新、Excel |
| 网络工具 | **网络拓扑** | 按环境 Canvas 拓扑（读写远程库） |
| 网络工具 | **网络环境** / **设备类型** | 环境、分组、设备类型与图标 |
| 网络工具 | **网络设置** | 扫描偏好、OUI 更新 |
| — | **关于** | 产品、版本、服务协议、官网、许可 |

生成逻辑在 Core 的 `CodegenEngine` + `Codegen/*Generator` 中实现（对照 `qolome-codegen`，NVelocity 渲染同款 `.vm`）。

### 界面与图标约定

- 按钮 **图标 + 文字** 同字号；列表 **表头冻结**、列宽可拖；支持点击列头排序。
- 表格 / 列表统一 **边框、网格线、斑马纹、悬停与选中**（`DevtoyTableListViewStyle` + `TableListChrome`）；表头放在 `ListView.Header` 内与行对齐。
- **本机信息** 等长页使用外层 `ScrollViewer`：滚轮上下滚动页面；横向溢出用滚动条或 Shift+滚轮。
- 底部 **控制台**：标题栏为 **图标 +「控制台」** 一体按钮；隐藏时 `HideBcc`（点开）、显示时 `ShowBcc`（点关）。
- 浅色主题下自定义标题栏系统按钮配色。
- 删除类 `ContentDialog` 主按钮文案为 **删除**，使用红色危险样式（`DevtoyDangerButtonStyle`）。
- **侧栏图标** 优先 [Fluent UI System Icons](https://github.com/microsoft/fluentui-system-icons) 字体（`Assets/Fonts/FluentSystemIcons-*.ttf`，资源键 `FluentSystemIconsRegular` / `FluentSystemIconsFilled`），字号资源 `DevtoyFluentIconFontSize`（默认 18，略大于正文以补偿视觉偏小）。码位可从 [fluenticons.co](https://fluenticons.co/) 的 **WinUI / Icon font** 片段复制，例如：

```xml
<FontIcon FontFamily="{StaticResource FluentSystemIconsRegular}"
          Glyph="&#xF0177;"
          FontSize="{StaticResource DevtoyFluentIconFontSize}" />
```

- 系统常用操作仍可用 [Segoe Fluent Icons](https://learn.microsoft.com/zh-cn/windows/apps/design/style/segoe-fluent-icons-font)（`SymbolIcon` / `FontIcon`）；离线清单见 [docs/segoe-fluent-icons.md](docs/segoe-fluent-icons.md)，应用内 **图标库** 可检索与复制。
- 应用窗口图标主题见 [icons/README.md](icons/README.md)。

### 代码生成配置说明

持久化于本机 SQLite `devtoy_doc`（库文件见上文），生成时写入实体注解与包路径。作者有邮箱时，注释自动为 `<a href="mailto:邮箱">作者</a>`。

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

项目级覆盖：在 **项目管理** 启用功能后，可编辑 Java 生成配置 / 枚举路径 / 字典路径；生成页仅列出已启用「代码生成」的 Java 项目；字典生成可多选已启用枚举/字典的项目。模块偏好落在本机 `devtoy_module`。

### 模块扫描约定

- 工作区可 **叠加多个根目录**（`devtoy_workspace`），扫描结果合并去重。
- 带 `packaging=pom` 的 Maven 聚合模块本身不作为生成目标，但仍会下钻子模块。
- Gradle 工程按插件识别；无源码的包装工程会跳过。
- **忽略** 的模块仍出现在「忽略列表」，但不出现在代码生成 / 字典生成的目标下拉中。
- 列表支持按名称、工作区、路径、类型、版本、创建/修改时间排序。

## 常用命令

```powershell
# 在仓库根目录执行

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
| `%LocalAppData%\Qolome\Devtoy\qolome-devtoy.db` | 本机配置库（`devtoy_doc` / `devtoy_workspace` / `devtoy_module`） |
| `%LocalAppData%\Qolome\Devtoy\crash.log` | 崩溃日志 |
| `%LocalAppData%\Qolome\Devtoy\templates\` | 「模板管理」落地的可编辑 `.vm` |

首次启动会尝试将旧版 `settings.json` / `module-overrides.json` 以及 `devtoy_doc` 内嵌的模块字段迁入 `devtoy_workspace` / `devtoy_module`。

字典 / 表生成：在 **数据源设置** 中配置远程业务库（通常为 MySQL）。空库可用 **初始化** / **生成初始化 SQL** 写入 `qolome_dict_*` 与网络业务表 `devtoy_*`（环境、设备、网卡、拓扑、OUI 等）；已有业务库可直接使用现有表。参考 DDL：[`sql/qolome_devtoy_mysql_init.sql`](sql/qolome_devtoy_mysql_init.sql)。

网络资产：配置并保存数据源后，由应用准备远程 Schema；扫描 / 设备 / 拓扑 / OUI 均读写该远程库。本机配置备份请用 **导入导出**。

## 打包发布

详见 **[packaging/README.md](packaging/README.md)**。图标主题说明见 [icons/README.md](icons/README.md)。

| 产物（均在 `dist/release/`） | 说明 |
| --- | --- |
| 便携 ZIP | 解压即用 |
| MSIX | 可侧载安装 / 卸载 |
| Inno Setup | 安装向导 + 卸载项 |

**带企业签名的正式发布**在 [GitHub Actions](.github/workflows/release.yml) 中完成（`windows-latest` 免费额度）：推送标签 `v*` 或手动 **Release** 工作流。PFX 仅存于仓库 **Secrets**，不入库。说明见 [docs/github-actions.md](docs/github-actions.md)。本地日常请用 `.\packaging\package.ps1 -SkipSigning`；需本地验签时设置 `QOLOME_SIGNING_PROPERTIES`（见 `config/signing.example.properties`）。

## 关联项目

| 项目 | 关系 |
| --- | --- |
| `qolome-codegen` | 代码生成逻辑与 `.vm` 模板对齐参考；字典表 `qolome_dict_*` |
| `qolome-nets` | 局域网功能源码来源（本仓为内嵌副本，与独立安装版 **数据目录分离**） |
| `qolome-webfx-dict` | TypeScript 字典消费方示例 |

## 贡献与版本控制

- 勿提交 `bin/`、`obj/`、`dist/`、`.sonarqube/`、`*.pfx`、`signing.properties`；规则见根目录 **[.gitignore](.gitignore)**
- 勿把 `SONAR_TOKEN`、签名口令、PFX 等凭据写入仓库
- 版本号变更请用脚本一键同步（**GitHub / GitLab 同一脚本**；csproj / MSIX / Sonar / README / Inno 等，**不含** NuGet 依赖版本；缺文件自动跳过）：
  ```powershell
  .\scripts\bump-version.ps1 -Bump patch          # 或 -Bump minor / major
  .\scripts\bump-version.ps1 -Version 1.1.1       # 或指定版本（不要写 v）
  .\scripts\bump-version.ps1 -Bump patch -DryRun  # 只预览
  ```
