using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using CommandLine;
using Microsoft.Azure.Management.Dns;
using Microsoft.Azure.Management.Dns.Models;
using Microsoft.Rest.Azure.Authentication;

namespace AzureDynDns
{
    enum InterfaceAddressFamily
    {
        Any,
        IPv4,
        IPv6,
    }

    class Program
    {
        public class Options
        {
            [JsonIgnore]
            [Option('f', "config-file", HelpText = "Path to configuration file")]
            public string ConfigFile { get; set; }
            [JsonPropertyName("resourceGroup")]
            [Option('g', "resource-group", HelpText = "Azure resource group where Azure DNS is located")]
            public string ResourceGroup { get; set; }
            [JsonPropertyName("zoneName")]
            [Option('z', "zone", HelpText = "Azure DNS zone name")]
            public string Zone { get; set; }
            [JsonPropertyName("recordName")]
            [Option('r', "record", HelpText = "DNS record name to be created/updated")]
            public string Record { get; set; }
            [JsonPropertyName("subscriptionId")]
            [Option('s', "subscription-id", HelpText = "Azure subscription ID")]
            public string SubscriptionId { get; set; }
            [JsonPropertyName("tenantId")]
            [Option('t', "tenant-id", HelpText = "Azure tenant ID (or set AZURE_TENANT_ID)")]
            public string TenantId { get; set; }
            [JsonPropertyName("clientId")]
            [Option('c', "client-id", HelpText = "Azure service principal client ID (or set AZURE_CLIENT_ID)")]
            public string ClientId { get; set; }
            [JsonPropertyName("clientSecret")]
            [Option('x', "client-secret", HelpText = "Azure service principal client secret (or set AZURE_CLIENT_SECRET)")]
            public string ClientSecret { get; set; }
            [JsonPropertyName("interfaceName")]
            [Option('i', "interface-name", HelpText = "The network interface to obtain the IP address from. If emtpy, obtain the address from ipconfig.me.")]
            public IList<string> InterfaceNames { get; set; }
            [JsonPropertyName("iterfaceAddressFamilies")]
            [Option('a', "interface-address-family", HelpText = "When the interface-name parameter is specified, use this parameter to limit the address family obtained from the interface. Possible values are IPv4, IPv6 and Any.", Required = false)]
            public IList<InterfaceAddressFamily> InterfaceAddressFamilies { get; set; }
            [JsonPropertyName("ttl")]
            [Option('l', "TTL", HelpText = "A Time-to-live value for the DNS records.", Default = 3600)]
            public long TTL { get; set; }
        }
        public static async Task Main(string[] args)
        {
            await Parser.Default.ParseArguments<Options>(args).WithParsedAsync(async (o) => await UpdateDNS(o));
        }

