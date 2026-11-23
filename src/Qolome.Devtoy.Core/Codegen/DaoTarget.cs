namespace Qolome.Devtoy.Core.Codegen;

/// <summary>对齐 Java DaoTarget。</summary>
public sealed class DaoTarget
{
    public string ProjectPath { get; set; } = "";

    public string ProjectName { get; set; } = "cloud-common";

    public string ModelName { get; set; } = "";

    public string Auth { get; set; } = "<a href=\"mailto:qolome@outlook.com\">Qolome</a>";

    public string BasePackage { get; set; } = "com.qolome.starter";

    /// <summary>实体基类；默认 com.qolome.starter.dao.model.BaseModel。</summary>
    public string? EntitySuperClass { get; set; } = "com.qolome.starter.dao.model.BaseModel";

    public string EntityPackage { get; set; } = "dao.model";

    public string Tables { get; set; } = "";

    public string ColumnConstantFieldPrefix { get; set; } = "COL_";

    public string MapperPackage { get; set; } = "dao.mapper";

    public string XmlPackage { get; set; } = "mapper";

    public bool ServiceGenEnabled { get; set; } = true;

    public string ServicePackage { get; set; } = "service";

    public string ServiceSuffixName { get; set; } = "Service";

    public bool ServiceEnableFileOverride { get; set; } = true;

    public string ServiceImplPackage { get; set; } = "service.impl";

    public bool ControllerGenEnabled { get; set; } = true;

    public bool ControllerEnableFileOverride { get; set; } = true;

    public string ControllerPackage { get; set; } = "controller";

    public bool ControllerRestStyle { get; set; } = true;

    public bool DtoGenEnabled { get; set; } = true;

    public string DtoPackageName { get; set; } = "dto";

    public bool DtoConvertGenEnabled { get; set; } = true;

    public string DtoConvertPackageName { get; set; } = "convert";

    public string Version { get; set; } = "";
}
