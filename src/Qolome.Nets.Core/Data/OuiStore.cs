using System.Data;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Qolome.Nets.Core.Models;

namespace Qolome.Nets.Core.Data;

public static class OuiStore
{
    public const string IeeeOuiUrl = "https://standards-oui.ieee.org/oui/oui.csv"; // NOSONAR S1075 — IEEE 官方 OUI CSV
    public const string IeeeMamUrl = "https://standards-oui.ieee.org/oui28/mam.csv"; // NOSONAR S1075 — IEEE 官方 MAM CSV
    public const string IeeeOui36Url = "https://standards-oui.ieee.org/oui36/oui36.csv"; // NOSONAR S1075 — IEEE 官方 OUI36 CSV

    private const int WriteChunk = 400;

    private static readonly object Gate = new();
    private static OuiLookupTable _table = OuiLookupTable.Empty;
    private static OuiMeta? _meta;

    public static string BundledDirectory { get; set; } =
        Path.Combine(AppContext.BaseDirectory, "Assets", "Oui");

    public static OuiMeta? Meta
    {
        get
        {
            lock (Gate)
            {
                return _meta;
            }
        }
    }

    public static int CachedCount => Volatile.Read(ref _table).Count;

    public static async Task EnsureSeededAsync()
    {
        await using var db = AppDatabase.CreateContext();
        if (await db.OuiEntries.AnyAsync().ConfigureAwait(false))
        {
            await ReloadAsync().ConfigureAwait(false);
            return;
        }

        var bundled = await Task.Run(() => LoadBundled()).ConfigureAwait(false);
        var (rows, batchKey) = bundled;
        if (rows.Count == 0)
        {
            throw new ScanException("安装包中没有 OUI 数据（Assets/Oui）。");
        }

        await ApplyAsync(new OuiApplyRequest(db, rows, OuiSources.Bundled, ReadBundledAt(), "安装包内置 IEEE OUI", batchKey))
            .ConfigureAwait(false);
        await ReloadAsync().ConfigureAwait(false);
    }

    public static async Task ReloadAsync()
    {
        await using var db = AppDatabase.CreateContext();
        var table = await LoadTableAsync(db).ConfigureAwait(false);
        var meta = await db.OuiMetas.AsNoTracking().FirstOrDefaultAsync().ConfigureAwait(false);
        lock (Gate)
        {
            Volatile.Write(ref _table, table);
            _meta = meta;
        }
    }

    public static string? Lookup(string? mac) => Volatile.Read(ref _table).Lookup(mac);

