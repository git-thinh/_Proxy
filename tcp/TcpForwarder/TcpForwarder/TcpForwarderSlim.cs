using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;

namespace TcpForwarder
{

    public class TcpForwarderSlim
    {
        private readonly Socket MainSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);

        private static Dictionary<string, string> dic_EndPoint_Domain = new Dictionary<string, string>() { };

        public void Start(IPEndPoint local, IPEndPoint remote_address)
        {
            MainSocket.Bind(local);
            MainSocket.Listen(10);

            dic_EndPoint_Domain.Add("127.0.0.1:9002", "amiss.com");
            dic_EndPoint_Domain.Add("127.0.0.1:9009", "id.ifc.com");

            Task.Factory.StartNew((object sok) =>
            {
                Socket m_socket = sok as Socket;

                Console.WriteLine("Opening: 127.0.0.1:9002 ... ");

                while (true)
                {
                    var source = m_socket.Accept();
                    var destination = new TcpForwarderSlim();
                    var state = new State(source, destination.MainSocket);
                    IPEndPoint remote = new IPEndPoint(IPAddress.Parse("127.0.0.1"), 9002);
                    destination.Connect(remote, source);
                    source.BeginReceive(state.Buffer, 0, state.Buffer.Length, 0, OnDataReceive, state);
                }
            }, MainSocket);


            Task.Factory.StartNew((object sok) =>
            {
                Socket m_socket = sok as Socket;

                Console.WriteLine("Opening: 127.0.0.1:9009 ... ");

                while (true)
                {
                    var source = m_socket.Accept();
                    var destination = new TcpForwarderSlim();
                    var state = new State(source, destination.MainSocket);
                    IPEndPoint remote = new IPEndPoint(IPAddress.Parse("127.0.0.1"), 9009);
                    destination.Connect(remote, source);
                    source.BeginReceive(state.Buffer, 0, state.Buffer.Length, 0, OnDataReceive, state);
                }
            }, MainSocket);

        }

        private void Connect(EndPoint remoteEndpoint, Socket destination)
        {
            var state = new State(MainSocket, destination);

            string line = System.Text.Encoding.ASCII.GetString(state.Buffer);

            MainSocket.Connect(remoteEndpoint);
            MainSocket.BeginReceive(state.Buffer, 0, state.Buffer.Length, SocketFlags.None, OnDataReceive, state);
        }

        private static void OnDataReceive(IAsyncResult result)
        {
            var state = (State)result.AsyncState;

            string uri_remote = state.DestinationSocket.RemoteEndPoint.ToString();
            string domain = "";
            dic_EndPoint_Domain.TryGetValue(uri_remote, out domain);

            if (domain == null || domain != "")
            {
                string data = System.Text.Encoding.ASCII.GetString(state.Buffer);
                if (data.Contains("/favicon.ico") == false)
                {
                    if (domain == null || data.Contains(domain))
                    {
                        Console.WriteLine("OnDataReceive: " + DateTime.Now.ToString());
                        try
                        {
                            var bytesRead = state.SourceSocket.EndReceive(result);
                            if (bytesRead > 0)
                            {
                                state.DestinationSocket.Send(state.Buffer, bytesRead, SocketFlags.None);
                                state.SourceSocket.BeginReceive(state.Buffer, 0, state.Buffer.Length, 0, OnDataReceive, state);
                            }
                        }
                        catch
                        {
                            state.DestinationSocket.Close();
                            state.SourceSocket.Close();
                        }
                    }
                }
            }
        }

        private class State
        {
            public Socket SourceSocket { get; private set; }
            public Socket DestinationSocket { get; private set; }
            public byte[] Buffer { get; private set; }

            public State(Socket source, Socket destination)
            {
                SourceSocket = source;
                DestinationSocket = destination;
                Buffer = new byte[8192];
            }
        }


    }
}