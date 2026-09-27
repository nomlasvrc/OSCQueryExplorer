using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Makaretu.Dns;
using OSCQueryExplorer.Core.Models;

namespace OSCQueryExplorer.Protocol.Discovery;

public sealed class MdnsOscQueryDiscovery : IDisposable
{
    private const string OscQueryServiceType = "_oscjson._tcp";
    private const string OscServiceType = "_osc._udp";
    private readonly MulticastService _mdns = new();
    private readonly ServiceDiscovery _discovery;
    private readonly ConcurrentDictionary<string, SRVRecord> _servers = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, IPAddress> _addresses = new(StringComparer.OrdinalIgnoreCase);
    private readonly CancellationTokenSource _lifetimeCts = new();
    private ServiceProfile? _advertisedQuery;
    private ServiceProfile? _advertisedOsc;
    private bool _started;
    public event EventHandler<DiscoveredService>? ServiceFound;
    public event EventHandler<string>? DiscoveryWarning;

    public MdnsOscQueryDiscovery()
    {
        _discovery = new ServiceDiscovery(_mdns);
        _discovery.ServiceInstanceDiscovered += (_, e) => _mdns.SendQuery(e.ServiceInstanceName, type: DnsType.SRV);
        _mdns.AnswerReceived += (_, e) =>
        {
            foreach (var server in e.Message.Answers.Concat(e.Message.AdditionalRecords).OfType<SRVRecord>()) _servers[Key(server.Name)] = server;
            foreach (var address in e.Message.Answers.Concat(e.Message.AdditionalRecords).OfType<AddressRecord>()) _addresses[Key(address.Name)] = address.Address;
            PublishResolved();
        };
    }

    public void Start()
    {
        if (_started) return;
        _mdns.Start(); _started = true; Refresh();
        _ = RefreshAfterStartupAsync(_lifetimeCts.Token);
    }

    private async Task RefreshAfterStartupAsync(CancellationToken cancellationToken)
    {
        try
        {
            // Multicast sockets may not be ready for the query issued immediately after Start().
            // Retry during startup so services are shown without requiring a manual refresh.
            foreach (var delay in new[] { 250, 1000 })
            {
                await Task.Delay(delay, cancellationToken);
                Refresh();
            }
        }
        catch (OperationCanceledException) { }
    }

    public void Refresh()
    {
        try { if (!_started) Start(); else _discovery.QueryServiceInstances(OscQueryServiceType); }
        catch (Exception ex) { DiscoveryWarning?.Invoke(this, ex.Message); }
    }

    public void Advertise(int httpPort, int oscPort, bool publishToLan)
    {
        if (httpPort <= 0) throw new ArgumentOutOfRangeException(nameof(httpPort));
        if (oscPort <= 0) throw new ArgumentOutOfRangeException(nameof(oscPort));
        if (!_started) Start();
        StopAdvertising();
        var addresses = publishToLan
            ? MulticastService.GetIPAddresses().Where(x => x.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(x)).ToArray()
            : [IPAddress.Loopback];
        if (addresses.Length == 0) addresses = [IPAddress.Loopback];
        _advertisedQuery = new ServiceProfile("OSCQuery Explorer", OscQueryServiceType, checked((ushort)httpPort), addresses);
        _advertisedQuery.AddProperty("version", "1");
        _advertisedOsc = new ServiceProfile("OSCQuery Explorer", OscServiceType, checked((ushort)oscPort), addresses);
        _discovery.Advertise(_advertisedQuery);
        _discovery.Advertise(_advertisedOsc);
        _discovery.Announce(_advertisedQuery);
        _discovery.Announce(_advertisedOsc);
    }

    public void StopAdvertising()
    {
        if (_advertisedQuery is not null) _discovery.Unadvertise(_advertisedQuery);
        if (_advertisedOsc is not null) _discovery.Unadvertise(_advertisedOsc);
        _advertisedQuery = null;
        _advertisedOsc = null;
    }

    private void PublishResolved()
    {
        foreach (var server in _servers.Values)
        {
            var fqdn = server.Name.ToString().TrimEnd('.');
            var suffix = "." + OscQueryServiceType + ".local";
            // A multicast response commonly contains related _osc._udp SRV records.
            // They are OSC endpoints, not OSCQuery HTTP services.
            if (!fqdn.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)) continue;
            if (!_addresses.TryGetValue(Key(server.Target), out var address)) { _mdns.SendQuery(server.Target, type: DnsType.A); continue; }
            var encodedName = fqdn.EndsWith(suffix, StringComparison.OrdinalIgnoreCase) ? fqdn[..^suffix.Length] : fqdn;
            var name = DecodeDnsName(encodedName);
            if (name.Equals("OSCQuery Explorer", StringComparison.OrdinalIgnoreCase)) continue;
            var host = server.Target.ToString().TrimEnd('.');
            ServiceFound?.Invoke(this, new DiscoveredService(new ServiceIdentity(name, host), new UriBuilder("http", address.ToString(), server.Port).Uri, DateTimeOffset.UtcNow));
        }
    }

    private static string DecodeDnsName(string value)
    {
        var result = new StringBuilder(value.Length);
        for (var index = 0; index < value.Length; index++)
        {
            if (value[index] == '\\' && index + 3 < value.Length
                && char.IsAsciiDigit(value[index + 1])
                && char.IsAsciiDigit(value[index + 2])
                && char.IsAsciiDigit(value[index + 3])
                && int.TryParse(value.AsSpan(index + 1, 3), out var code))
            {
                result.Append((char)code);
                index += 3;
                continue;
            }

            result.Append(value[index]);
        }
        return result.ToString();
    }

    private static string Key(DomainName name) => name.ToString().TrimEnd('.');
    public void Dispose() { _lifetimeCts.Cancel(); StopAdvertising(); _discovery.Dispose(); _mdns.Stop(); _mdns.Dispose(); _lifetimeCts.Dispose(); }
}
