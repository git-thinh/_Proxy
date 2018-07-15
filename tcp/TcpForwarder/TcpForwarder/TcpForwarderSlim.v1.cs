using System;
using System.Net;
using System.Net.Sockets;

namespace TcpForwarder
{

    public class TcpForwarderSlim
    {
        private readonly Socket MainSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);

        public void Start(IPEndPoint local, IPEndPoint remote)
        {
            MainSocket.Bind(local);
            MainSocket.Listen(10);

            Console.WriteLine("Opening ... ");

            while (true)
            {
                var source = MainSocket.Accept();
                var destination = new TcpForwarderSlim();
                var state = new State(source, destination.MainSocket);

                string line = System.Text.Encoding.ASCII.GetString(state.Buffer);

                destination.Connect(remote, source);

                string line2 = System.Text.Encoding.ASCII.GetString(state.Buffer); 

                source.BeginReceive(state.Buffer, 0, state.Buffer.Length, 0, OnDataReceive, state);
            }
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

            string line = System.Text.Encoding.ASCII.GetString(state.Buffer); 

            Console.WriteLine("OnDataReceive: " +  DateTime.Now.ToString());
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