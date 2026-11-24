using System.Data.Common;
using Qolome.Devtoy.Core.Models;

namespace Qolome.Devtoy.Core.Services;

/// <summary>兼容入口：统一走 <see cref="DbConnectionFactory"/>。</summary>
public static class MySqlConnectionFactory
{
    public static DbConnection Open(DataSourceSettings ds) => DbConnectionFactory.Open(ds);
}
