using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Net.Security;
using System.Numerics;
using System.Security.Authentication;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using TcpForwarder.Utils;

namespace TcpForwarder {
    internal class ForwardingConnection {

        // Events may be called from different threads
        /// <summary>
        /// Raised when the local connection has been authenticated when using SSL.
        /// If not using SSL, it will not be raised.
        /// </summary>
        public event Action<SslProtocols,
            CipherAlgorithmType, int,
            HashAlgorithmType, int,
            ExchangeAlgorithmType, int> LocalConnectionAuthenticated;
        /// <summary>
        /// Raised when the remote connection has been established. Note that if the local connection
        /// uses SSL, the remote connection will not be created until authentication has succeded.
        /// </summary>
        public event Action<bool, IPEndPoint, IPEndPoint> RemoteConnectionEstablished;
        /// <summary>
        /// Raised when the remote connection has been authenticated when using SSL.
        /// If not using SSL, it will not be raised.
        /// </summary>
        public event Action<SslProtocols, System.Security.Cryptography.X509Certificates.X509Certificate2,
            CipherAlgorithmType, int,
            HashAlgorithmType, int,
            ExchangeAlgorithmType, int> RemoteConnectionAuthenticated;
        public event Action LocalConnectionClosed;
        public event Action RemoteConnectionClosed;
        /// <summary>
        /// Raised when both directions of the connection have been shutdown.
        /// </summary>
        public event Action ConnectionClosedCompletely;
        /// <summary>
        /// Raised when either the local end or the remote end of the connection was
        /// aborted.
        /// This event may be called multiple times, but only the first call should be treated as relevant.
        /// After this event occured, other events from this connection like DataReceivedLocal etc. should be ignored.
        /// </summary>
        public event Action<bool, Exception> ConnectionAborted;
        public event Action<byte[], int, int> DataReceivedLocal;
        public event Action DataForwardedLocal;
        public event Action<byte[], int, int> DataReceivedRemote;
        public event Action DataForwardedRemote;

        private readonly BigInteger conId;

        private readonly SemaphoreSlim
            sendWaitSemaphore = new SemaphoreSlim(0, 1),
            receiveWaitSemaphore = new SemaphoreSlim(0, 1),
            sendThrottleSemaphore = new SemaphoreSlim(0, 1),
            receiveThrottleSemaphore = new SemaphoreSlim(0, 1);



        public BigInteger ConId {
            get { return conId; }
        }

        /// <summary>
        /// An application can store custom data here.
        /// </summary>
        public object Tag { get; set; }


        private TcpConnectionForwarder forwarder;
        private readonly IPAddress addressForBind;
        private readonly IPAddress addressForConnect;

        private TcpClient client;
        private TcpClient server;
        private Stream strClient;
        private Stream strServer;
        private object lockObjForClosing = new object();

        private readonly int sendTimeout = 120000; // 2 min
        private readonly int receiveTimeout = 600000; // 10 min
        private int shutdownCount = 0;

        private readonly Func<SemaphoreSlim, Task> waitForSending, waitForReceiving;
        private readonly SemaphoreSlim releaseConnectionSemaphore;

        //private bool isSending = false;
        //private bool isReceiving = false;
        private readonly IPEndPoint localLocalIPEndpoint;
        private readonly IPEndPoint localRemoteIPEndpoint;

        public IPEndPoint LocalLocalIPEndpoint {
            get {
                return localLocalIPEndpoint;
            }
        }
        public IPEndPoint LocalRemoteIPEndpoint {
            get {
                return localRemoteIPEndpoint;
            }
        }

