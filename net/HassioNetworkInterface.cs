using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq.Expressions;
using System.Net;
using System.Net.NetworkInformation;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace AzureDynDns
{
    class HassioNetworkInterface : NetworkInterface
    {
        public HassioNetworkInterface()
        {
        }

        public string Interface { get; set; }
        public Ipv4 Ipv4 { get; set; }
        public Ipv6 Ipv6 { get; set; }

        public override IPInterfaceProperties GetIPProperties()
        {
            return new HassioIPInterfaceProperties(this);
        }

        public override string Name => Interface;

        public static new IEnumerable<HassioNetworkInterface> GetAllNetworkInterfaces()
        {
            var process = Process.Start(new ProcessStartInfo
            {
                FileName = "/usr/bin/ha",
                Arguments = "network info",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                CreateNoWindow = true,
            });
            var yaml = process.StandardOutput.ReadToEnd();
            process.WaitForExit();
            var deserializer = new DeserializerBuilder()
                .WithNamingConvention(CamelCaseNamingConvention.Instance)
                .IgnoreUnmatchedProperties()
                .Build();
            var info = deserializer.Deserialize<HassioNetworkInfo>(yaml);
            return info.Interfaces;
        }
    }

    class Ipv4
    {
        public IList<string> Address { get; set; }
        public string Gateway { get; set; }
        public string Method { get; set; }
        public IList<string> Nameservers { get; set; }
        public bool Ready { get; set; }
    }

    class Ipv6
    {
        public IList<string> Address { get; set; }
        public string Gateway { get; set; }
        public string Method { get; set; }
        public IList<string> Nameservers { get; set; }
        public bool Ready { get; set; }
    }

    class HassioNetworkInfo
    {
        public IList<HassioNetworkInterface> Interfaces { get; set; }
    }

    class HassioIPInterfaceProperties : IPInterfaceProperties
    {
        private readonly HassioNetworkInterface @interface;

        public HassioIPInterfaceProperties(HassioNetworkInterface @interface)
        {
            this.@interface = @interface;
        }
        public override IPAddressInformationCollection AnycastAddresses => throw new NotImplementedException();

        public override IPAddressCollection DhcpServerAddresses => throw new NotImplementedException();

        public override IPAddressCollection DnsAddresses => throw new NotImplementedException();

        public override string DnsSuffix => throw new NotImplementedException();

        public override GatewayIPAddressInformationCollection GatewayAddresses => throw new NotImplementedException();

        public override bool IsDnsEnabled => throw new NotImplementedException();

        public override bool IsDynamicDnsEnabled => throw new NotImplementedException();

        public override MulticastIPAddressInformationCollection MulticastAddresses => throw new NotImplementedException();

        public override UnicastIPAddressInformationCollection UnicastAddresses
        {
            get
            {
                var collection = new HassioUnicastIPAddressInformationCollection();
                foreach (var address in this.@interface.Ipv4.Address)
                {
                    collection.Add(new HassioUnicastIPAddressInformation(IPAddress.Parse(address.Split('/')[0])));
                }
                foreach (var address in this.@interface.Ipv6.Address)
                {
                    collection.Add(new HassioUnicastIPAddressInformation(IPAddress.Parse(address.Split('/')[0])));
                }
                return collection;
            }
        }

        public override IPAddressCollection WinsServersAddresses => throw new NotImplementedException();

        public override IPv4InterfaceProperties GetIPv4Properties()
        {
            throw new NotImplementedException();
        }

        public override IPv6InterfaceProperties GetIPv6Properties()
        {
            throw new NotImplementedException();
        }
    }

    class HassioUnicastIPAddressInformation : UnicastIPAddressInformation
    {
        public HassioUnicastIPAddressInformation(IPAddress address)
        {
            Address = address;
        }

        public override long AddressPreferredLifetime => throw new NotImplementedException();

        public override long AddressValidLifetime => throw new NotImplementedException();

        public override long DhcpLeaseLifetime => throw new NotImplementedException();

        public override DuplicateAddressDetectionState DuplicateAddressDetectionState => throw new NotImplementedException();

        public override IPAddress IPv4Mask => throw new NotImplementedException();

        public override PrefixOrigin PrefixOrigin => throw new NotImplementedException();

        public override SuffixOrigin SuffixOrigin => throw new NotImplementedException();

        public override IPAddress Address { get; }

        public override bool IsDnsEligible => throw new NotImplementedException();

        public override bool IsTransient => throw new NotImplementedException();
    }

    class HassioUnicastIPAddressInformationCollection : UnicastIPAddressInformationCollection, ICollection<UnicastIPAddressInformation>
    {
        protected internal HassioUnicastIPAddressInformationCollection()
        {
        }

        private readonly List<UnicastIPAddressInformation> _addresses =
            new List<UnicastIPAddressInformation>();

        public override void CopyTo(UnicastIPAddressInformation[] array, int offset)
        {
            _addresses.CopyTo(array, offset);
        }

        public override int Count
        {
            get
            {
                return _addresses.Count;
            }
        }

        public override bool IsReadOnly
        {
            get
            {
                return true;
            }
        }

        public override void Add(UnicastIPAddressInformation address)
        {
            _addresses.Add(address);
        }

        public override bool Contains(UnicastIPAddressInformation address)
        {
            return _addresses.Contains(address);
        }

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()
        {
            return this.GetEnumerator();
        }

        public override IEnumerator<UnicastIPAddressInformation> GetEnumerator()
        {
            return _addresses.GetEnumerator();
        }

        public override UnicastIPAddressInformation this[int index]
        {
            get
            {
                return _addresses[index];
            }
        }

        public override bool Remove(UnicastIPAddressInformation address)
        {
            throw new NotSupportedException();
        }

        public override void Clear()
        {
            throw new NotSupportedException();
        }
    }
}
