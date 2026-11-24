using System.Data;
using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using MySqlConnector;
using Qolome.Devtoy.Core.Services;
using Qolome.Nets.Core.Models;
using Qolome.Nets.Core.Scanning;

namespace Qolome.Nets.Core.Data;

public static class AppDatabase
{
    private const string OuiColumnBatchKey = "BatchKey";

    public static string DataDirectory => DevtoyPaths.AppDataRoot;

    public static string DbPath { get; private set; } = DevtoyPaths.LocalDbPath;

    public static string? StartupWarning { get; set; }

    /// <summary>业务库是否为 MySQL（唯一受支持的日常 EF 业务方言）。</summary>
    public static bool IsMySql
    {
        get
        {
            try
            {
                return RequireBusinessConfig().IsMySql;
            }
            catch
            {
                return false;
            }
        }
    }

    public static string ProviderDisplayName
    {
        get
        {
            try
            {
                var db = RequireBusinessConfig();
                return db.IsMySql ? "MySQL" : db.Provider;
            }
            catch
            {
                return "未配置";
            }
        }
    }

    /// <summary>业务读写：走「数据源设置」远程库（当前 EF 支持 MySQL）。</summary>
    public static NetsDbContext CreateContext() => CreateContext(RequireBusinessConfig());

    public static NetsDbContext CreateContext(DatabaseConfig database)
    {
        database.Normalize();
        var options = new DbContextOptionsBuilder<NetsDbContext>();
        Configure(options, database);
        return new NetsDbContext(options.Options);
    }

    public static DatabaseConfig RequireBusinessConfig()
    {
        var ds = NetsRuntime.RequireRemoteDataSource();
        return NetsDataSourceBridge.ToBusinessDatabaseConfig(ds);
    }

    public static bool TryGetBusinessConfig(out DatabaseConfig config, out string? error)
    {
        if (!NetsRuntime.TryGetRemoteDataSource(out var ds, out error))
        {
            config = null!;
            return false;
        }

        try
        {
            config = NetsDataSourceBridge.ToBusinessDatabaseConfig(ds);
            error = null;
            return true;
        }
        catch (Exception ex)
        {
            config = null!;
            error = ex.Message;
            return false;
        }
    }

    public static async Task InitializeAsync()
    {
        await PrepareRuntimeAsync().ConfigureAwait(false);
        if (!string.IsNullOrWhiteSpace(StartupWarning))
        {
            return;
        }

        await LoadStartupDataAsync().ConfigureAwait(false);
    }

    public static async Task PrepareRuntimeAsync()
    {
        AppConfigStore.Load();
        Directory.CreateDirectory(DataDirectory);
        Directory.CreateDirectory(DeviceTypeIconStore.CustomDirectory);
        ApplySqlitePath(NetsDataSourceBridge.LocalSqliteConfig());

        if (!TryGetBusinessConfig(out var business, out var error))
        {
            StartupWarning = error ?? "未配置业务数据源。";
            return;
        }

        AppConfigStore.Current.Database = business;
        AppConfigStore.Save(AppConfigStore.Current);
        StartupWarning = null;
        await PrepareSchemaAsync(business).ConfigureAwait(false);
    }

    public static async Task LoadStartupDataAsync(params Func<Task>[] extras)
    {
        if (!TryGetBusinessConfig(out _, out var error))
        {
            StartupWarning = error ?? StartupWarning;
            return;
        }

        var tasks = new List<Task>
        {
            SeedFromNewContextAsync(),
            OuiStore.EnsureSeededAsync()
        };
        foreach (var extra in extras)
        {
            tasks.Add(extra());
        }

        await Task.WhenAll(tasks).ConfigureAwait(false);
    }

    private static async Task SeedFromNewContextAsync()
    {
        await using var db = CreateContext();
        await SeedAsync(db).ConfigureAwait(false);
    }

    public static async Task PrepareSchemaAsync(DatabaseConfig database)
    {
        database.Normalize();
        if (database.IsMySql)
        {
            await EnsureMySqlDatabaseAsync(database.Mysql);
        }
        else
        {
            Directory.CreateDirectory(Path.GetDirectoryName(ResolveSqlitePath(database))!);
        }

        await using var db = CreateContext(database);
        await db.Database.EnsureCreatedAsync();
        await ApplySqlitePragmasAsync(db);
        await UpgradeSchemaAsync(db);
    }