        public ForwardingConnection(TcpConnectionForwarder forwarder, TcpClient client, BigInteger conId, IPAddress addressForConnect, IPAddress addressForBind,
                Func<SemaphoreSlim, Task> waitForSending, Func<SemaphoreSlim, Task> waitForReceiving, SemaphoreSlim releaseConnectionSemaphore) {
            this.forwarder = forwarder;
            this.client = client;
            this.conId = conId;
            this.addressForConnect = addressForConnect;
            this.addressForBind = addressForBind;
            this.waitForSending = waitForSending;
            this.waitForReceiving = waitForReceiving;
            this.localRemoteIPEndpoint = ((IPEndPoint)client.Client.RemoteEndPoint);
            this.localLocalIPEndpoint = ((IPEndPoint)client.Client.LocalEndPoint);
            this.releaseConnectionSemaphore = releaseConnectionSemaphore;

            // Note: Setting client.SendTimeout has no effect in our case because it only applies to synchronous Write() calls.
            // To implement a send timeout, we implement something that will close the destination socket after a specific time
            // if the write call did not finish in that time, so that the Write() or Read() method of the stream will throw an exception.
            // Currently, this is implemented by the AsyncTimeoutHelper class which uses Semaphores in a separate Task to decide if the
            // socket should be closed.
        }

        public async Task Run() {
            try {
                await ConnectAsync();
            } finally {
                // Connection is finished; so release the semaphore.
                releaseConnectionSemaphore.Release();
            }
        }

