using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

namespace Tools
{
    public class SubnetScanner
    {

        public IPAddress[] LocalIPAddresses { get; private set; }

        public int Timeout { get; set; }

        public SubnetScanner() 
        {
            LocalIPAddresses = GetLocalIPAddresses().ToArray();
            Timeout = 50;
        }

        public IEnumerable<IPAddress> AliveHosts()
        {
            List<IPAddress> hosts = new List<IPAddress>();

            Parallel.For(0, LocalIPAddresses.Length, (i) =>
            {
                hosts.AddRange(AliveHosts(LocalIPAddresses[i]));
            });
            return hosts;
        }

        public IEnumerable<IPAddress> AliveHosts(IPAddress localIP)
        {
            List<IPAddress> hosts = new List<IPAddress>();
            AliveHosts(localIP, hosts.Add);
            return hosts;
        }

        public void AliveHosts(IPAddress localIP, Action<IPAddress> action)
        {
            string[] split = localIP.ToString().Split(".");
            IEnumerable<string> firstThree = split.Take(3);

            Parallel.For(0, 255, (i) =>
            {
                IEnumerable<string> thisIP = firstThree.Append(i.ToString());
                Ping ping = new Ping();

                IPAddress copy = IPAddress.Parse(string.Join('.', thisIP));
                if (ping.Send(copy, Timeout).Status == IPStatus.Success)
                {
                    action(copy);
                }
            });
        }

        public IEnumerable<int> OpenPorts(IPAddress address, params int[] portsToCheck)
        {
            List<int> ports = new List<int>();
            Parallel.For(0, portsToCheck.Length, (i) =>
            {
                int port = portsToCheck[i];
                using (TcpClient tcpClient = new TcpClient())
                {
                    try
                    {
                        tcpClient.ReceiveTimeout = Timeout;
                        tcpClient.SendTimeout = Timeout;
                        tcpClient.Connect(address, port);
                        ports.Add(port);
                    }
                    catch 
                    {
                    }
                }
            });
            return ports;
        }

        public struct OpenPortsStruct
        {
            public IPAddress Address;
            public int[] Ports;

            public override string ToString()
            {
                return Address.ToString() + ":" + string.Join(", ",Ports);
            }
        }


        public IEnumerable<OpenPortsStruct> AliveWithOpenPorts(params int[] portsToCheck)
        {
            List<OpenPortsStruct> hosts = new List<OpenPortsStruct>();

            Parallel.For(0, LocalIPAddresses.Length, (i) =>
            {
                hosts.AddRange(AliveWithOpenPorts(LocalIPAddresses[i], portsToCheck));
            });
            return hosts;
        }

        public IEnumerable<OpenPortsStruct> AliveWithOpenPorts(IPAddress localIP, params int[] portsToCheck)
        {
            List<OpenPortsStruct> ports = new List<OpenPortsStruct>();

            AliveHosts(localIP, (aliveHost) =>
            {
                int[] open = OpenPorts(aliveHost, portsToCheck).ToArray();
                if (open.Any())
                {
                    OpenPortsStruct openPorts = new OpenPortsStruct() { Address = aliveHost, Ports = open };
                    ports.Add(openPorts); 
                }
            });
            return ports;
        }


        public static IEnumerable<IPAddress> GetLocalIPAddresses()
        {
            var host = Dns.GetHostEntry(Dns.GetHostName());
            foreach (var ip in host.AddressList)
            {
                if (ip.AddressFamily == AddressFamily.InterNetwork && IsLocalIPV4(ip))
                {
                    yield return ip;
                }
            }
        }

        public static bool IsLocalIPV4(IPAddress address)
        {
            string ip = address.ToString();

            return (ip.StartsWith("10") || ip.StartsWith("172.16") || ip.StartsWith("192.168"));
        }
    }
}
