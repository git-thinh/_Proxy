using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;

namespace TcpForwarder
{
    class Program
    {
        static void Main(string[] args)
        {
            
            //127.0.0.1 12345 107.6.106.82 80

            string host_ip = "127.0.0.1", host_port = "12345",
                //ditributed_ip = "104.156.85.67", ditributed_port = "80";
                ditributed_ip = "127.0.0.1", ditributed_port = "9002";
                //ditributed_ip = "192.168.1.30", ditributed_port = "9002";

            new TcpForwarderSlim().Start(
                new IPEndPoint(IPAddress.Parse(host_ip), int.Parse(host_port)),
                new IPEndPoint(IPAddress.Parse(ditributed_ip), int.Parse(ditributed_port)));

            Console.WriteLine("........finish............");
            Console.ReadKey();

        }
    }
}
