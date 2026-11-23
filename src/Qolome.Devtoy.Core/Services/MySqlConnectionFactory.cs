namespace Qolome.Devtoy.Core.Services;

using MySqlConnector;
using Qolome.Devtoy.Core.Models;

public static class MySqlConnectionFactory
{
    public static MySqlConnection Open(DataSourceSettings ds)
    {
        if (ds.DatabaseKind != DatabaseKind.MySql)
        {
            throw new NotSupportedException("当前实现仅支持 MySQL，其它数据库后续补齐。");
        }

        var url = JdbcUrlBuilder.Resolve(ds);
        var cs = JdbcUrlBuilder.ToMySqlConnectionString(url, ds.Username, ds.Password);
        var conn = new MySqlConnection(cs);
        conn.Open();
        return conn;
    }
}