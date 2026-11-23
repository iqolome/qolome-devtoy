namespace Qolome.Devtoy.Core.Codegen;

/// <summary>对齐 Java CodegenSettings 的运行参数。</summary>
public sealed class CodegenSettings
{
    public string Copyright { get; set; } =
        $"copyrightCopyright (c) 2016 - {DateTime.Now.Year} qolome.com Inc. All Rights Reserved.";

    public string DatabaseType { get; set; } = "MYSQL";

    public string VersionColumnName { get; set; } = "modify_version";

    public string LogicDeleteColumnName { get; set; } = "deleted";

    public string TenantIdColumnName { get; set; } = "tenant_id";

    public string CreateTimeColumnName { get; set; } = "create_time";

    public string CreaterIdColumnName { get; set; } = "creater_id";

    public string ModifyTimeColumnName { get; set; } = "modify_time";

    public string ModifierIdColumnName { get; set; } = "modifier_id";

    public string IdType { get; set; } = "ASSIGN_ID";

    public string MapperAnnotation { get; set; } = "org.apache.ibatis.annotations.Mapper";

    public string TsDictPath { get; set; } = Path.Combine("src", "dict");

    public string EnumPackage { get; set; } = "com.qolome.common.dict.biz.enums";

    public string EnumNameSuffix { get; set; } = "Enum";

    public string EnumInterface { get; set; } = "com.qolome.common.dict.biz.inft.IBizEnum";

    public string EnumTemplateName { get; set; } = "enum.java";

    public string TsDictTemplateName { get; set; } = "dict.ts";

    public string EntityDictCommentTemplateName { get; set; } = "entity-comment.java";

    public string DictCodeExtractRegular { get; set; } = "<dict-code>(.*?)</dict-code>";

    public string DictItemIdExtractRegular { get; set; } = "<dict-item-id>(.*?)</dict-item-id>";
}