    public static async Task EnsureTopologyStorageAsync()
    {
        await using var db = CreateContext();
        await EnsureNetworkTopologyTableAsync(db, ContextIsMySql(db), swallow: false);
    }

    public static string DescribeConnection()
    {
        if (TryGetBusinessConfig(out var config, out var error))
        {
            return DescribeConnection(config);
        }

        return error ?? "未配置业务数据源";
    }

    public static string DescribeConnection(DatabaseConfig database)
    {
        database.Normalize();
        if (database.IsMySql)
        {
            var m = database.Mysql;
            return $"MySQL {m.User}@{m.Host}:{m.Port}/{m.Database}";
        }

        return $"SQLite {ResolveSqlitePath(database)}";
    }

    public static string ResolveSqlitePath(DatabaseConfig database)
    {
        database.Normalize();
        if (string.IsNullOrWhiteSpace(database.Sqlite.Path))
        {
            return DevtoyPaths.LocalDbPath;
        }

        var raw = Environment.ExpandEnvironmentVariables(database.Sqlite.Path);
        return Path.IsPathRooted(raw)
            ? Path.GetFullPath(raw)
            : Path.GetFullPath(Path.Combine(DataDirectory, raw));
    }

    private static void ApplySqlitePath(DatabaseConfig database) => DbPath = ResolveSqlitePath(database);

    private static void Configure(DbContextOptionsBuilder<NetsDbContext> builder, DatabaseConfig database)
    {
        if (database.IsMySql)
        {
            var cs = BuildMySqlConnectionString(database.Mysql, includeDatabase: true);
            builder.UseMySql(cs, new MySqlServerVersion(new Version(8, 0, 21)));
            builder.AddInterceptors(MySqlTimeZoneInterceptor.Instance);
            return;
        }

            builder.UseSqlite($"Data Source={ResolveSqlitePath(database)};Cache=Shared;Pooling=True");
    }

    private static async Task ApplySqlitePragmasAsync(NetsDbContext db)
    {
        if (ContextIsMySql(db))
        {
            return;
        }

        var connection = db.Database.GetDbConnection();
        var opened = connection.State != ConnectionState.Open;
        if (opened)
        {
            await connection.OpenAsync().ConfigureAwait(false);
        }

        try
        {
            await using var journal = connection.CreateCommand();
            journal.CommandText = "PRAGMA journal_mode=WAL;";
            await journal.ExecuteScalarAsync().ConfigureAwait(false);
            await using var timeout = connection.CreateCommand();
            timeout.CommandText = "PRAGMA busy_timeout=5000;";
            await timeout.ExecuteNonQueryAsync().ConfigureAwait(false);
        }
        finally
        {
            if (opened)
            {
                await connection.CloseAsync().ConfigureAwait(false);
            }
        }
    }

    private static string BuildMySqlConnectionString(MySqlConfig mysql, bool includeDatabase)
    {
        var builder = new MySqlConnectionStringBuilder
        {
            Server = mysql.Host,
            Port = (uint)mysql.Port,
            UserID = mysql.User,
            Password = mysql.Password,
            CharacterSet = "utf8mb4",
            AllowUserVariables = true,
            ConnectionTimeout = 8
        };
        if (includeDatabase)
        {
            builder.Database = mysql.Database;
        }

        return builder.ConnectionString;
    }

    private static async Task EnsureMySqlDatabaseAsync(MySqlConfig mysql)
    {
        try
        {
            var adminCs = BuildMySqlConnectionString(mysql, includeDatabase: false);
            await using var conn = new MySqlConnection(adminCs);
            await conn.OpenAsync();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = NetsSqlIdentifiers.BuildMySqlCreateDatabaseSql(mysql.Database);
            await cmd.ExecuteNonQueryAsync();
        }
        catch (Exception ex)
        {
            throw new ScanException($"无法连接或创建 MySQL 数据库：{ex.Message}");
        }
    }