        private async Task ConnectAsync() {

            strClient = client.GetStream();
            
            // If the server uses SSL, we try to authenticate as a Server before we open a forwarding connection.
            if (forwarder.LocalSslCertificate != null) {
                SslStream sslStream = new SslStream(strClient, true);
                try {
                    await sslStream.AuthenticateAsServerAsync(forwarder.LocalSslCertificate, false, forwarder.LocalSslProtocols, false);

                } catch (Exception ex) { // TODO: Use more specific catch clause
                    if (ExceptionUtils.ShouldExceptionBeRethrown(ex))
                        throw;

                    OnConnectionAborted(true, ex);

                    // We could not authenticate the connection. Therefore we need to close the client socket.
                    Cancel();

                    return;
                }

                // Authentication OK. Use the SslStream to send and receive data.
                strClient = sslStream;

                OnLocalConnectionAuthenticated(sslStream.SslProtocol, sslStream.CipherAlgorithm, sslStream.CipherStrength,
                    sslStream.HashAlgorithm, sslStream.HashStrength, sslStream.KeyExchangeAlgorithm, sslStream.KeyExchangeStrength);
            }

            // Try to establish a connection to the server.
            // First we try to connect using a IPv6 socket; if it fails, we use a IPv4 socket.
            Exception e = null;
            bool usedIpv6 = false;
                    

            for (int i = 0; i < 2; i++) {
                AddressFamily adrFamily = i == 0 ? AddressFamily.InterNetworkV6 : AddressFamily.InterNetwork;
                if (addressForBind != null && addressForBind.AddressFamily != adrFamily || addressForConnect != null && addressForConnect.AddressFamily != adrFamily)
                    continue;

                TcpClient c = new TcpClient(adrFamily);


                try {
                    if (addressForBind != null) {
                        // Use the given addresses to bind and to connect, instead of the remotehost string
                        // (this is used when connecting to localhost addresses, as the forwarder will generate a random address
                        // in the range 127.0.0.1-127.255.255.254 to minimize the risk of re-using a combination of a specific local endpoint to connect
                        // to a specific remote endpoint in a short interval of time, which is not permitted by TCP/IP to prohibit data corruption).
                        c.Client.Bind(new IPEndPoint(addressForBind, 0));
                    }

                    if (addressForConnect != null) {
                        await c.ConnectAsync(addressForConnect, forwarder.RemotePort);
                    } else {
                        await c.ConnectAsync(forwarder.RemoteHost, forwarder.RemotePort);
                    }

                    
                } catch (Exception ex) {
                    if (ExceptionUtils.ShouldExceptionBeRethrown(ex))
                        throw;

                    if (e == null) {
                        e = ex;
                    } else {
                        e = ex; // TODO!
                    }
                    c.Close();
                    continue;
                }

                // OK, Connection established
                usedIpv6 = adrFamily == AddressFamily.InterNetworkV6;
                server = c;
                e = null;
                break;

            }


            if (e != null) {
                // TODO maybe also pause here if pause is enabled for the server.
                
                OnConnectionAborted(false, e);

                // We could not establish the connection. Therefore we need to close the client socket.
                Cancel();

                return;
            }

            OnRemoteConnectionEstablished(usedIpv6, (IPEndPoint)server.Client.LocalEndPoint, (IPEndPoint)server.Client.RemoteEndPoint);


            strServer = server.GetStream();
            
            // If we use client SSL, then create an SSL Stream and authenticate
            // asynchronously.
            if (forwarder.RemoteSslHost != null) {
                SslStream stream = new SslStream(strServer, true);
                try {
                    await stream.AuthenticateAsClientAsync(forwarder.RemoteSslHost,
                        new System.Security.Cryptography.X509Certificates.X509CertificateCollection(),
                        forwarder.RemoteSslProtocols, false);
                } catch (Exception ex) {
                    if (ExceptionUtils.ShouldExceptionBeRethrown(ex))
                        throw;

                    // TOODO maybe also pause here if pause is enabled for the server.
                
                    OnConnectionAborted(false, ex);

                    // We could not authenticate the connection. Therefore we need to close the client and server sockets.
                    Cancel();

                    return;
                }

                // Authentication was OK - now use the SSLStream to transfer data.
                strServer = stream;
                System.Security.Cryptography.X509Certificates.X509Certificate2 remoteCert = new System.Security.Cryptography.X509Certificates.X509Certificate2(stream.RemoteCertificate);

                OnRemoteConnectionAuthenticated(stream.SslProtocol, remoteCert, stream.CipherAlgorithm, stream.CipherStrength,
                    stream.HashAlgorithm, stream.HashStrength, stream.KeyExchangeAlgorithm, stream.KeyExchangeStrength);
            }
            

            // now start to read from the client and pass the data to the server.
            // Also, start to read from the server and pass the data to the client.

            // Note: The read timeout should only be used to determine if the remote endpoint is not available any more.
            // This means, if we can't read data for a long while we still can write data, a timeout should not occur.
            // Therefore we reset the timeout if we still can write data.
            string exMsg = "The socket {0} operation exceeded the timeout of {{0}} ms.";
            AsyncTimeoutHelper clientSendTimeoutHelper = new AsyncTimeoutHelper(sendTimeout, ex => AbortConnection(true, ex), string.Format(exMsg, "write"));
            AsyncTimeoutHelper clientReceiveTimeoutHelper = new AsyncTimeoutHelper(receiveTimeout, ex => AbortConnection(true, ex), string.Format(exMsg, "read"));
            AsyncTimeoutHelper serverSendTimeoutHelper = new AsyncTimeoutHelper(sendTimeout, ex => AbortConnection(false, ex), string.Format(exMsg, "write"));
            AsyncTimeoutHelper serverReceiveTimeoutHelper = new AsyncTimeoutHelper(receiveTimeout, ex => AbortConnection(false, ex), string.Format(exMsg, "read"));

            // Use ExceptionUtils.WrapTaskForHandlingUnhandledExceptions because we start two tasks and only await t2 after t1 is finished.
            Task t1 = ExceptionUtils.WrapTaskForHandlingUnhandledExceptions(async () => await CopyStreamAsync(true, client, server, strClient, strServer, clientReceiveTimeoutHelper, serverSendTimeoutHelper, serverReceiveTimeoutHelper));
            Task t2 = ExceptionUtils.WrapTaskForHandlingUnhandledExceptions(async () => await CopyStreamAsync(false, server, client, strServer, strClient, serverReceiveTimeoutHelper, clientSendTimeoutHelper, clientReceiveTimeoutHelper));

            await t1;
            await t2;

            // Dispose the timeout helpers.
            await clientSendTimeoutHelper.DisposeAsync();
            await clientReceiveTimeoutHelper.DisposeAsync();
            await serverSendTimeoutHelper.DisposeAsync();
            await serverReceiveTimeoutHelper.DisposeAsync();


            // Copying the streams has finished.
            // In this state we should be able to Dispose the Streams and Sockets, as they initiated
            // shutdown so all data should be sent.
            // We don't need to lock on lockObjForClosing since every task that might close the sockets
            // (CopyStreamAsync() method, AsyncTimeoutHelper) has already finished in this state.
            strClient.Close();
            strServer.Close();
            client.Close();
            server.Close();

        }

