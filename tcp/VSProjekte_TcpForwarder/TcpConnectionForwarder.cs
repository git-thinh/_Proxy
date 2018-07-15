using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Net.Security;
using System.Numerics;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using TcpForwarder.Utils;

namespace TcpForwarder {
    internal class TcpConnectionForwarder {

        // Events may be called from different threads
        public event Action<ForwardingConnection> ConnectionAccepted;


        private readonly string host;
        private readonly int remotePort, localPort;
        
        private readonly string remoteSslHost; // If not null, the remote connection uses SSL
        private readonly SslProtocols remoteSslProtocols;

        private readonly X509Certificate2 localSslCertificate; // Certifcate if the local connection uses SSL
        private readonly SslProtocols localSslProtocols;

        private volatile bool throttleSpeedClient, throttleSpeedServer;
        private volatile int speedClient, speedServer;


        private readonly TcpListener listener;
        private readonly SemaphoreSlim concurrentConnectionsSemaphore; // Semaphore to limit the number of concurrrent connections
        private readonly Random localhostAddressRNG;
        //private Thread listenerThread;

        /// <summary>
        /// The next Connection ID. This is a BigInteger to avoid that the same Connection ID can occur multiple times
        /// (when the long overflows).
        /// </summary>
        private BigInteger nextConId = 0;
        ///// <summary>
        ///// A list that contains all active ForwardingConnections mapped by their ID.
        ///// </summary>
        //private readonly IDictionary<long, ForwardingConnection> connections = new SortedList<long, ForwardingConnection>();

        /// <summary>
        /// A list that contains SemaphoreSlims that are waiting for sending data.
        /// </summary>
        private readonly List<SemaphoreSlim> waitingSemaphoresSending = new List<SemaphoreSlim>();
        /// <summary>
        /// A list that contains SemaphoreSlims that are waiting for sending data.
        /// </summary>
        private readonly List<SemaphoreSlim> waitingSemaphoresReceiving = new List<SemaphoreSlim>();

        private readonly Stopwatch sw = new Stopwatch();

        public string RemoteHost {
            get { return host; }
        }

        public int RemotePort {
            get { return remotePort; }
        }

        public string RemoteSslHost {
            get {
                return remoteSslHost;
            }
        }

        public SslProtocols RemoteSslProtocols {
            get {
                return remoteSslProtocols;
            }
        }

        public X509Certificate LocalSslCertificate {
            get {
                return localSslCertificate;
            }
        }

        public SslProtocols LocalSslProtocols {
            get {
                return localSslProtocols;
            }
        }


        public int SpeedServer {
            get { return speedServer; }
            set { speedServer = value; }
        }

        public int SpeedClient {
            get { return speedClient; }
            set { speedClient = value; }
        }

        public bool ThrottleSpeedServer {
            get { return throttleSpeedServer; }
            set { throttleSpeedServer = value; }
        }

        public bool ThrottleSpeedClient {
            get { return throttleSpeedClient; }
            set { throttleSpeedClient = value; }
        }

        public long StopwatchElapsedMilliseconds {
            get {
                return sw.ElapsedMilliseconds;
            }
        }

        private volatile bool pauseClient, pauseServer;
        public bool PauseClient {
            get { return pauseClient; }
            set {
                if (value) {
                    pauseClient = value;
                } else {
                    lock (waitingSemaphoresSending) {
                        pauseClient = value;

                        // Go through all waiting semaphores and release them.
                        foreach (SemaphoreSlim s in waitingSemaphoresSending) {
                            s.Release();
                        }
                        waitingSemaphoresSending.Clear();
                    }
                }
            }
        }


        public bool PauseServer {
            get { return pauseServer; }
            set {
                if (value) {
                    pauseServer = value;
                } else {
                    lock (waitingSemaphoresReceiving) {
                        pauseServer = value;

                        // Go through all waiting semaphores and release them.
                        foreach (SemaphoreSlim s in waitingSemaphoresReceiving) {
                            s.Release();
                        }
                        waitingSemaphoresReceiving.Clear();
                    }
                }
            }
        }