    private static async Task SeedAsync(NetsDbContext db)
    {
        var catalog = DeviceTypeCatalog.Defaults;
        var existing = await db.DeviceTypes.ToListAsync();
        await MergeDuplicateDeviceTypesAsync(db, existing);
        await db.SaveChangesAsync();
        var byCode = existing.ToDictionary(x => x.Code, StringComparer.OrdinalIgnoreCase);
        await MergeAliasDeviceTypeAsync(db, existing, byCode, fromCode: "tv-box", toCode: "streaming");
        await MergeAliasDeviceTypeAsync(db, existing, byCode, fromCode: "iptv-box", toCode: "set-top");
        await MergeAliasDeviceTypeAsync(db, existing, byCode, fromCode: "computer", toCode: "desktop");
        await db.SaveChangesAsync();
        foreach (var item in catalog)
        {
            if (byCode.TryGetValue(item.Code, out var entity))
            {
                entity.Name = item.Name;
                entity.Description = item.Description;
                entity.SortOrder = item.SortOrder;
                continue;
            }

            if (existing.Any(x => x.Name.Equals(item.Name, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            var created = new DeviceType
            {
                Code = item.Code,
                Name = item.Name,
                Description = item.Description,
                SortOrder = item.SortOrder,
                ColorGroup = DeviceTypeColorGroups.ForCode(item.Code)
            };
            db.DeviceTypes.Add(created);
            existing.Add(created);
            byCode[item.Code] = created;
        }

        ApplyCatalogColorGroups(existing, catalog);

        if (!await db.Environments.AnyAsync())
        {
            var cidr = NetworkAdapterEnumerator.GetActiveIpv4Adapters().FirstOrDefault()?.Cidr;
            var now = DateTime.UtcNow;
            db.Environments.Add(new NetworkEnvironment
            {
                Name = "默认环境",
                Description = "本机当前局域网",
                DefaultCidr = cidr,
                CreatedAtUtc = now,
                UpdatedAtUtc = now
            });
        }

        await db.SaveChangesAsync();
        await EnsureDeviceTypeUniqueIndexesAsync(db);
    }

    private static async Task MergeDuplicateDeviceTypesAsync(NetsDbContext db, List<DeviceType> existing)
    {
        await CollapseDeviceTypesAsync(db, existing, x => x.Code);
        await CollapseDeviceTypesAsync(db, existing, x => x.Name);
        foreach (var entity in existing)
        {
            entity.Code = (entity.Code ?? "").Trim();
            entity.Name = (entity.Name ?? "").Trim();
            if (!string.IsNullOrWhiteSpace(entity.Description))
            {
                entity.Description = entity.Description.Trim();
            }

            if (!string.IsNullOrWhiteSpace(entity.ColorGroup))
            {
                entity.ColorGroup = entity.ColorGroup.Trim();
            }
        }
    }

    private static async Task CollapseDeviceTypesAsync(
        NetsDbContext db,
        List<DeviceType> existing,
        Func<DeviceType, string> keySelector)
    {
        var groups = existing
            .Where(x => !string.IsNullOrWhiteSpace(keySelector(x)))
            .GroupBy(x => keySelector(x).Trim(), StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1)
            .ToList();
        foreach (var group in groups)
        {
            var ranked = group
                .OrderByDescending(x => x.IsCustomIcon)
                .ThenBy(x => keySelector(x).Trim().Length)
                .ThenBy(x => keySelector(x).Length)
                .ThenBy(x => x.Id)
                .ToList();
            var keeper = ranked[0];
            foreach (var loser in ranked.Skip(1))
            {
                await AbsorbDeviceTypeAsync(db, existing, keeper, loser);
            }
        }
    }

    private static async Task AbsorbDeviceTypeAsync(
        NetsDbContext db,
        List<DeviceType> existing,
        DeviceType keeper,
        DeviceType loser)
    {
        if (keeper.Id != 0 && loser.Id != 0 && keeper.Id != loser.Id)
        {
            await db.Devices.Where(d => d.DeviceTypeId == loser.Id)
                .ExecuteUpdateAsync(d => d.SetProperty(x => x.DeviceTypeId, keeper.Id));
        }

        if (!keeper.IsCustomIcon && loser.IsCustomIcon)
        {
            keeper.IconFile = loser.IconFile;
            keeper.IsCustomIcon = true;
        }
        else if (loser.IsCustomIcon
            && !string.Equals(keeper.IconFile, loser.IconFile, StringComparison.OrdinalIgnoreCase))
        {
            DeviceTypeIconStore.DeleteCustom(loser.IconFile);
        }

        if (string.IsNullOrWhiteSpace(keeper.Description) && !string.IsNullOrWhiteSpace(loser.Description))
        {
            keeper.Description = loser.Description;
        }

        if ((string.IsNullOrWhiteSpace(keeper.ColorGroup)
                || keeper.ColorGroup == DeviceTypeColorGroups.Other)
            && !string.IsNullOrWhiteSpace(loser.ColorGroup)
            && loser.ColorGroup != DeviceTypeColorGroups.Other)
        {
            keeper.ColorGroup = loser.ColorGroup;
        }

        db.DeviceTypes.Remove(loser);
        existing.Remove(loser);
    }

    private static async Task EnsureDeviceTypeUniqueIndexesAsync(NetsDbContext db)
    {
        if (ContextIsMySql(db))
        {
            await TryExecuteAsync(db, "CREATE UNIQUE INDEX `ix_devtoy_device_type_code` ON `devtoy_device_type` (`Code`)");
            await TryExecuteAsync(db, "CREATE UNIQUE INDEX `ix_devtoy_device_type_name` ON `devtoy_device_type` (`Name`)");
            return;
        }

        await TryExecuteAsync(db, "CREATE UNIQUE INDEX IF NOT EXISTS ix_devtoy_device_type_code ON devtoy_device_type (Code);");
        await TryExecuteAsync(db, "CREATE UNIQUE INDEX IF NOT EXISTS ix_devtoy_device_type_name ON devtoy_device_type (Name);");
    }

    private static async Task MergeAliasDeviceTypeAsync(
        NetsDbContext db,
        List<DeviceType> existing,
        Dictionary<string, DeviceType> byCode,
        string fromCode,
        string toCode)
    {
        if (byCode.TryGetValue(fromCode, out var from))
        {
            if (byCode.TryGetValue(toCode, out var to) && from.Id != to.Id)
            {
                if (from.Id != 0)
                {
                    await db.Devices.Where(d => d.DeviceTypeId == from.Id)
                        .ExecuteUpdateAsync(d => d.SetProperty(x => x.DeviceTypeId, to.Id));
                }

                db.DeviceTypes.Remove(from);
                existing.Remove(from);
                byCode.Remove(from.Code);
                return;
            }

            if (!byCode.ContainsKey(toCode))
            {
                byCode.Remove(from.Code);
                from.Code = toCode;
                byCode[toCode] = from;
            }
        }
    }

    private static void ApplyCatalogColorGroups(
        IReadOnlyCollection<DeviceType> existing,
        IReadOnlyList<DeviceTypeCatalog.Item> catalog)
    {
        var catalogCodes = catalog
            .Select(x => x.Code)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var misplaced = existing.Count(e =>
            catalogCodes.Contains(e.Code)
            && string.Equals(e.ColorGroup, DeviceTypeColorGroups.Other, StringComparison.OrdinalIgnoreCase)
            && DeviceTypeColorGroups.ForCode(e.Code) != DeviceTypeColorGroups.Other);
        var remapStuckOther = misplaced >= 20;

        foreach (var entity in existing)
        {
            var mapped = DeviceTypeColorGroups.ForCode(entity.Code);
            if (string.IsNullOrWhiteSpace(entity.ColorGroup))
            {
                entity.ColorGroup = mapped;
                continue;
            }

            if (remapStuckOther
                && catalogCodes.Contains(entity.Code)
                && string.Equals(entity.ColorGroup, DeviceTypeColorGroups.Other, StringComparison.OrdinalIgnoreCase)
                && mapped != DeviceTypeColorGroups.Other)
            {
                entity.ColorGroup = mapped;
            }
        }
    }

    private static async Task UpgradeSchemaAsync(NetsDbContext db)
    {
        if (ContextIsMySql(db))
        {
            await EnsureColumnAsync(db, NetsTableNames.DeviceType, "IconFile",
                "ALTER TABLE `devtoy_device_type` ADD COLUMN `IconFile` VARCHAR(128) NULL");
            await EnsureColumnAsync(db, NetsTableNames.DeviceType, "IsCustomIcon",
                "ALTER TABLE `devtoy_device_type` ADD COLUMN `IsCustomIcon` TINYINT(1) NOT NULL DEFAULT 0");
            await EnsureColumnAsync(db, NetsTableNames.DeviceType, "ColorGroup",
                "ALTER TABLE `devtoy_device_type` ADD COLUMN `ColorGroup` VARCHAR(32) NULL");
            await EnsureColumnAsync(db, NetsTableNames.Device, "Brand",
                $"ALTER TABLE `{NetsTableNames.Device}` ADD COLUMN `Brand` VARCHAR(128) NULL");
            await EnsureColumnAsync(db, NetsTableNames.Device, "Vendor",
                $"ALTER TABLE `{NetsTableNames.Device}` ADD COLUMN `Vendor` VARCHAR(256) NULL");
            await EnsureColumnAsync(db, NetsTableNames.Device, "Description",
                $"ALTER TABLE `{NetsTableNames.Device}` ADD COLUMN `Description` VARCHAR(512) NULL");
            await TryExecuteAsync(db,
                """
                CREATE TABLE IF NOT EXISTS `devtoy_port_purpose` (
                    `Id` INT NOT NULL AUTO_INCREMENT PRIMARY KEY,
                    `Port` INT NOT NULL,
                    `Protocol` VARCHAR(8) NOT NULL,
                    `Service` VARCHAR(64) NULL,
                    `Description` VARCHAR(256) NULL
                ) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci
                """);
            await TryExecuteAsync(db,
                $"CREATE UNIQUE INDEX `ix_devtoy_device_environment_mac` ON `{NetsTableNames.Device}` (`NetworkEnvironmentId`, `Mac`)");
            await TryExecuteAsync(db,
                "CREATE UNIQUE INDEX `ix_devtoy_port_purpose_port_protocol` ON `devtoy_port_purpose` (`Port`, `Protocol`)");
            await TryExecuteAsync(db,
                """
                CREATE TABLE IF NOT EXISTS `devtoy_oui_entry` (
                    `Id` INT NOT NULL AUTO_INCREMENT PRIMARY KEY,
                    `Prefix` VARCHAR(16) NOT NULL,
                    `BitLength` INT NOT NULL,
                    `Registry` VARCHAR(16) NOT NULL,
                    `Vendor` VARCHAR(256) NOT NULL,
                    `BatchKey` VARCHAR(64) NULL
                ) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci
                """);
            await TryExecuteAsync(db,
                "CREATE UNIQUE INDEX `ix_devtoy_oui_entry_prefix` ON `devtoy_oui_entry` (`Prefix`)");
            await TryExecuteAsync(db,
                """
                CREATE TABLE IF NOT EXISTS `devtoy_oui_meta` (
                    `Id` INT NOT NULL PRIMARY KEY,
                    `Source` VARCHAR(32) NOT NULL,
                    `BundledAtUtc` DATETIME NULL,
                    `ImportedAtUtc` DATETIME NOT NULL,
                    `RecordCount` INT NOT NULL,
                    `Note` VARCHAR(512) NULL,
                    `BatchKey` VARCHAR(64) NULL,
                    `PreviousBatchKey` VARCHAR(64) NULL,
                    `AddedCount` INT NOT NULL DEFAULT 0,
                    `UpdatedCount` INT NOT NULL DEFAULT 0,
                    `RemovedCount` INT NOT NULL DEFAULT 0
                ) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci
                """);
            await EnsureOuiBatchColumnsAsync(db, mysql: true);
            await EnsureNetworkTopologyTableAsync(db, mysql: true);
            await EnsureDeviceNicTableAsync(db, mysql: true);
            await BackfillDeviceVendorAsync(db);
            await BackfillDeviceNicsAsync(db);
            return;
        }

        await EnsureColumnAsync(db, NetsTableNames.DeviceType, "IconFile", "ALTER TABLE devtoy_device_type ADD COLUMN IconFile TEXT NULL");
        await EnsureColumnAsync(db, NetsTableNames.DeviceType, "IsCustomIcon", "ALTER TABLE devtoy_device_type ADD COLUMN IsCustomIcon INTEGER NOT NULL DEFAULT 0");
        await EnsureColumnAsync(db, NetsTableNames.DeviceType, "ColorGroup", "ALTER TABLE devtoy_device_type ADD COLUMN ColorGroup TEXT NULL");
        await EnsureColumnAsync(db, NetsTableNames.Device, "Brand",
            $"ALTER TABLE {NetsTableNames.Device} ADD COLUMN Brand TEXT NULL");
        await EnsureColumnAsync(db, NetsTableNames.Device, "Vendor",
            $"ALTER TABLE {NetsTableNames.Device} ADD COLUMN Vendor TEXT NULL");
        await EnsureColumnAsync(db, NetsTableNames.Device, "Description",
            $"ALTER TABLE {NetsTableNames.Device} ADD COLUMN Description TEXT NULL");
        await TryExecuteAsync(db,
            $"CREATE UNIQUE INDEX IF NOT EXISTS ix_devtoy_device_environment_mac ON {NetsTableNames.Device} (NetworkEnvironmentId, Mac);");
        await db.Database.ExecuteSqlRawAsync(
            """
            CREATE TABLE IF NOT EXISTS devtoy_port_purpose (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                Port INTEGER NOT NULL,
                Protocol TEXT NOT NULL,
                Service TEXT NULL,
                Description TEXT NULL
            );
            """);
        await db.Database.ExecuteSqlRawAsync(
            "CREATE UNIQUE INDEX IF NOT EXISTS ix_devtoy_port_purpose_port_protocol ON devtoy_port_purpose (Port, Protocol);");
        await db.Database.ExecuteSqlRawAsync(
            """
            CREATE TABLE IF NOT EXISTS devtoy_oui_entry (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                Prefix TEXT NOT NULL,
                BitLength INTEGER NOT NULL,
                Registry TEXT NOT NULL,
                Vendor TEXT NOT NULL,
                BatchKey TEXT NULL
            );
            """);
        await db.Database.ExecuteSqlRawAsync(
            "CREATE UNIQUE INDEX IF NOT EXISTS ix_devtoy_oui_entry_prefix ON devtoy_oui_entry (Prefix);");
        await db.Database.ExecuteSqlRawAsync(
            """
            CREATE TABLE IF NOT EXISTS devtoy_oui_meta (
                Id INTEGER PRIMARY KEY,
                Source TEXT NOT NULL,
                BundledAtUtc TEXT NULL,
                ImportedAtUtc TEXT NOT NULL,
                RecordCount INTEGER NOT NULL,
                Note TEXT NULL,
                BatchKey TEXT NULL,
                PreviousBatchKey TEXT NULL,
                AddedCount INTEGER NOT NULL DEFAULT 0,
                UpdatedCount INTEGER NOT NULL DEFAULT 0,
                RemovedCount INTEGER NOT NULL DEFAULT 0
            );
            """);
        await EnsureOuiBatchColumnsAsync(db, mysql: false);
        await EnsureNetworkTopologyTableAsync(db, mysql: false);
        await EnsureDeviceNicTableAsync(db, mysql: false);
        await BackfillDeviceVendorAsync(db);
        await BackfillDeviceNicsAsync(db);
    }

    private static async Task EnsureNetworkTopologyTableAsync(NetsDbContext db, bool mysql, bool swallow = true)
    {
        // 不在 DDL 里带外键：历史库可能因引擎/字符集导致 CREATE 失败，
        // TryExecuteAsync 会吞掉异常，之后保存拓扑就无法写入 MySQL。
        var sql = mysql
            ? """
              CREATE TABLE IF NOT EXISTS `devtoy_network_topology` (
                  `NetworkEnvironmentId` INT NOT NULL PRIMARY KEY,
                  `Json` LONGTEXT NOT NULL,
                  `UpdatedAtUtc` DATETIME(6) NOT NULL
              ) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci
              """
            : """
              CREATE TABLE IF NOT EXISTS devtoy_network_topology (
                  NetworkEnvironmentId INTEGER NOT NULL PRIMARY KEY,
                  Json TEXT NOT NULL,
                  UpdatedAtUtc TEXT NOT NULL
              );
              """;
        if (swallow)
        {
            await TryExecuteAsync(db, sql);
            return;
        }

        await db.Database.ExecuteSqlRawAsync(sql);
    }

    private static async Task EnsureDeviceNicTableAsync(NetsDbContext db, bool mysql)
    {
        var sql = mysql
            ? """
              CREATE TABLE IF NOT EXISTS `devtoy_device_nic` (
                  `Id` INT NOT NULL AUTO_INCREMENT PRIMARY KEY,
                  `DeviceId` INT NOT NULL,
                  `Name` VARCHAR(64) NULL,
                  `Ip` VARCHAR(64) NULL,
                  `Mac` VARCHAR(32) NULL,
                  `SortOrder` INT NOT NULL DEFAULT 0
              ) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci
              """
            : """
              CREATE TABLE IF NOT EXISTS devtoy_device_nic (
                  Id INTEGER PRIMARY KEY AUTOINCREMENT,
                  DeviceId INTEGER NOT NULL,
                  Name TEXT NULL,
                  Ip TEXT NULL,
                  Mac TEXT NULL,
                  SortOrder INTEGER NOT NULL DEFAULT 0
              );
              """;
        await TryExecuteAsync(db, sql);
        await TryExecuteAsync(db, mysql
            ? "CREATE INDEX `ix_devtoy_device_nic_device_id` ON `devtoy_device_nic` (`DeviceId`)"
            : "CREATE INDEX IF NOT EXISTS ix_devtoy_device_nic_device_id ON devtoy_device_nic (DeviceId);");
    }

    private static async Task BackfillDeviceNicsAsync(NetsDbContext db)
    {
        var devices = await db.Devices.Include(x => x.Nics).ToListAsync();
        var changed = false;
        foreach (var device in devices)
        {
            if (device.Nics.Count > 0)
            {
                continue;
            }

            if (string.IsNullOrWhiteSpace(device.Ip) && string.IsNullOrWhiteSpace(device.Mac))
            {
                continue;
            }

            device.Nics.Add(new DeviceNic
            {
                Ip = device.Ip,
                Mac = device.Mac,
                SortOrder = 0
            });
            changed = true;
        }

        if (changed)
        {
            await db.SaveChangesAsync();
        }
    }

    private static async Task BackfillDeviceVendorAsync(NetsDbContext db)
    {
        var devices = await db.Devices
            .Where(x => x.DeviceInfo != null && x.DeviceInfo != "")
            .ToListAsync();
        var changed = false;
        foreach (var device in devices)
        {
            if (!OsTypeGuess.TrySplitVendor(device.DeviceInfo, out var vendor, out var remainder))
            {
                continue;
            }

            if (string.IsNullOrWhiteSpace(device.Vendor))
            {
                device.Vendor = vendor;
                device.DeviceInfo = remainder;
                changed = true;
            }
            else if (string.Equals(device.Vendor, vendor, StringComparison.OrdinalIgnoreCase))
            {
                device.DeviceInfo = remainder;
                changed = true;
            }
        }

        if (changed)
        {
            await db.SaveChangesAsync();
        }
    }

    private static async Task EnsureOuiBatchColumnsAsync(NetsDbContext db, bool mysql)
    {
        if (mysql)
        {
            await EnsureColumnAsync(db, NetsTableNames.OuiEntry, OuiColumnBatchKey,
                "ALTER TABLE `devtoy_oui_entry` ADD COLUMN `BatchKey` VARCHAR(64) NULL");
            await EnsureColumnAsync(db, NetsTableNames.OuiMeta, OuiColumnBatchKey,
                "ALTER TABLE `devtoy_oui_meta` ADD COLUMN `BatchKey` VARCHAR(64) NULL");
            await EnsureColumnAsync(db, NetsTableNames.OuiMeta, "PreviousBatchKey",
                "ALTER TABLE `devtoy_oui_meta` ADD COLUMN `PreviousBatchKey` VARCHAR(64) NULL");
            await EnsureColumnAsync(db, NetsTableNames.OuiMeta, "AddedCount",
                "ALTER TABLE `devtoy_oui_meta` ADD COLUMN `AddedCount` INT NOT NULL DEFAULT 0");
            await EnsureColumnAsync(db, NetsTableNames.OuiMeta, "UpdatedCount",
                "ALTER TABLE `devtoy_oui_meta` ADD COLUMN `UpdatedCount` INT NOT NULL DEFAULT 0");
            await EnsureColumnAsync(db, NetsTableNames.OuiMeta, "RemovedCount",
                "ALTER TABLE `devtoy_oui_meta` ADD COLUMN `RemovedCount` INT NOT NULL DEFAULT 0");
            return;
        }

        await EnsureColumnAsync(db, NetsTableNames.OuiEntry, OuiColumnBatchKey,
            "ALTER TABLE devtoy_oui_entry ADD COLUMN BatchKey TEXT NULL");
        await EnsureColumnAsync(db, NetsTableNames.OuiMeta, OuiColumnBatchKey,
            "ALTER TABLE devtoy_oui_meta ADD COLUMN BatchKey TEXT NULL");
        await EnsureColumnAsync(db, NetsTableNames.OuiMeta, "PreviousBatchKey",
            "ALTER TABLE devtoy_oui_meta ADD COLUMN PreviousBatchKey TEXT NULL");
        await EnsureColumnAsync(db, NetsTableNames.OuiMeta, "AddedCount",
            "ALTER TABLE devtoy_oui_meta ADD COLUMN AddedCount INTEGER NOT NULL DEFAULT 0");
        await EnsureColumnAsync(db, NetsTableNames.OuiMeta, "UpdatedCount",
            "ALTER TABLE devtoy_oui_meta ADD COLUMN UpdatedCount INTEGER NOT NULL DEFAULT 0");
        await EnsureColumnAsync(db, NetsTableNames.OuiMeta, "RemovedCount",
            "ALTER TABLE devtoy_oui_meta ADD COLUMN RemovedCount INTEGER NOT NULL DEFAULT 0");
    }

    private static async Task TryExecuteAsync(NetsDbContext db, string sql)
    {
        try
        {
            await db.Database.ExecuteSqlRawAsync(sql);
        }
        catch (Exception)
        {
            // 索引已存在或历史重复数据时跳过。
        }
    }

    private static async Task EnsureColumnAsync(NetsDbContext db, string table, string column, string ddl)
    {
        NetsSqlIdentifiers.RequireKnownTableName(table);
        NetsSqlIdentifiers.RequireColumnName(column);
        var names = await ReadColumnNamesAsync(db, table);
        if (!names.Contains(column))
        {
            await db.Database.ExecuteSqlRawAsync(ddl);
        }
    }

    private static async Task<HashSet<string>> ReadColumnNamesAsync(NetsDbContext db, string table)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var connection = db.Database.GetDbConnection();
        var shouldClose = connection.State != ConnectionState.Open;
        if (shouldClose)
        {
            await db.Database.OpenConnectionAsync();
        }

        try
        {
            await using var cmd = connection.CreateCommand();
            if (ContextIsMySql(db))
            {
                cmd.CommandText =
                    """
                    SELECT COLUMN_NAME FROM INFORMATION_SCHEMA.COLUMNS
                    WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = @table
                    """;
                var p = cmd.CreateParameter();
                p.ParameterName = "@table";
                p.Value = table;
                cmd.Parameters.Add(p);
            }
            else
            {
                cmd.CommandText = NetsSqlIdentifiers.ResolvePragmaTableInfoSql(table);
            }

            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                names.Add(reader.GetString(ContextIsMySql(db) ? 0 : 1));
            }
        }
        finally
        {
            if (shouldClose)
            {
                await db.Database.CloseConnectionAsync();
            }
        }

        return names;
    }

    private static bool ContextIsMySql(NetsDbContext db) =>
        (db.Database.ProviderName ?? "").Contains("MySql", StringComparison.OrdinalIgnoreCase);

    private sealed class MySqlTimeZoneInterceptor : DbConnectionInterceptor
    {
        public static MySqlTimeZoneInterceptor Instance { get; } = new();

        public override void ConnectionOpened(DbConnection connection, ConnectionEndEventData eventData)
        {
            try
            {
                using var cmd = connection.CreateCommand();
                cmd.CommandText = SessionTimeZoneSql();
                cmd.ExecuteNonQuery();
            }
            catch
            {
                // 会话时区失败不阻断后续读写。
            }
        }

        public override async Task ConnectionOpenedAsync(
            DbConnection connection,
            ConnectionEndEventData eventData,
            CancellationToken cancellationToken = default)
        {
            try
            {
                await using var cmd = connection.CreateCommand();
                cmd.CommandText = SessionTimeZoneSql();
                await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                // 会话时区失败不阻断后续读写。
            }
        }

        private static string SessionTimeZoneSql()
        {
            var offset = AppTimeZones.ToMysqlOffset(AppConfigStore.Current.TimeZone);
            if (offset.Length != 6 || (offset[0] != '+' && offset[0] != '-'))
            {
                offset = "+00:00";
            }

            return $"SET time_zone = '{offset}'";
        }
    }
}