        /// <summary>
        /// Frees resources if the local or remote connection could not be established.
        /// This is not called anymore once CopyStreamAsync() is run for both streams.
        /// </summary>
        private void Cancel() {
            client.Client.Close(0); // Close the socket so that it resets the connection.
            client.Close();
            if (server != null) {
                server.Client.Close(0); // Close the socket so that it resets the connection.
                server.Close();
            }

            strClient.Close();
            if (strServer != null) {
                strServer.Close();
            }

            // Free semaphores.
            sendThrottleSemaphore.Dispose();
            receiveThrottleSemaphore.Dispose();
            sendWaitSemaphore.Dispose();
            receiveWaitSemaphore.Dispose();
        }


        /// <summary>
        /// Helper class for implementing timeouts when using asynchronous operations on TcpClients/Sockets,
        /// as in this case e.g. TcpClient.SendTimeout does not apply, and a CancellationToken supplied to
        /// NetworkStream.ReadAsync(...) or .WriteAsync(...) does not seem to be honored.
        /// Therefore, after a timeout occurs we close the TcpClient so that pending async operations are
        /// aborted.
        /// </summary>
        private class AsyncTimeoutHelper {
            private readonly SemaphoreSlim semWriteTimeoutStart = new SemaphoreSlim(0); // can be release two times: When starting a write operation and when exiting.
            private readonly SemaphoreSlim semWriteTimeoutCancel = new SemaphoreSlim(0);
            private readonly ConcurrentQueue<SemaphoreReleaseType> semWriteTimeoutCancelQueue = new ConcurrentQueue<SemaphoreReleaseType>();
            private readonly int timeout;
            private readonly Task timeoutTask;
            private readonly string timeoutExceptionMessage;

            public AsyncTimeoutHelper(int timeout, Action<Exception> timeoutAction, string timeoutExceptionFormatString) {
                this.timeout = timeout;
                this.timeoutExceptionMessage = string.Format(timeoutExceptionFormatString, timeout.ToString(CultureInfo.InvariantCulture));

                this.timeoutTask = new Func<Task>(async () => {

                    while (true) {
                        // Wait for the timeout to start.
                        await semWriteTimeoutStart.WaitAsync();

                        bool loop;
                        do {
                            loop = false;

                            // Start the timeout.
                            bool ok = await semWriteTimeoutCancel.WaitAsync(timeout);

                            if (!ok) {                                
                                // The write call did not finish in the timespan - now close the socket.
                                timeoutAction(new TimeoutException(timeoutExceptionMessage));                                

                                // Now wait for the object being put in the semaphore after the timeout.
                                await semWriteTimeoutCancel.WaitAsync();
                            }
                            
                            SemaphoreReleaseType type;
                            if (!semWriteTimeoutCancelQueue.TryDequeue(out type))
                                throw new InvalidOperationException(); // should never occur

                            if (type == SemaphoreReleaseType.ResetTimeout) {
                                // Reset the timeout, so loop.
                                loop = true;

                            } else if (type == SemaphoreReleaseType.CancelTimeout) {
                                // Do nothing

                            } else if (type == SemaphoreReleaseType.Quit) {
                                return;
                            }

                        } while (loop);
                    }

                })();
            }

