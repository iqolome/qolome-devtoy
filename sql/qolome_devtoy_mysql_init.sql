-- Qolome Devtoy MySQL 初始化 DDL
-- 包含：字典 qolome_dict_* + 网络业务 devtoy_*（与 Nets EF / AppDatabase 对齐）
-- 「数据源设置 → 生成初始化 SQL / 初始化」使用本文件；不做 IF NOT EXISTS（空库直建）。
SET NAMES utf8mb4;
SET FOREIGN_KEY_CHECKS=0;

-- ---------------------------------------------------------------------------
-- 字典（codegen / 字典管理）
-- ---------------------------------------------------------------------------
CREATE TABLE `qolome_dict_item` (
  `item_id` bigint NOT NULL COMMENT '项ID',
  `item_code` varchar(50) CHARACTER SET utf8mb4 COLLATE utf8mb4_general_ci DEFAULT NULL COMMENT '项编码',
  `item_name` varchar(100) CHARACTER SET utf8mb4 COLLATE utf8mb4_general_ci DEFAULT NULL COMMENT '项名称',
  `item_scope` int DEFAULT NULL COMMENT '项作用域',
  `item_desc` varchar(200) CHARACTER SET utf8mb4 COLLATE utf8mb4_general_ci DEFAULT NULL COMMENT '项描述',
  `enum_flag` tinyint(1) DEFAULT NULL COMMENT '枚举标志',
  `data_reference_type` int DEFAULT NULL COMMENT '数据类型',
  `remark` varchar(200) CHARACTER SET utf8mb4 COLLATE utf8mb4_general_ci DEFAULT NULL COMMENT '备注',
  `modify_version` bigint DEFAULT NULL COMMENT '修改版本',
  `deleted` tinyint(1) DEFAULT NULL COMMENT '已删除',
  `enabled` tinyint(1) DEFAULT NULL COMMENT '是否启用',
  `tenant_id` bigint DEFAULT NULL COMMENT '租户ID',
  `create_time` timestamp NULL DEFAULT NULL COMMENT '创建时间',
  `creater_id` bigint DEFAULT NULL COMMENT '创建人ID',
  `modify_time` timestamp NULL DEFAULT NULL COMMENT '修改时间',
  `modifier_id` bigint DEFAULT NULL COMMENT '修改人ID',
  PRIMARY KEY (`item_id`) USING BTREE,
  UNIQUE KEY `iteam_code` (`item_code`) USING BTREE COMMENT '编码索引'
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci COMMENT='数据字典项表';

CREATE TABLE `qolome_dict_val` (
  `val_id` bigint NOT NULL COMMENT '值ID',
  `item_id` bigint DEFAULT NULL COMMENT '域ID',
  `val_value` varchar(50) CHARACTER SET utf8mb4 COLLATE utf8mb4_general_ci DEFAULT NULL COMMENT '值编码',
  `val_code` varchar(100) CHARACTER SET utf8mb4 COLLATE utf8mb4_general_ci DEFAULT NULL COMMENT '值英文名称',
  `val_name` varchar(100) CHARACTER SET utf8mb4 COLLATE utf8mb4_general_ci DEFAULT NULL COMMENT '值中文名称',
  `val_enum` varchar(100) CHARACTER SET utf8mb4 COLLATE utf8mb4_general_ci DEFAULT NULL COMMENT '值枚举名称',
  `val_scope` int DEFAULT NULL COMMENT '值作用域',
  `val_desc` varchar(100) CHARACTER SET utf8mb4 COLLATE utf8mb4_general_ci DEFAULT NULL COMMENT '值描述',
  `seq` int DEFAULT NULL COMMENT '排序',
  `remark` varchar(255) CHARACTER SET utf8mb4 COLLATE utf8mb4_general_ci DEFAULT NULL COMMENT '备注',
  `modify_version` bigint DEFAULT NULL COMMENT '修改版本',
  `deleted` tinyint(1) DEFAULT NULL COMMENT '已删除',
  `enabled` tinyint(1) DEFAULT NULL COMMENT '是否启用',
  `tenant_id` bigint DEFAULT NULL COMMENT '租户ID',
  `create_time` timestamp NULL DEFAULT NULL COMMENT '创建时间',
  `creater_id` bigint DEFAULT NULL COMMENT '创建人ID',
  `modify_time` timestamp NULL DEFAULT NULL COMMENT '修改时间',
  `modifier_id` bigint DEFAULT NULL COMMENT '修改人ID',
  PRIMARY KEY (`val_id`) USING BTREE,
  UNIQUE KEY `iteam_id_value` (`item_id`,`val_value`) USING BTREE,
  UNIQUE KEY `iteam_id_code` (`item_id`,`val_code`) USING BTREE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci COMMENT='数据字典项值表';

-- ---------------------------------------------------------------------------
-- 网络业务（库存 / 拓扑 / OUI；列名与 EF 一致为 PascalCase）
-- ---------------------------------------------------------------------------
CREATE TABLE `devtoy_network_environment` (
  `Id` int NOT NULL AUTO_INCREMENT,
  `Name` varchar(64) NOT NULL,
  `Description` varchar(256) NULL,
  `DefaultCidr` varchar(64) NULL,
  `CreatedAtUtc` datetime(6) NOT NULL,
  `UpdatedAtUtc` datetime(6) NOT NULL,
  PRIMARY KEY (`Id`),
  UNIQUE KEY `ix_devtoy_network_environment_name` (`Name`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci COMMENT='网络环境';

CREATE TABLE `devtoy_device_type` (
  `Id` int NOT NULL AUTO_INCREMENT,
  `Code` varchar(32) NOT NULL,
  `Name` varchar(64) NOT NULL,
  `Description` varchar(256) NULL,
  `SortOrder` int NOT NULL DEFAULT 0,
  `IconFile` varchar(128) NULL,
  `IsCustomIcon` tinyint(1) NOT NULL DEFAULT 0,
  `ColorGroup` varchar(32) NULL,
  PRIMARY KEY (`Id`),
  UNIQUE KEY `ix_devtoy_device_type_code` (`Code`),
  UNIQUE KEY `ix_devtoy_device_type_name` (`Name`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci COMMENT='设备类型';

CREATE TABLE `devtoy_device_group` (
  `Id` int NOT NULL AUTO_INCREMENT,
  `NetworkEnvironmentId` int NOT NULL,
  `Name` varchar(64) NOT NULL,
  `Description` varchar(256) NULL,
  `CreatedAtUtc` datetime(6) NOT NULL,
  PRIMARY KEY (`Id`),
  UNIQUE KEY `ix_devtoy_device_group_env_name` (`NetworkEnvironmentId`, `Name`),
  KEY `ix_devtoy_device_group_env` (`NetworkEnvironmentId`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci COMMENT='设备分组';

CREATE TABLE `devtoy_device` (
  `Id` int NOT NULL AUTO_INCREMENT,
  `Code` varchar(64) NOT NULL,
  `NetworkEnvironmentId` int NOT NULL,
  `DeviceGroupId` int NULL,
  `DeviceTypeId` int NULL,
  `Hostname` varchar(255) NULL,
  `Vendor` varchar(256) NULL,
  `Brand` varchar(128) NULL,
  `Model` varchar(128) NULL,
  `Description` varchar(512) NULL,
  `Ip` varchar(64) NULL,
  `Mac` varchar(32) NULL,
  `OsType` varchar(64) NULL,
  `DeviceInfo` longtext NULL,
  `OpenPorts` longtext NULL,
  `Remarks` longtext NULL,
  `Source` varchar(16) NULL,
  `CreatedAtUtc` datetime(6) NOT NULL,
  `UpdatedAtUtc` datetime(6) NOT NULL,
  PRIMARY KEY (`Id`),
  UNIQUE KEY `ix_devtoy_device_code` (`Code`),
  UNIQUE KEY `ix_devtoy_device_environment_mac` (`NetworkEnvironmentId`, `Mac`),
  KEY `ix_devtoy_device_environment_ip` (`NetworkEnvironmentId`, `Ip`),
  KEY `ix_devtoy_device_group` (`DeviceGroupId`),
  KEY `ix_devtoy_device_type` (`DeviceTypeId`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci COMMENT='设备';

CREATE TABLE `devtoy_device_domain` (
  `Id` int NOT NULL AUTO_INCREMENT,
  `DeviceId` int NOT NULL,
  `Domain` varchar(255) NOT NULL,
  PRIMARY KEY (`Id`),
  UNIQUE KEY `ix_devtoy_device_domain_device_domain` (`DeviceId`, `Domain`),
  KEY `ix_devtoy_device_domain_device` (`DeviceId`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci COMMENT='设备域名';

CREATE TABLE `devtoy_device_nic` (
  `Id` int NOT NULL AUTO_INCREMENT,
  `DeviceId` int NOT NULL,
  `Name` varchar(64) NULL,
  `Ip` varchar(64) NULL,
  `Mac` varchar(32) NULL,
  `SortOrder` int NOT NULL DEFAULT 0,
  PRIMARY KEY (`Id`),
  KEY `ix_devtoy_device_nic_device_id` (`DeviceId`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci COMMENT='设备网卡';

CREATE TABLE `devtoy_port_purpose` (
  `Id` int NOT NULL AUTO_INCREMENT,
  `Port` int NOT NULL,
  `Protocol` varchar(8) NOT NULL,
  `Service` varchar(64) NULL,
  `Description` varchar(256) NULL,
  PRIMARY KEY (`Id`),
  UNIQUE KEY `ix_devtoy_port_purpose_port_protocol` (`Port`, `Protocol`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci COMMENT='端口用途';

CREATE TABLE `devtoy_network_topology` (
  `NetworkEnvironmentId` int NOT NULL,
  `Json` longtext NOT NULL,
  `UpdatedAtUtc` datetime(6) NOT NULL,
  PRIMARY KEY (`NetworkEnvironmentId`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci COMMENT='网络拓扑 JSON';

CREATE TABLE `devtoy_oui_entry` (
  `Id` int NOT NULL AUTO_INCREMENT,
  `Prefix` varchar(16) NOT NULL,
  `BitLength` int NOT NULL,
  `Registry` varchar(16) NOT NULL,
  `Vendor` varchar(256) NOT NULL,
  `BatchKey` varchar(64) NULL,
  PRIMARY KEY (`Id`),
  UNIQUE KEY `ix_devtoy_oui_entry_prefix` (`Prefix`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci COMMENT='OUI 厂商登记';

CREATE TABLE `devtoy_oui_meta` (
  `Id` int NOT NULL,
  `Source` varchar(32) NOT NULL,
  `BundledAtUtc` datetime NULL,
  `ImportedAtUtc` datetime NOT NULL,
  `RecordCount` int NOT NULL,
  `Note` varchar(512) NULL,
  `BatchKey` varchar(64) NULL,
  `PreviousBatchKey` varchar(64) NULL,
  `AddedCount` int NOT NULL DEFAULT 0,
  `UpdatedCount` int NOT NULL DEFAULT 0,
  `RemovedCount` int NOT NULL DEFAULT 0,
  PRIMARY KEY (`Id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci COMMENT='OUI 元数据';

-- ---------------------------------------------------------------------------
-- 应用配置 / 模块（远程落库）
-- ---------------------------------------------------------------------------
CREATE TABLE `devtoy_doc` (
  `kind` varchar(64) NOT NULL,
  `json` longtext NOT NULL,
  `updated_at` varchar(64) NOT NULL,
  PRIMARY KEY (`kind`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci COMMENT='应用文档配置';

CREATE TABLE `devtoy_workspace` (
  `path` varchar(512) NOT NULL,
  `sort_order` int NOT NULL DEFAULT 0,
  `updated_at` varchar(64) NOT NULL,
  PRIMARY KEY (`path`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci COMMENT='工作区根路径';

CREATE TABLE `devtoy_module` (
  `path` varchar(512) NOT NULL,
  `workspace_path` varchar(512) NULL,
  `source` varchar(32) NOT NULL,
  `display_name` varchar(256) NULL,
  `ignored` tinyint(1) NOT NULL DEFAULT 0,
  `java` tinyint(1) NULL,
  `web` tinyint(1) NULL,
  `flutter` tinyint(1) NULL,
  `version` varchar(64) NULL,
  `manual` tinyint(1) NULL,
  `codegen_model_name` varchar(128) NULL,
  `codegen_dao` tinyint(1) NULL,
  `codegen_service` tinyint(1) NULL,
  `codegen_controller` tinyint(1) NULL,
  `enable_codegen` tinyint(1) NULL,
  `enable_enum` tinyint(1) NULL,
  `enable_ts_dict` tinyint(1) NULL,
  `tag_boot_starter` tinyint(1) NULL,
  `enum_package_path` varchar(256) NULL,
  `ts_dict_path` varchar(512) NULL,
  `create_time` varchar(64) NOT NULL,
  `modify_time` varchar(64) NOT NULL,
  `updated_at` varchar(64) NOT NULL,
  PRIMARY KEY (`path`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci COMMENT='模块列表';

SET FOREIGN_KEY_CHECKS=1;