    public static async Task<OuiMeta> UpdateFromIeeeAsync(
        IProgress<ScanProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(3) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("Qolome.Nets (IEEE OUI update)");
        using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var map = new Dictionary<string, OuiEntry>(StringComparer.OrdinalIgnoreCase);
        (string Url, string Label)[] files =
        [
            (IeeeOuiUrl, "MA-L（oui.csv）"),
            (IeeeMamUrl, "MA-M（mam.csv）"),
            (IeeeOui36Url, "MA-S（oui36.csv）")
        ];
        var downloadSpan = 50.0 / files.Length;
        for (var i = 0; i < files.Length; i++)
        {
            var (url, label) = files[i];
            var start = i * downloadSpan;
            var mid = start + downloadSpan * 0.7;
            var end = (i + 1) * downloadSpan;
            var bytes = await DownloadIeeeAsync(http, url, label, start, mid, progress, cancellationToken)
                .ConfigureAwait(false);
            if (HeaderText(bytes).IndexOf("Assignment", StringComparison.OrdinalIgnoreCase) < 0)
            {
                throw new ScanException($"IEEE 返回的不是 OUI CSV：{url}");
            }

            sha.AppendData(bytes);
            sha.AppendData([0]);
            using var reader = new StreamReader(new MemoryStream(bytes), Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
            var parsed = 0;
            foreach (var row in OuiCsvParser.Enumerate(reader))
            {
                map[row.Prefix] = row;
                parsed++;
                if (parsed % 4000 == 0)
                {
                    Report(progress, Lerp(mid, end, parsed / (parsed + 4000.0)), $"正在解析 {label}… 已读 {parsed} 条");
                }
            }

            Report(progress, end, $"已解析 {label}，累计 {map.Count} 条");
        }

        Report(progress, 50, $"正在整理 {map.Count} 条 OUI 记录…");
        var rows = InternVendors(map.Values);
        if (rows.Count == 0)
        {
            throw new ScanException("IEEE 未返回有效 OUI 记录。");
        }

        var batchKey = ToBatchKey(sha.GetHashAndReset());
        await using var db = AppDatabase.CreateContext();
        var bundledAt = (await db.OuiMetas.AsNoTracking().FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false))?.BundledAtUtc
            ?? ReadBundledAt();
        await ApplyAsync(new OuiApplyRequest(
            db,
            rows,
            OuiSources.Ieee,
            bundledAt,
            $"{IeeeOuiUrl} ; {IeeeMamUrl} ; {IeeeOui36Url}",
            batchKey,
            progress,
            cancellationToken)).ConfigureAwait(false);
        Report(progress, 94, "正在加载厂商查找表…");
        await ReloadAsync().ConfigureAwait(false);
        Report(progress, 100, "OUI 更新完成");
        return Meta ?? throw new ScanException("OUI 更新后未能读取元数据。");
    }

    public static async Task CopyAsync(DatabaseConfig source, DatabaseConfig target)
    {
        await using var from = AppDatabase.CreateContext(source);
        await using var to = AppDatabase.CreateContext(target);
        var rows = await from.OuiEntries.AsNoTracking().ToListAsync().ConfigureAwait(false);
        var meta = await from.OuiMetas.AsNoTracking().FirstOrDefaultAsync().ConfigureAwait(false);
        if (rows.Count == 0)
        {
            return;
        }

        var copies = rows.Select(r => new OuiEntry
        {
            Prefix = r.Prefix,
            BitLength = r.BitLength,
            Registry = r.Registry,
            Vendor = r.Vendor
        }).ToList();
        var batchKey = string.IsNullOrWhiteSpace(meta?.BatchKey)
            ? HashRows(copies)
            : meta.BatchKey;
        await ApplyAsync(new OuiApplyRequest(
            to,
            copies,
            meta?.Source ?? OuiSources.Bundled,
            meta?.BundledAtUtc,
            meta?.Note,
            batchKey)).ConfigureAwait(false);
        await ReloadAsync().ConfigureAwait(false);
    }

    public static string Describe()
    {
        var meta = Meta;
        if (meta is null && CachedCount == 0)
        {
            return "尚未加载 OUI。";
        }

        var source = meta?.Source == OuiSources.Ieee ? "IEEE 在线更新" : "安装包内置";
        var bundled = meta?.BundledAtUtc is DateTime b
            ? b.ToLocalTime().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
            : "—";
        var imported = meta?.ImportedAtUtc is DateTime t
            ? t.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)
            : "—";
        var count = meta?.RecordCount > 0 ? meta.RecordCount : CachedCount;
        var batch = ShortBatch(meta?.BatchKey);
        var delta = "全量";
        if (!string.IsNullOrEmpty(meta?.PreviousBatchKey) && meta is not null)
        {
            delta = $"+{meta.AddedCount} / 改 {meta.UpdatedCount} / 删 {meta.RemovedCount}";
        }

