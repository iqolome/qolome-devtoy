using System.Text;
using System.Text.RegularExpressions;
using Qolome.Devtoy.Core.Models;
using Qolome.Devtoy.Core.Services;

namespace Qolome.Devtoy.Core.Codegen;

/// <summary>对齐 Java DictCommentRenderer / DictTypeConvertHandler：注释字典标记 → 枚举说明与类型。</summary>
public sealed class DictCommentRenderer
{
    private readonly CodegenSettings _settings;
    private readonly DataSourceSettings _dataSource;
    private readonly VelocityFiles _velocityFiles;
    private readonly string _tableName;
    private readonly IReadOnlyList<ColumnMetadata> _columns;
    private readonly Regex _codeRegex;
    private readonly Regex _itemIdRegex;

    public DictCommentRenderer(
        CodegenSettings settings,
        DataSourceSettings dataSource,
        VelocityFiles velocityFiles,
        string tableName,
        IReadOnlyList<ColumnMetadata> columns)
    {
        _settings = settings;
        _dataSource = dataSource;
        _velocityFiles = velocityFiles;
        _tableName = tableName;
        _columns = columns;
        _codeRegex = new Regex(
            settings.DictCodeExtractRegular,
            RegexOptions.CultureInvariant,
            TimeSpan.FromSeconds(2));
        _itemIdRegex = new Regex(
            settings.DictItemIdExtractRegular,
            RegexOptions.CultureInvariant,
            TimeSpan.FromSeconds(2));
    }

    public string Render(string? comment, string columnName)
    {
        if (string.IsNullOrWhiteSpace(comment))
        {
            return comment ?? "";
        }

        var resolved = TryResolve(comment, columnName);
        if (resolved is null)
        {
            return comment;
        }

        var (token, item) = resolved.Value;
        var rendered = _velocityFiles.MergeBean(
            _settings.EntityDictCommentTemplateName,
            new CommentModel(
                new CommentDict(
                    item.ItemId.ToString(),
                    item.ItemCode,
                    item.ItemName),
                EnumClassName(item.ItemCode)));
        return comment.Replace(token, rendered, StringComparison.Ordinal);
    }

    /// <summary>
    /// 对齐 DictTypeConvertHandler：注释含字典标记时返回枚举简单类名与 import 全限定名。
    /// </summary>
    public (string SimpleName, string ImportName)? TryResolveEnumJavaType(string? comment, string columnName)
    {
        var resolved = TryResolve(comment, columnName);
        if (resolved is null)
        {
            return null;
        }

        var enumClassName = EnumClassName(resolved.Value.Item.ItemCode);
        var importName = _settings.EnumPackage.TrimEnd('.') + "." + enumClassName;
        return (enumClassName, importName);
    }

    private (string Token, DictItemRecord Item)? TryResolve(string? comment, string columnName)
    {
        if (string.IsNullOrWhiteSpace(comment))
        {
            return null;
        }

        string? token = null;
        DictItemRecord? item = null;

        var idMatch = _itemIdRegex.Match(comment);
        if (idMatch.Success)
        {
            token = idMatch.Value;
            if (!long.TryParse(idMatch.Groups[1].Value, out var itemId))
            {
                throw new InvalidOperationException(FormatDictError("字段注释中的 dict-item-id 非法: " + token, columnName, token));
            }

            item = DictCatalogService.FindItemById(_dataSource, itemId);
        }
        else
        {
            var codeMatch = _codeRegex.Match(comment);
            if (codeMatch.Success)
            {
                token = codeMatch.Value;
                item = DictCatalogService.FindItemByCode(_dataSource, codeMatch.Groups[1].Value);
            }
        }

        if (token is null)
        {
            return null;
        }

        if (item is null)
        {
            throw new InvalidOperationException(FormatDictError("字段注释中的字典不存在: " + token, columnName, token));
        }

        return (token, item);
    }

    private string FormatDictError(string headline, string columnName, string missingToken)
    {
        var sb = new StringBuilder();
        sb.AppendLine(headline);
        sb.Append("表: ").AppendLine(_tableName);
        sb.Append("字段: ").AppendLine(columnName);
        sb.AppendLine("该表注释中引用的字典:");
        var any = false;
        foreach (var column in _columns)
        {
            if (string.IsNullOrWhiteSpace(column.Comment))
            {
                continue;
            }

            foreach (var token in ExtractDictTokens(column.Comment))
            {
                any = true;
                var marker = string.Equals(token, missingToken, StringComparison.Ordinal) ? " ← 缺失" : "";
                sb.Append("  - ").Append(column.Name).Append(": ").Append(token).AppendLine(marker);
            }
        }

        if (!any)
        {
            sb.AppendLine("  （无）");
        }

        return sb.ToString().TrimEnd();
    }

    private IEnumerable<string> ExtractDictTokens(string comment)
    {
        var idMatch = _itemIdRegex.Match(comment);
        if (idMatch.Success)
        {
            yield return idMatch.Value;
            yield break;
        }

        var codeMatch = _codeRegex.Match(comment);
        if (codeMatch.Success)
        {
            yield return codeMatch.Value;
        }
    }

    private string EnumClassName(string itemCode) =>
        CaseFormats.DictCodeToUpperCamel(itemCode) + _settings.EnumNameSuffix;

    /// <summary>entity-comment.java.vm 根对象。</summary>
    public sealed record CommentModel(CommentDict qolomeDictItem, string enumClassName);

    /// <summary>模板里的 qolomeDictItem。</summary>
    public sealed record CommentDict(string itemId, string itemCode, string itemName);
}