            public async Task DoTimeoutableOperationAsync(Func<Task> callback) {
                if (timeout == -1) { // Timeout is disabled.
                    await callback();
                    return;
                }

                // Start the task for the write timeout.
                semWriteTimeoutStart.Release();
                try {

                    await callback(); // May throw an exception

                } finally {
                    // Notify that the callback has finished now. We also do this even if the helper already
                    // aborted the connection in the meanwhile (concurrency issue), because in every case
                    // the timer helper will remove the object from the semWriteTimeoutCancelQueue semaphore.
                    semWriteTimeoutCancelQueue.Enqueue(SemaphoreReleaseType.CancelTimeout);
                    semWriteTimeoutCancel.Release();
                }
                  
            }

            /// <summary>
            /// Resets the timeout.
            /// </summary>
            public void ResetTimeout() {
                semWriteTimeoutCancelQueue.Enqueue(SemaphoreReleaseType.ResetTimeout);
                semWriteTimeoutCancel.Release();
            }

            public async Task DisposeAsync() {
                // Stop the write timeout task and wait for the it to finish.
                semWriteTimeoutCancelQueue.Enqueue(SemaphoreReleaseType.Quit);
                semWriteTimeoutCancel.Release(); // Relase the semaphore so the task can quit
                semWriteTimeoutStart.Release(); // This semaphore must be released after the semWriteTimeoutCancel to avoid
                // calling timeoutAction() if the current thread is stuck on semWriteTimeoutStart.Release() and does not
                // call semWriteTimeoutCancel.Release()
               

                // Wait for the timeout task to finish.
                await timeoutTask;

                // Dispose the semaphores as they now are not used any more.
                // Note: Dispose them after awaiting the timeoutTask as otherwise it might still use it
                // while re already disposed it.
                semWriteTimeoutStart.Dispose();
                semWriteTimeoutCancel.Dispose();

            }



            private enum SemaphoreReleaseType : int {
                StartTimeout = 0,
                CancelTimeout,
                ResetTimeout,
                Quit
            }
        }

        /// <summary>
        /// Aborts the connection by raising the OnConnectionAborted event and then closing the sockets.
        /// </summary>
        /// <param name="fromClient"></param>
        /// <param name="ex"></param>
        private void AbortConnection(bool fromClient, Exception ex) {
            // The connection shall be aborted.
            // Therefore we need to close both sockets to ensure all active async I/O operations
            // are cancelled.
            // Raise the event prior to Close()ing both connections to make sure
            // the correct source (client or server) is reported.
            OnConnectionAborted(fromClient, ex);

            // Note: Close() of the TcpClients may be called from multiple threads at the same time,
            // so we lock over lockObjForClosing to avoid concurrency issues.
            lock (lockObjForClosing) {
                server.Client.Close(0); // Close the socket so that it resets the connection.
                client.Client.Close(0); // Close the socket so that it resets the connection.
                server.Close();
                client.Close();
            }
        }