        return $"内置登记 {bundled}。数据库 {imported} 写入 {count} 条（{source}）。批次 {batch}（{delta}）。之后只维护当前库中的 OUI。IEEE 没有官方批次号，本程序用三份登记文件的 SHA256 作为批次。";
    }

    private static async Task ApplyAsync(OuiApplyRequest apply)
    {
        Report(apply.Progress, 52, "正在比对当前库中的 OUI 批次…");
        var current = await apply.Db.OuiMetas.AsNoTracking()
            .FirstOrDefaultAsync(apply.CancellationToken).ConfigureAwait(false);
        if (string.Equals(current?.BatchKey, apply.BatchKey, StringComparison.OrdinalIgnoreCase)
            && await apply.Db.OuiEntries.AnyAsync(apply.CancellationToken).ConfigureAwait(false))
        {
            Report(apply.Progress, 90, "IEEE 批次未变，正在更新元数据…");
            await TouchMetaAsync(apply.Db, apply.Source, apply.BundledAt, apply.Note).ConfigureAwait(false);
            return;
        }

        Report(apply.Progress, 54, "正在读取当前库中的 OUI…");
        var existing = await LoadExistingAsync(apply.Db).ConfigureAwait(false);
        Report(apply.Progress, 58, $"已读取 {existing.Count} 条现有记录，正在计算增量…");
        var incoming = new Dictionary<string, OuiEntry>(apply.Rows.Count, StringComparer.OrdinalIgnoreCase);
        foreach (var row in apply.Rows)
        {
            incoming[row.Prefix] = row;
        }

        var added = new List<OuiEntry>();
        var updated = new List<OuiEntry>();
        foreach (var row in incoming.Values)
        {
            if (!existing.TryGetValue(row.Prefix, out var was))
            {
                added.Add(row);
                continue;
            }

            if (was.BitLength != row.BitLength
                || !string.Equals(was.Vendor, row.Vendor, StringComparison.Ordinal)
                || !string.Equals(was.Registry, row.Registry, StringComparison.OrdinalIgnoreCase))
            {
                updated.Add(row);
            }
        }

        var removed = existing.Keys.Where(prefix => !incoming.ContainsKey(prefix)).ToList();
        Report(apply.Progress, 62, $"增量：新增 {added.Count} / 改 {updated.Count} / 删 {removed.Count}");

        apply.Db.ChangeTracker.Clear();
        apply.Db.ChangeTracker.AutoDetectChangesEnabled = false;
        try
        {
            await DeleteByPrefixAsync(apply.Db, removed, apply.Progress, apply.CancellationToken).ConfigureAwait(false);
            await UpdateByPrefixAsync(apply.Db, updated, apply.BatchKey, apply.Progress, apply.CancellationToken)
                .ConfigureAwait(false);
            await InsertAsync(apply.Db, added, apply.BatchKey, apply.Progress, apply.CancellationToken).ConfigureAwait(false);
        }
        finally
        {
            apply.Db.ChangeTracker.AutoDetectChangesEnabled = true;
        }

        Report(apply.Progress, 92, "正在写入批次元数据…");
        await UpsertMetaAsync(new OuiMetaUpsertRequest(
            apply.Db,
            apply.Source,
            apply.BundledAt,
            apply.Note,
            apply.BatchKey,
            current?.BatchKey,
            incoming.Count,
            added.Count,
            updated.Count,
            removed.Count)).ConfigureAwait(false);
    }

    private static async Task TouchMetaAsync(
        NetsDbContext db,
        string source,
        DateTime? bundledAt,
        string? note)
    {
        var tracked = await db.OuiMetas.FirstOrDefaultAsync(x => x.Id == 1).ConfigureAwait(false);
        if (tracked is null)
        {
            return;
        }

        tracked.Source = source;
        tracked.BundledAtUtc = bundledAt ?? tracked.BundledAtUtc;
        tracked.ImportedAtUtc = DateTime.UtcNow;
        tracked.Note = note;
        await db.SaveChangesAsync().ConfigureAwait(false);
    }

    private static async Task UpsertMetaAsync(OuiMetaUpsertRequest upsert)
    {
        upsert.Db.ChangeTracker.Clear();
        var meta = await upsert.Db.OuiMetas.FirstOrDefaultAsync(x => x.Id == 1).ConfigureAwait(false);
        if (meta is null)
        {
            meta = new OuiMeta { Id = 1 };
            upsert.Db.OuiMetas.Add(meta);
        }

        meta.Source = upsert.Source;
        meta.BundledAtUtc = upsert.BundledAt;
        meta.ImportedAtUtc = DateTime.UtcNow;
        meta.RecordCount = upsert.RecordCount;
        meta.Note = upsert.Note;
        meta.PreviousBatchKey = upsert.PreviousBatchKey;
        meta.BatchKey = upsert.BatchKey;
        meta.AddedCount = upsert.Added;
        meta.UpdatedCount = upsert.Updated;
        meta.RemovedCount = upsert.Removed;
        await upsert.Db.SaveChangesAsync().ConfigureAwait(false);
    }

    private static async Task DeleteByPrefixAsync(
        NetsDbContext db,
        List<string> prefixes,
        IProgress<ScanProgress>? progress,
        CancellationToken cancellationToken)
    {
        if (prefixes.Count == 0)
        {
            Report(progress, 68, "没有需要删除的前缀");
            return;
        }

        for (var i = 0; i < prefixes.Count; i += WriteChunk)
        {
            var chunk = prefixes.Skip(i).Take(WriteChunk).ToList();
            await db.OuiEntries.Where(x => chunk.Contains(x.Prefix)).ExecuteDeleteAsync(cancellationToken)
                .ConfigureAwait(false);
            var done = Math.Min(i + chunk.Count, prefixes.Count);
            Report(progress, Lerp(62, 70, done / (double)prefixes.Count), $"正在删除失效前缀 {done}/{prefixes.Count}…");
        }
    }

    private static async Task UpdateByPrefixAsync(
        NetsDbContext db,
        List<OuiEntry> rows,
        string batchKey,
        IProgress<ScanProgress>? progress,
        CancellationToken cancellationToken)
    {
        if (rows.Count == 0)
        {
            Report(progress, 80, "没有需要更新的厂商");
            return;
        }

        for (var i = 0; i < rows.Count; i += WriteChunk)
        {
            var chunk = rows.Skip(i).Take(WriteChunk).ToList();
            var keys = chunk.Select(x => x.Prefix).ToList();
            var entities = await db.OuiEntries.Where(x => keys.Contains(x.Prefix)).ToListAsync(cancellationToken)
                .ConfigureAwait(false);
            var incoming = chunk.ToDictionary(x => x.Prefix, StringComparer.OrdinalIgnoreCase);
            foreach (var entity in entities)
            {
                if (!incoming.TryGetValue(entity.Prefix, out var row))
                {
                    continue;
                }

                entity.BitLength = row.BitLength;
                entity.Registry = row.Registry;
                entity.Vendor = row.Vendor;
                entity.BatchKey = batchKey;
            }

            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            db.ChangeTracker.Clear();
            var done = Math.Min(i + chunk.Count, rows.Count);
            Report(progress, Lerp(70, 82, done / (double)rows.Count), $"正在更新厂商 {done}/{rows.Count}…");
        }
    }

    private static async Task InsertAsync(
        NetsDbContext db,
        List<OuiEntry> rows,
        string batchKey,
        IProgress<ScanProgress>? progress,
        CancellationToken cancellationToken)
    {
        if (rows.Count == 0)
        {
            Report(progress, 92, "没有需要新增的前缀");
            return;
        }

        const int insertChunk = 2000;
        for (var i = 0; i < rows.Count; i += insertChunk)
        {
            var chunk = rows.Skip(i).Take(insertChunk).Select(r => new OuiEntry
            {
                Prefix = r.Prefix,
                BitLength = r.BitLength,
                Registry = r.Registry,
                Vendor = r.Vendor,
                BatchKey = batchKey
            });
            db.OuiEntries.AddRange(chunk);
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            db.ChangeTracker.Clear();
            var done = Math.Min(i + insertChunk, rows.Count);
            Report(progress, Lerp(82, 92, done / (double)rows.Count), $"正在写入新增前缀 {done}/{rows.Count}…");
        }
    }

    private static async Task<OuiLookupTable> LoadTableAsync(NetsDbContext db)
    {
        var builder = new OuiLookupTable.Builder();
        var connection = db.Database.GetDbConnection();
        var opened = connection.State != ConnectionState.Open;
        if (opened)
        {
            await db.Database.OpenConnectionAsync().ConfigureAwait(false);
        }

        try
        {
            await using var cmd = connection.CreateCommand();
            cmd.CommandText = $"SELECT Prefix, BitLength, Vendor FROM {NetsTableNames.OuiEntry}";
            await using var reader = await cmd.ExecuteReaderAsync().ConfigureAwait(false);
            while (await reader.ReadAsync().ConfigureAwait(false))
            {
                builder.Add(reader.GetString(0), Convert.ToInt32(reader.GetValue(1), CultureInfo.InvariantCulture), reader.GetString(2));
            }

            return builder.ToTable();
        }
        finally
        {
            if (opened)
            {
                await db.Database.CloseConnectionAsync().ConfigureAwait(false);
            }
        }
    }

    private static async Task<Dictionary<string, (int BitLength, string Registry, string Vendor)>> LoadExistingAsync(NetsDbContext db)
    {
        var map = new Dictionary<string, (int BitLength, string Registry, string Vendor)>(StringComparer.OrdinalIgnoreCase);
        var connection = db.Database.GetDbConnection();
        var opened = connection.State != ConnectionState.Open;
        if (opened)
        {
            await db.Database.OpenConnectionAsync().ConfigureAwait(false);
        }

        try
        {
            await using var cmd = connection.CreateCommand();
            cmd.CommandText = $"SELECT Prefix, BitLength, Registry, Vendor FROM {NetsTableNames.OuiEntry}";
            await using var reader = await cmd.ExecuteReaderAsync().ConfigureAwait(false);
            while (await reader.ReadAsync().ConfigureAwait(false))
            {
                map[reader.GetString(0)] = (
                    Convert.ToInt32(reader.GetValue(1), CultureInfo.InvariantCulture),
                    reader.GetString(2),
                    reader.GetString(3));
            }

            return map;
        }
        finally
        {
            if (opened)
            {
                await db.Database.CloseConnectionAsync().ConfigureAwait(false);
            }
        }
    }

    private static (List<OuiEntry> Rows, string BatchKey) LoadBundled()
    {
        string[] names = ["oui.csv", "mam.csv", "oui36.csv"];
        using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var map = new Dictionary<string, OuiEntry>(StringComparer.OrdinalIgnoreCase);
        foreach (var name in names)
        {
            var path = Path.Combine(BundledDirectory, name);
            if (!File.Exists(path))
            {
                continue;
            }

            var bytes = File.ReadAllBytes(path);
            sha.AppendData(bytes);
            sha.AppendData([0]);
            using var reader = new StreamReader(new MemoryStream(bytes), Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
            foreach (var row in OuiCsvParser.Enumerate(reader))
            {
                map[row.Prefix] = row;
            }
        }

        return (InternVendors(map.Values), ToBatchKey(sha.GetHashAndReset()));
    }

    private static List<OuiEntry> InternVendors(IEnumerable<OuiEntry> rows)
    {
        var intern = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in rows)
        {
            if (intern.TryGetValue(row.Vendor, out var shared))
            {
                row.Vendor = shared;
            }
            else
            {
                intern[row.Vendor] = row.Vendor;
            }
        }

        return rows as List<OuiEntry> ?? rows.ToList();
    }

    private static string HashRows(IEnumerable<OuiEntry> rows)
    {
        using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (var row in rows.OrderBy(r => r.Prefix, StringComparer.OrdinalIgnoreCase))
        {
            sha.AppendData(Encoding.UTF8.GetBytes(row.Prefix));
            sha.AppendData([0]);
            sha.AppendData(Encoding.UTF8.GetBytes(row.Vendor));
            sha.AppendData([10]);
        }

        return ToBatchKey(sha.GetHashAndReset());
    }

    private static string ToBatchKey(byte[] hash) => Convert.ToHexString(hash).ToLowerInvariant();

    private static async Task<byte[]> DownloadIeeeAsync(
        HttpClient http,
        string url,
        string label,
        double percentStart,
        double percentEnd,
        IProgress<ScanProgress>? progress,
        CancellationToken cancellationToken)
    {
        Report(progress, percentStart, $"正在连接 IEEE {label}…", indeterminate: true);
        using var response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw new ScanException($"IEEE 下载失败（{(int)response.StatusCode}）：{url}");
        }

        var total = response.Content.Headers.ContentLength;
        await using var input = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var output = new MemoryStream(total is > 0 and <= int.MaxValue ? (int)total.Value : 256 * 1024);
        var buffer = new byte[64 * 1024];
        long read = 0;
        long lastReport = 0;
        int n;
        while ((n = await input.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken).ConfigureAwait(false)) > 0)
        {
            await output.WriteAsync(buffer.AsMemory(0, n), cancellationToken).ConfigureAwait(false);
            read += n;
            if (read - lastReport < 256 * 1024 && (total is null || read < total.Value))
            {
                continue;
            }

            lastReport = read;
            if (total > 0)
            {
                Report(
                    progress,
                    Lerp(percentStart, percentEnd, read / (double)total.Value),
                    $"正在下载 {label}… {FormatSize(read)} / {FormatSize(total.Value)}");
            }
            else
            {
                Report(progress, percentStart, $"正在下载 {label}… 已接收 {FormatSize(read)}", indeterminate: true);
            }
        }

        Report(progress, percentEnd, $"已下载 {label}（{FormatSize(read)}）");
        return output.ToArray();
    }

    private static void Report(IProgress<ScanProgress>? progress, double percent, string message, bool indeterminate = false)
    {
        var value = (int)Math.Clamp(Math.Round(percent), 0, 100);
        progress?.Report(new ScanProgress(value, 100, message, indeterminate));
    }

    private static double Lerp(double start, double end, double t) =>
        start + (end - start) * Math.Clamp(t, 0, 1);

    private static string FormatSize(long bytes)
    {
        if (bytes < 1024)
        {
            return $"{bytes} B";
        }

        if (bytes < 1024 * 1024)
        {
            return $"{bytes / 1024.0:0.0} KB";
        }

        return $"{bytes / (1024.0 * 1024):0.00} MB";
    }

    private static string ShortBatch(string? batchKey)
    {
        if (string.IsNullOrEmpty(batchKey))
        {
            return "—";
        }

        return batchKey.Length <= 8 ? batchKey : batchKey[..8];
    }

    private static string HeaderText(byte[] bytes)
    {
        var length = Math.Min(bytes.Length, 256);
        return Encoding.UTF8.GetString(bytes, 0, length);
    }

    private static DateTime? ReadBundledAt()
    {
        var path = Path.Combine(BundledDirectory, "built.txt");
        if (!File.Exists(path))
        {
            return null;
        }

        var line = File.ReadLines(path).FirstOrDefault();
        return DateTime.TryParse(
            line,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
            out var value)
            ? value
            : null;
    }

    private sealed record OuiApplyRequest(
        NetsDbContext Db,
        IReadOnlyList<OuiEntry> Rows,
        string Source,
        DateTime? BundledAt,
        string? Note,
        string BatchKey,
        IProgress<ScanProgress>? Progress = null,
        CancellationToken CancellationToken = default);

    private sealed record OuiMetaUpsertRequest(
        NetsDbContext Db,
        string Source,
        DateTime? BundledAt,
        string? Note,
        string BatchKey,
        string? PreviousBatchKey,
        int RecordCount,
        int Added,
        int Updated,
        int Removed);
}
