-- Qolome Devtoy MySQL 初始化 DDL（字典模块）
-- 来源对齐：qolome_dict_schema_full_V1.0.0
SET NAMES utf8mb4;
SET FOREIGN_KEY_CHECKS=0;

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

SET FOREIGN_KEY_CHECKS=1;