        private async Task CopyStreamAsync(bool fromClient, TcpClient tcpIn, TcpClient tcpOut, Stream strIn, Stream strOut, 
            AsyncTimeoutHelper receiveTimeoutHelper, AsyncTimeoutHelper sendTimeoutHelper, AsyncTimeoutHelper oppositeReceiveTimeoutHelper) {
           
            try {

                byte[] buf = new byte[64 * 1024]; // 64 KiB Buffer for optimal efficiency

                Task waitTask;
                Func<Task> getWaitTask = () => {
                    Task awaitableTask;
                    if (fromClient) {
                        awaitableTask = waitForSending(sendWaitSemaphore);
                    } else {
                        awaitableTask = waitForReceiving(receiveWaitSemaphore);
                    }
                    return awaitableTask;
                };

                int read = 0;

                while (true) {
                    /* OLD: Only handle exceptions that occur in Read() but not the ones that
                     * occur in Write().
                     * This is because when the other conenction is aborted while it is paused,
                     * Write()s will fail although the other conenction did not yet report
                     * the abortion.
                     * NEW: Also Exceptions that occur in Write() must be handled - otherwise it could happen that
                     * the client half-closes the connection while data is still transmitted from the server to the client,
                     * and then the client aborts (resets) the connection. In this case if Write() would not handle the exection,
                     * we would never raise the ConnectionAborted event.
                     */
                    Exception readException = null;
                    try {

                        await receiveTimeoutHelper.DoTimeoutableOperationAsync(async () => read = await strIn.ReadAsync(buf, 0, buf.Length));

                        if (!(read > 0)) {
                            break;
                        }

                    } catch (Exception ex) {
                        if (ExceptionUtils.ShouldExceptionBeRethrown(ex))
                            throw;

                        // The await operator cannot be used in a catch clause.
                        // Also, don't run an async delegate here because this method
                        // might return (and therefore release the Semaphores etc.) before
                        // the async delegate completes.
                        readException = ex;
                    }

                    if (readException != null) {
                        // See if we need to pause the transmission.
                        // This also needs to be done here to ensure that when the code is waiting in ReadAsync()
                        // and then the connection is aborted that we wait correctly before shutting down the
                        // other side.
                        waitTask = getWaitTask();
                        if (waitTask != null) {
                            await waitTask;
                        }

                        // Abort the connection.
                        AbortConnection(fromClient, readException);
                        return;
                    }

                    // See if we need to pause the transmission.
                    // This is done after reading the next block because it would be
                    // more difficult to reabk the reading operation (when currently
                    // no data arrives).
                    waitTask = getWaitTask();
                    if (waitTask != null) {
                        await waitTask;
                    }

                    

                    // Check if we need to throttle transfer speed.
                    // TODO: need a better handling for this
                    if (fromClient && forwarder.ThrottleSpeedClient || !fromClient && forwarder.ThrottleSpeedServer) {
                        int speed = fromClient ? forwarder.SpeedClient : forwarder.SpeedServer;
                        SemaphoreSlim sem = fromClient ? sendThrottleSemaphore : receiveThrottleSemaphore;
                        long pauseTime = (long)((double)read * 1000d / (double)speed);
                        long startSwTime = forwarder.StopwatchElapsedMilliseconds;

                        int waitTime;
                        while (true) {
                            waitTime = (int)Math.Min(200, pauseTime - (forwarder.StopwatchElapsedMilliseconds - startSwTime));
                            if (waitTime <= 0)
                                break;

                            await sem.WaitAsync(waitTime);
                        }
                    }

                    // Raise events
                    if (fromClient) {
                        OnDataReceivedLocal(buf, 0, read);
                    } else {
                        OnDataReceivedRemote(buf, 0, read);
                    }

                    // Write the data.
                    try {

                        // Write
                        await sendTimeoutHelper.DoTimeoutableOperationAsync(() => strOut.WriteAsync(buf, 0, read));
                        
                    } catch (Exception ex) {
                        if (ExceptionUtils.ShouldExceptionBeRethrown(ex))
                            throw;

                        /* The other connection has been aborted or a write time occured.
                         * We must handle the exception. See comment in the Read() method.
                         */
                        AbortConnection(!fromClient, ex);
                        return;
                        
                    }

                    // We could write data to the other connection, so reset its read timeout as it is still alive.
                    oppositeReceiveTimeoutHelper.ResetTimeout();

                    // Raise DataForwarded event to indicate the data have actually
                    // been written.
                    if (fromClient) {
                        OnDataForwardedLocal();
                    } else {
                        OnDataForwardedRemote();
                    }

                }

                

                // See if we need to pause the transmission.
                // This also needs to be done here to ensure that when the code is waiting in ReadAsync()
                // and then the connection is closed that we wait correctly before shutting down the
                // other side.
                waitTask = getWaitTask();
                if (waitTask != null) {
                    await waitTask;
                }



                if (fromClient) {
                    OnLocalConnectionClosed();
                } else {
                    OnRemoteConnectionClosed();
                }

                // The connection has been half-closed by tcpIn. Therefore we
                // need to initiate the shutdown on tcpOut.
                try {
                    tcpOut.Client.Shutdown(SocketShutdown.Send);
                } catch (SocketException ex) {
                    System.Diagnostics.Debug.WriteLine(ex.ToString());
                    // Ignore. This probably means the connection has already
                    // been reset and the other direction will get the error
                    // when reading.
                }

                int myShutdownCount = Interlocked.Add(ref shutdownCount, 1);

                // After the shutdown was initiated for both sides,
                // deregister this ForwardingConnection from the 
                // TcpConnectionForwarder.
                if (myShutdownCount == 2) {
                    //forwarder.DeregisterConnection(this);
                    OnConnectionClosedCompletely();
                }

            } finally {
                // Free resources.
                if (fromClient) {
                    sendWaitSemaphore.Dispose();
                    sendThrottleSemaphore.Dispose();
                } else {
                    receiveWaitSemaphore.Dispose();
                    receiveThrottleSemaphore.Dispose();
                }

            }
        }




