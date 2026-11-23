# 代码生成默认模板

来源：`qolome-codegen-core/src/main/resources/templates`（Velocity `.vm`），以及本仓补充的实体基类模板。

| 模板 | 说明 |
| --- | --- |
| `entity.java.vm` 等 | 表 / Dao / Service / Controller / DTO 生成 |
| `BaseModel.java.vm` | 实体基类 `com.qolome.starter.dao.model.BaseModel`（模板管理可编辑） |
| `enum.java.vm` / `dict.ts.vm` | 字典枚举与 TS |

应用首次启动时会复制到本机：

`%LocalAppData%\Qolome\Devtoy\templates`

在「配置管理 → 模板管理」中编辑；「还原」会用本目录覆盖用户副本。新建的 `BaseModel.java.vm` 若本机已有模板目录，需点一次「刷新」或删除对应用户文件后由 EnsureSeeded 再复制。
