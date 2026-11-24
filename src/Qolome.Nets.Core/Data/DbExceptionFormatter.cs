using Microsoft.EntityFrameworkCore;
using System.Text;

namespace Qolome.Nets.Core.Data;

/// <summary>将 EF / 数据库异常展开为可读中文消息（含内层异常）。</summary>
public static class DbExceptionFormatter
{
    public static string Format(Exception ex, string? operation = null)
    {
        var root = Unwrap(ex);
        var sb = new StringBuilder();
        if (!string.IsNullOrWhiteSpace(operation))
        {
            sb.Append(operation).Append("失败：");
        }

        sb.Append(root.Message);
        if (ex is DbUpdateException dbEx && dbEx.Entries.Count > 0)
        {
            var types = dbEx.Entries
                .Select(e => e.Entity.GetType().Name)
                .Distinct()
                .Take(5);
            sb.Append("（实体：").Append(string.Join(", ", types)).Append('）');
        }

        return sb.ToString();
    }

    public static Exception Unwrap(Exception ex)
    {
        var current = ex;
        while (current.InnerException is not null
               && (current is DbUpdateException
                   || current is AggregateException
                   || string.Equals(
                       current.Message,
                       "An error occurred while saving the entity changes. See the inner exception for details.",
                       StringComparison.Ordinal)))
        {
            current = current.InnerException is AggregateException agg
                ? agg.Flatten().InnerExceptions.FirstOrDefault() ?? agg.InnerException ?? current.InnerException!
                : current.InnerException;
        }

        return current;
    }
}