        public static async Task UpdateDNS(Options options)
        {
            if (!string.IsNullOrEmpty(options.ConfigFile))
            {
                var configJson = File.ReadAllText(options.ConfigFile);
                options = JsonSerializer.Deserialize<Options>(configJson);
            }

            var aRecordSet = new RecordSet();
            aRecordSet.ARecords = new List<ARecord>();
            var aaaaRecordSet = new RecordSet();
            aaaaRecordSet.AaaaRecords = new List<AaaaRecord>();
            if (options.InterfaceNames.Count > 0 && options.InterfaceAddressFamilies.Count != options.InterfaceNames.Count && options.InterfaceAddressFamilies.Count != 0)
                throw new Exception("The number of --interface-address-family parameters must be equal to the number of --interface-name parameters. Or be 0 to default to Any.");
            var interfaces = new Dictionary<string, InterfaceAddressFamily>();
            for (int i = 0; i < options.InterfaceNames.Count; i++)
            {
                interfaces.Add(options.InterfaceNames[i], options.InterfaceAddressFamilies.Count == 0 ? InterfaceAddressFamily.Any : options.InterfaceAddressFamilies[i]);
            }
            await foreach (var ip in GetPublicIPs(interfaces))
            {
                switch (ip.AddressFamily)
                {
                    case AddressFamily.InterNetwork:
                        aRecordSet.ARecords.Add(new ARecord(ip.ToString()));
                        break;
                    case AddressFamily.InterNetworkV6:
                        aaaaRecordSet.AaaaRecords.Add(new AaaaRecord(ip.ToString()));
                        break;
                    default:
                        continue;
                }
            }

            if (aRecordSet.ARecords.Count == 0 && aaaaRecordSet.AaaaRecords.Count == 0)
                throw new Exception("No IP address found.");

            SetMetadata(aRecordSet);
            SetMetadata(aaaaRecordSet);
            aRecordSet.TTL = options.TTL;
            aaaaRecordSet.TTL = options.TTL;

            var (tenantId, clientId, clientSecret) = GetCredentialInfo(options);
            var creds = await ApplicationTokenProvider.LoginSilentAsync(tenantId, clientId, clientSecret);
            var dnsClient = new DnsManagementClient(creds);
            dnsClient.SubscriptionId = options.SubscriptionId;
            if (aRecordSet.ARecords.Count > 0)
            {
                Console.WriteLine(JsonSerializer.Serialize(await dnsClient.RecordSets.CreateOrUpdateAsync(options.ResourceGroup, options.Zone, options.Record, RecordType.A, aRecordSet)));
            }
            if (aaaaRecordSet.AaaaRecords.Count > 0)
            {
                Console.WriteLine(JsonSerializer.Serialize(await dnsClient.RecordSets.CreateOrUpdateAsync(options.ResourceGroup, options.Zone, options.Record, RecordType.AAAA, aaaaRecordSet)));
            }
        }

        private static void SetMetadata(RecordSet recordSet)
        {
            recordSet.Metadata = new Dictionary<string, string>
            {
                { "createdBy", "Azure-DynDns (.NET)" },
                { "updated", DateTime.Now.ToString() }
            };
        }

        public static async IAsyncEnumerable<IPAddress> GetPublicIPs(Dictionary<string, InterfaceAddressFamily> interfaces)
        {
            if (interfaces.Count == 0)
            {
                var client = new HttpClient();
                yield return IPAddress.Parse(await client.GetStringAsync("https://ifconfig.me"));
            }

            foreach (var networkInterface in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (!interfaces.TryGetValue(networkInterface.Name, out var requestedAddressFamily))
                    continue;
                foreach (var address in networkInterface.GetIPProperties().UnicastAddresses)
                {
                    if (!address.Address.IsIPv6Teredo && !address.Address.IsIPv4MappedToIPv6 && !address.Address.IsIPv6SiteLocal && !address.Address.IsIPv6LinkLocal && !address.Address.IsIPv6Multicast && !address.Address.IsIPv6SiteLocal && !address.IsTransient && address.IsDnsEligible && !IsUlaAddress(address.Address))
                    {
                        switch (requestedAddressFamily)
                        {
                            case InterfaceAddressFamily.Any:
                                yield return address.Address;
                                break;
                            case InterfaceAddressFamily.IPv4:
                                if (address.Address.AddressFamily == AddressFamily.InterNetwork)
                                    yield return address.Address;
                                break;
                            case InterfaceAddressFamily.IPv6:
                                if (address.Address.AddressFamily == AddressFamily.InterNetworkV6)
                                    yield return address.Address;
                                break;
                            default:
                                break;
                        }
                    }

                }
            }
        }

        public static (string, string, string) GetCredentialInfo(Options options)
        {
            var tenantId = options.TenantId ?? Environment.GetEnvironmentVariable("AZURE_TENANT_ID");
            var clientId = options.ClientId ?? Environment.GetEnvironmentVariable("AZURE_CLIENT_ID");
            var clientSecret = options.ClientSecret ?? Environment.GetEnvironmentVariable("AZURE_CLIENT_SECRET");
            return (tenantId, clientId, clientSecret);
        }

        private static bool IsUlaAddress(IPAddress address)
        {
            var addressString = address.ToString();
            return addressString.StartsWith("fc00:") || addressString.StartsWith("fd00:");
        }
    }
}