        protected void OnLocalConnectionAuthenticated(SslProtocols protocol,
            CipherAlgorithmType cipherAlgorithmType, int cipherAlgorithmStrength,
            HashAlgorithmType hashAlgorithmType, int hashAlgorithmStrength,
            ExchangeAlgorithmType exchangeAlgorithmType, int exchangeAlgorithmStrength) {

            if (LocalConnectionAuthenticated != null) {
                LocalConnectionAuthenticated(protocol, cipherAlgorithmType, cipherAlgorithmStrength, hashAlgorithmType, hashAlgorithmStrength,
                    exchangeAlgorithmType, exchangeAlgorithmStrength);
            }
        }

        protected void OnRemoteConnectionEstablished(bool usedIpv6, IPEndPoint remoteLocalEndpoint, IPEndPoint remoteRemoteEndpoint) {
            if (RemoteConnectionEstablished != null) {
                RemoteConnectionEstablished(usedIpv6, remoteLocalEndpoint, remoteRemoteEndpoint);
            }
        }

        protected void OnRemoteConnectionAuthenticated(SslProtocols protocol, System.Security.Cryptography.X509Certificates.X509Certificate2 remoteCertificate,
            CipherAlgorithmType cipherAlgorithmType, int cipherAlgorithmStrength,
            HashAlgorithmType hashAlgorithmType, int hashAlgorithmStrength,
            ExchangeAlgorithmType exchangeAlgorithmType, int exchangeAlgorithmStrength) {

            if (RemoteConnectionAuthenticated != null) {
                RemoteConnectionAuthenticated(protocol, remoteCertificate, cipherAlgorithmType, cipherAlgorithmStrength,
                    hashAlgorithmType, hashAlgorithmStrength, exchangeAlgorithmType, exchangeAlgorithmStrength);
            }
        }

        protected void OnLocalConnectionClosed() {
            if (LocalConnectionClosed != null) {
                LocalConnectionClosed();
            }
        }

        protected void OnRemoteConnectionClosed() {
            if (RemoteConnectionClosed != null) {
                RemoteConnectionClosed();
            }
        }

        protected void OnConnectionClosedCompletely() {
            if (ConnectionClosedCompletely != null) {
                ConnectionClosedCompletely();
            }
        }

        protected void OnConnectionAborted(bool fromClient, Exception ex) {
            if (ConnectionAborted != null) {
                ConnectionAborted(fromClient, ex);
            }
        }

        protected void OnDataReceivedLocal(byte[] b, int offset, int count) {
            if (DataReceivedLocal != null) {
                DataReceivedLocal(b, offset, count);
            }
        }

        protected void OnDataForwardedLocal() {
            if (DataForwardedLocal != null) {
                DataForwardedLocal();
            }
        }

        protected void OnDataReceivedRemote(byte[] b, int offset, int count) {
            if (DataReceivedRemote != null) {
                DataReceivedRemote(b, offset, count);
            }
        }

        protected void OnDataForwardedRemote() {
            if (DataForwardedRemote != null) {
                DataForwardedRemote();
            }
        }
    }
}