        public TcpConnectionForwarder(int localhostAddressRNGSeed, string host, int remotePort, int localPort, string remoteSslHost,
                SslProtocols remoteSslProtocols, X509Certificate2 localSslCertificate, SslProtocols localSslProtocols,
                int maxConcurrentConnections) {
            if (remotePort < 0 || remotePort > 65535)
                throw new ArgumentOutOfRangeException("remotePort");

            if (localPort < 0 || localPort > 65535)
                throw new ArgumentOutOfRangeException("localPort");

            if (maxConcurrentConnections < 0)
                throw new ArgumentOutOfRangeException("maxConcurrentConnections");

            sw.Start();

            this.localhostAddressRNG = new Random(localhostAddressRNGSeed);
            
            this.host = host;
            this.remotePort = remotePort;
            this.localPort = localPort;
            this.remoteSslHost = remoteSslHost;
            this.remoteSslProtocols = remoteSslProtocols;
            this.localSslCertificate = localSslCertificate;
            this.localSslProtocols = localSslProtocols;

            // maxCount = (maxConcurrentConnections + 1) to ensure the semaphore can be released when Stop() is called
            this.concurrentConnectionsSemaphore = new SemaphoreSlim(maxConcurrentConnections, maxConcurrentConnections + 1);


            // Create a TcpListener.
            // Use TcpListener.Create(int port) instead of new TcpListener(...) as that will enable Dual Mode and allow to
            // bind on [::] as well as 0.0.0.0
            // See: http://blogs.msdn.com/b/webdev/archive/2013/01/08/dual-mode-sockets-never-create-an-ipv4-socket-again.aspx
            listener = TcpListener.Create(localPort);



        }

        //internal void DeregisterConnection(ForwardingConnection con) {
        //    lock (connections) {
        //        connections.Remove(con.ConId);
        //    }
        //}

        public async Task RunAsync() {
            // Ensure that exceptions thrown by listener.Start() are thrown in this thread
            // TODO: make the number of backlog queue configurable
            listener.Start(1000);
            
            while (true) {

                // Wait for the concurrent connections semaphore.
                await concurrentConnectionsSemaphore.WaitAsync();

                TcpClient client;
                try {
                    client = await listener.AcceptTcpClientAsync();
                } catch (ObjectDisposedException ex) {
                    System.Diagnostics.Debug.WriteLine(ex.ToString());
                    // Listener has been stopped.
                    // Ignore
                    return;
                }

                BigInteger conId = nextConId++;
                Func<SemaphoreSlim, Task> waitForSendingDelegate = sem => ConnectionWaitForSending(sem, true);
                Func<SemaphoreSlim, Task> waitForReceivingDelegate = sem => ConnectionWaitForSending(sem, false);


                IPAddress[] bindAndConnectAddresses = new IPAddress[2];
                if (RemoteHost == "[localhost-random]") {
                    for (int i = 0; i < bindAndConnectAddresses.Length; i++) {
                        // Generate two random addresses in the range 127.0.0.1-127.255.255.254.
                        // Bind to one of these addresses as local endpoint and use the other one for the remote endpoint.
                        // This ensures that if a lot of connections are opened and closed in a very short time, that we do not
                        // get errors that an address+port has been used multiple times when opening new connections (as the TCP/IP standard
                        // defines that between consecutive connections using the same combination of a specific local endpoint (adr+port) and a specific remote endpoint (adr+port)
                        // some time must pass, to minimize data corruption).
                        // If we use random addresses in the above range we are minimizing the risk of using the same adr+port again in a short time interval.

                        byte[] adr;
                        adr = new byte[4];
                        adr[0] = 127;

                        // TODO: Currently, for the last byte only numbers in the range 1-254 are generated although a address like
                        // 127.2.3.255 would be OK (but 127.255.255-255 wouldn't).
                        for (int x = 1; x < adr.Length; x++) {
                            adr[x] = (byte)(localhostAddressRNG.Next(254 + (x != 3 ? 2 : 0)) + (x == 3 ? 1 : 0));
                        }

                        bindAndConnectAddresses[i] = new IPAddress(adr);
                    }
                }

                ForwardingConnection con = new ForwardingConnection(this, client, conId, bindAndConnectAddresses[0], bindAndConnectAddresses[1], waitForSendingDelegate, waitForReceivingDelegate, concurrentConnectionsSemaphore);

                //lock (connections) {
                //    connections.Add(con.ConId, con);
                //}

                OnConnectionAccepted(con);

                // TODO maybe save the Task somewhere.
                Task t = ExceptionUtils.WrapTaskForHandlingUnhandledExceptions(async () => await con.Run());
            }
            
        }

        
        public void Stop() {
            listener.Stop();
            // Close all connections.
            // TODO

            // Release the concurrentConnectionsSemaphore. This is OK as maxCount = (maxConcurrentConnections + 1).
            concurrentConnectionsSemaphore.Release();
            
        }



        protected void OnConnectionAccepted(ForwardingConnection con) {
            if (ConnectionAccepted != null) {
                ConnectionAccepted(con);
            }
        }



        private Task ConnectionWaitForSending(SemaphoreSlim sem, bool sending) {
            bool block = false;

            if (sending && pauseClient) {
                lock (waitingSemaphoresSending) {
                    if (pauseClient) {
                        block = true;
                        waitingSemaphoresSending.Add(sem);
                    }
                }
            } else if (!sending && pauseServer) {
                lock (waitingSemaphoresReceiving) {
                    if (pauseServer) {
                        block = true;
                        waitingSemaphoresReceiving.Add(sem);
                    }
                }
            }

            if (!block) {
                // No need to await something.
                return null;
            }

            return sem.WaitAsync();
        }
    }
}
