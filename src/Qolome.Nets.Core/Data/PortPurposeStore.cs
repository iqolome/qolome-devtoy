using Microsoft.EntityFrameworkCore;
using Qolome.Nets.Core.Models;
using Qolome.Nets.Core.Scanning;

namespace Qolome.Nets.Core.Data;

public sealed class PortPurposeStore
{
    private readonly Dictionary<(int Port, string Protocol), PortPurpose> _custom = new();
    private readonly object _gate = new();
    private bool _loaded;

    public async Task EnsureLoadedAsync()
    {
        if (_loaded)
        {
            return;
        }

        await ReloadAsync().ConfigureAwait(false);
    }

    public async Task ReloadAsync()
    {
        await using var db = AppDatabase.CreateContext();
        var rows = await db.PortPurposes.AsNoTracking().ToListAsync().ConfigureAwait(false);
        lock (_gate)
        {
            _custom.Clear();
            foreach (var row in rows)
            {
                _custom[(row.Port, PortPurposeCatalog.NormalizeProtocol(row.Protocol))] = row;
            }

            _loaded = true;
        }
    }

    public PortProfile Resolve(int port, string? protocol)
    {
        var proto = PortPurposeCatalog.NormalizeProtocol(protocol);
        var builtin = PortPurposeCatalog.Find(port, proto);
        PortPurpose? custom;
        lock (_gate)
        {
            _custom.TryGetValue((port, proto), out custom);
        }

        var service = NullIfEmpty(custom?.Service) ?? builtin?.Service ?? "";
        var description = NullIfEmpty(custom?.Description) ?? builtin?.Description ?? "";
        return new PortProfile(port, proto, service, description);
    }

    public void Apply(OpenPort port)
    {
        var profile = Resolve(port.Port, port.Protocol);
        if (!string.IsNullOrWhiteSpace(profile.Service))
        {
            port.Service = profile.Service;
        }

        port.Description = string.IsNullOrWhiteSpace(profile.Description) ? null : profile.Description;
    }

    public async Task UpsertAsync(int port, string protocol, string? service, string? description)
    {
        var proto = PortPurposeCatalog.NormalizeProtocol(protocol);
        await using var db = AppDatabase.CreateContext();
        var entity = await db.PortPurposes.FirstOrDefaultAsync(x => x.Port == port && x.Protocol == proto)
            .ConfigureAwait(false);
        if (entity is null)
        {
            entity = new PortPurpose { Port = port, Protocol = proto };
            db.PortPurposes.Add(entity);
        }

        entity.Service = NullIfEmpty(service);
        entity.Description = NullIfEmpty(description);
        await db.SaveChangesAsync().ConfigureAwait(false);
        await ReloadAsync().ConfigureAwait(false);
    }

    private static string? NullIfEmpty(string? value)
    {
        var text = value?.Trim();
        return string.IsNullOrWhiteSpace(text) ? null : text;
    }
}
