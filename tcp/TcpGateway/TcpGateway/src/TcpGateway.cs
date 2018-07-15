// Copyright 2009-2010 Christian d'Heureuse, Inventec Informatik AG, Zurich, Switzerland
// www.source-code.biz, www.inventec.ch/chdh
//
// License: GPL, GNU General Public License, V3 or later, http://www.gnu.org/licenses/gpl.html
// Please contact the author if you need another license.
//
// This module is provided "as is", without warranties of any kind.

namespace Biz.Source_Code.TcpGateway
{

    using AddressFamily = System.Net.Sockets.AddressFamily;
    using Array = System.Array;
    using AsyncCallback = System.AsyncCallback;
    using DateTime = System.DateTime;
    using Exception = System.Exception;
    using TcpChannelSet = System.Collections.Generic.HashSet<TcpChannel>;
    using IAsyncResult = System.IAsyncResult;
    using IPAddress = System.Net.IPAddress;
    using IPEndPoint = System.Net.IPEndPoint;
    using ProtocolType = System.Net.Sockets.ProtocolType;
    using Socket = System.Net.Sockets.Socket;
    using SocketError = System.Net.Sockets.SocketError;
    using SocketException = System.Net.Sockets.SocketException;
    using SocketFlags = System.Net.Sockets.SocketFlags;
    using SocketShutdown = System.Net.Sockets.SocketShutdown;
    using SocketType = System.Net.Sockets.SocketType;
    using TextWriter = System.IO.TextWriter;

    //--- Gateway ------------------------------------------------------------------

    // A listen-listen TCP gateway.
    // The gateway connects two TCP ports.
    // Multiple connection pairs may be active at the same time, but only a single
    // unpaired waiting connection is supported.
    public class TcpGateway
    {

        // The following fields must be set before open() is called.
        public int portNo1;
        public int portNo2;
        public Logger logger;

        private TcpListener listener1;
        private TcpListener listener2;
        private object gwLock = new object();

        public void Open()
        {
            lock (gwLock)
                Open2();
        }

        private void Open2()
        {
            listener1 = new TcpListener();
            listener2 = new TcpListener();
            listener1.portNo = portNo1;
            listener2.portNo = portNo2;
            listener1.peerListener = listener2;
            listener2.peerListener = listener1;
            listener1.gwLock = gwLock;
            listener2.gwLock = gwLock;
            listener1.logger = logger;
            listener2.logger = logger;
            listener1.Open();
            listener2.Open();
            Log(5, "TCP gateway opened.");
        }

        public void Close()
        {
            lock (gwLock)
                Close2();
        }

        private void Close2()
        {
            Log(5, "Closing TCP gateway.");
            listener1.Close();
            listener2.Close();
        }

        private void Log(int logLevel, string msg)
        {
            logger.Log(logLevel, "Gateway " + portNo1 + "/" + portNo2 + ": " + msg);
        }

    } // end class TcpGateway

    //--- Listener -----------------------------------------------------------------

    internal class TcpListener
    {

        public int portNo;
        public TcpListener peerListener;
        public object gwLock;
        public Logger logger;

        private Socket listenerSocket;
        private AsyncCallback acceptCallbackDelegate;
        private bool isOpen;
        private TcpChannelSet channelSet;
        private TcpChannel pendingNewChannel;            // waiting new channel, which has not yet a peer

        public void Open()
        {
            acceptCallbackDelegate = new AsyncCallback(AcceptCallback);
            channelSet = new TcpChannelSet();
            listenerSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            IPEndPoint localEp = new IPEndPoint(IPAddress.Any, portNo);
            listenerSocket.Bind(localEp);
            listenerSocket.Listen(2);
            isOpen = true;
            Log(6, "TCP listener opened.");
            BeginAccept();
        }

        private void BeginAccept()
        {
            listenerSocket.BeginAccept(acceptCallbackDelegate, null);
        }
        // Warning: AcceptCallback() may be called synchronously during BeginAccept()!

        private void AcceptCallback(IAsyncResult ar)
        {
            if (!isOpen) return;
            lock (gwLock)
            {
                try
                {
                    AcceptCallback2(ar);
                }
                catch (Exception e)
                {
                    LogFatalError(e);
                }
            }
        }

        private void AcceptCallback2(IAsyncResult ar)
        {
            if (!isOpen) return;
            Socket socket = AcceptConnection(ar);
            if (socket != null)
                ProcessNewChannel(socket);
            if (!isOpen) return;
            BeginAccept();
        }

        private Socket AcceptConnection(IAsyncResult ar)
        {
            Socket socket;
            try
            {
                socket = listenerSocket.EndAccept(ar);
            }
            catch (SocketException e)
            {
                switch (e.SocketErrorCode)
                {
                    case SocketError.NetworkUnreachable:
                    case SocketError.NetworkReset:
                    case SocketError.ConnectionAborted:
                    case SocketError.ConnectionReset:
                    case SocketError.NotConnected:
                    case SocketError.TimedOut:
                    case SocketError.HostUnreachable:
                        return null;
                }                                 // ignore error
                throw;
            }
            Log(2, "Incomming TCP connection from " + socket.RemoteEndPoint + ".");
            return socket;
        }

        private void ProcessNewChannel(Socket socket)
        {
            if (pendingNewChannel != null)
            {                        // we support only one pending new channel
                Log(2, "Closing pending previous incoming connection.");
                pendingNewChannel.Close(true);
                pendingNewChannel = null;
            }
            TcpChannel channel = new TcpChannel();
            channel.socket = socket;
            channel.listener = this;
            channel.gwLock = gwLock;
            channel.logger = logger;
            channelSet.Add(channel);
            TcpChannel channel2 = peerListener.GetPendingNewChannel();
            if (channel2 != null)
            {
                channel.peerChannel = channel2;
                channel2.peerChannel = channel;
            }
            else
                pendingNewChannel = channel;
            channel.Open();
        }

        // Called from the peer listener to get a waiting new channel.
        private TcpChannel GetPendingNewChannel()
        {
            if (!isOpen) return null;
            TcpChannel c = pendingNewChannel;
            pendingNewChannel = null;
            return c;
        }

        public void DeregisterChannel(TcpChannel channel)
        {
            if (!isOpen) return;
            channelSet.Remove(channel);
            if (pendingNewChannel == channel)
                pendingNewChannel = null;
        }

        public void Close()
        {
            isOpen = false;
            foreach (TcpChannel channel in channelSet)
            {
                try
                {
                    channel.Close(true);
                }
                catch (Exception e)
                {
                    Log(5, "Error while closing channel: ", e);
                }
            }
            listenerSocket.Close();
        }

        private void Log(int logLevel, string msg)
        {
            logger.Log(logLevel, "Listener " + portNo + ": " + msg);
        }
        private void Log(int logLevel, string msg, Exception e)
        {
            logger.Log(logLevel, "Listener " + portNo + ": " + msg, e);
        }
        private void LogFatalError(Exception e)
        {
            logger.LogFatalError("Listener " + portNo + ": ", e);
        }

    } // end class TcpListener

    //--- Channel ------------------------------------------------------------------

    internal class TcpChannel
    {

        private const int rxQueueSize = 0x1000;

        public Socket socket;
        public TcpListener listener;
        public TcpChannel peerChannel;                  // may be null
        public object gwLock;
        public Logger logger;

        private string channelSignature;             // signature string identifying this channel
        private byte[] rxBuf = new byte[0x2000];
        private byte[] rxQueue;                      // used to buffer received data while the peer is not yet connected
        private int rxQueueUsed;                  // no of used bytes in rxQueue
        private AsyncCallback receiveCallbackDelegate;
        private AsyncCallback sendCallbackDelegate;
        private bool isOpen;
        private bool rxActive;                     // true between BeginReceive and EndReceive
        private bool txActive;                     // true between BeginSend and EndSend

        public void Open()
        {
            channelSignature = listener.portNo + "-" + socket.RemoteEndPoint;
            receiveCallbackDelegate = new AsyncCallback(ReceiveCallback);
            sendCallbackDelegate = new AsyncCallback(SendCallback);
            isOpen = true;
            Log(7, "Channel opened.");
            BeginReceive();
            if (!isOpen) return;
            if (peerChannel != null && peerChannel.isOpen && peerChannel.rxQueueUsed > 0)
                peerChannel.SendQueuedReceivedData();
        }

        public void Close(bool abort)
        {
            Log(7, "Closing channel.");
            isOpen = false;
            listener.DeregisterChannel(this);
            if (!abort) socket.Shutdown(SocketShutdown.Both);
            socket.Close();
        }

        private void BeginReceive()
        {
            try
            {
                rxActive = true;
                socket.BeginReceive(rxBuf, 0, rxBuf.Length, SocketFlags.None, receiveCallbackDelegate, null);
            }
            // Warning: ReceiveCallback() may be called synchronously during BeginReceive()!
            catch (Exception e)
            {
                rxActive = false;
                ProcessIoException(e);
                return;
            }
        }

        private void ReceiveCallback(IAsyncResult ar)
        {
            if (!isOpen) return;
            lock (gwLock)
            {
                try
                {
                    ReceiveCallback2(ar);
                }
                catch (Exception e)
                {
                    LogFatalError(e);
                }
            }
        }

        private void ReceiveCallback2(IAsyncResult ar)
        {
            if (!isOpen) return;
            rxActive = false;
            int rxDataLen;
            try
            {
                rxDataLen = socket.EndReceive(ar);
            }
            catch (Exception e)
            {
                ProcessIoException(e);
                return;
            }
            if (rxDataLen <= 0)
            {
                Log(2, "Channel disconnected by remote.");
                ProcDisconnected();
                return;
            }
            if (logger.CheckLevel(9))
                Log(9, "Received " + rxDataLen + " bytes.");
            if (peerChannel == null || !peerChannel.isOpen)
            {
                // The peer is not connected. We queue the received data and continue receiving.
                QueueReceivedData(rxBuf, rxDataLen);
                BeginReceive();
                return;
            }
            if (peerChannel.txActive)
            {
                // The peer transmitter is active sending the previously queued data.
                // We stop receiving here. The peer will restart our receiver when the peer transmitter is finished.
                QueueReceivedData(rxBuf, rxDataLen);
                return;
            }
            if (rxQueueUsed > 0)
            {
                // The peer is ready, but there is data in our RX queue.
                // We add the newly received data to the queue and send the queue.
                QueueReceivedData(rxBuf, rxDataLen);
                SendQueuedReceivedData();
                return;
            }
            // This is the normal case when both peers are connected and the queue is empty.
            // Receiving and transmitting are alternated and the RX queue buffer is not used.
            peerChannel.BeginSend(rxBuf, rxDataLen);
        }

        private void QueueReceivedData(byte[] buf, int len)
        {
            Log(9, "Queueing " + len + " bytes.");
            if (rxQueue == null) rxQueue = new byte[rxQueueSize];
            if (rxQueueUsed + len > rxQueueSize)
            {
                // ignore old data on overflow
                rxQueueUsed = 0;
                Log(3, "RX queue overflow.");
            }
            if (len > rxQueueSize)
            {
                // Ignore if new data block is too large for queue.
                return;
            }
            Array.Copy(buf, 0, rxQueue, rxQueueUsed, len);
            rxQueueUsed += len;
        }

        private void SendQueuedReceivedData()
        {
            int len = rxQueueUsed;
            byte[] data = rxQueue;
            rxQueueUsed = 0;
            rxQueue = null;
            peerChannel.BeginSend(data, len);
        }

        private void BeginSend(byte[] dataBuf, int dataLen)
        {
            if (logger.CheckLevel(9))
                Log(9, "Sending " + dataLen + " bytes.");
            try
            {
                txActive = true;
                socket.BeginSend(dataBuf, 0, dataLen, SocketFlags.None, sendCallbackDelegate, null);
            }
            // Warning: SendCallback() may be called synchronously during BeginSend()!
            catch (Exception e)
            {
                txActive = false;
                ProcessIoException(e);
                return;
            }
        }

        private void SendCallback(IAsyncResult ar)
        {
            if (!isOpen) return;
            lock (gwLock)
            {
                try
                {
                    SendCallback2(ar);
                }
                catch (Exception e)
                {
                    LogFatalError(e);
                }
            }
        }

        private void SendCallback2(IAsyncResult ar)
        {
            if (!isOpen) return;
            txActive = false;
            try
            {
                socket.EndSend(ar);
            }
            catch (Exception e)
            {
                ProcessIoException(e);
                return;
            }
            if (peerChannel == null || !peerChannel.isOpen) return;
            if (peerChannel.rxQueueUsed > 0)
            {
                peerChannel.SendQueuedReceivedData();
                return;
            }
            if (!peerChannel.rxActive)
                peerChannel.BeginReceive();
        }

        private void ProcessIoException(Exception e)
        {
            if (!isOpen) return;
            LogDisconnectionReason(e);
            ProcDisconnected();
        }

        private void ProcDisconnected()
        {
            CloseBothChannels();
        }

        private void CloseBothChannels()
        {
            Close(false);
            if (peerChannel != null && peerChannel.isOpen)
                peerChannel.Close(false);
        }

        private void LogDisconnectionReason(Exception e)
        {
            if (!(e is SocketException))
            {
                Log(1, "Fatal error during channel i/o: ", e);
                return;
            }
            SocketError ec = ((SocketException)e).SocketErrorCode;
            if (CheckNormalDisconnectionErrorCode(ec))
                Log(2, "Channel disconnected, errorCode=" + ec + ".");
            else if (CheckAbnormalDisconnectionErrorCode(ec))
                Log(1, "Abnormal channel disconnection, errorCode=" + ec + ", ", e);
            else
                Log(1, "Fatal socket error during channel i/o, errorCode=" + ec + ", ", e);
        }

        private bool CheckNormalDisconnectionErrorCode(SocketError ec)
        {
            switch (ec)
            {
                case SocketError.ConnectionReset:
                    return true;
            }
            return false;
        }

        private bool CheckAbnormalDisconnectionErrorCode(SocketError ec)
        {
            switch (ec)
            {
                case SocketError.NetworkDown:
                case SocketError.NetworkUnreachable:
                case SocketError.NetworkReset:
                case SocketError.ConnectionAborted:
                case SocketError.NotConnected:
                case SocketError.Shutdown:
                case SocketError.TimedOut:
                case SocketError.HostDown:
                case SocketError.HostUnreachable:
                case SocketError.Disconnecting:
                    return true;
            }
            return false;
        }

        private void Log(int logLevel, string msg)
        {
            logger.Log(logLevel, getLogPrefix() + msg);
        }
        private void Log(int logLevel, string msg, Exception e)
        {
            logger.Log(logLevel, getLogPrefix() + msg, e);
        }
        private void LogFatalError(Exception e)
        {
            logger.LogFatalError(getLogPrefix(), e);
        }
        private string getLogPrefix()
        {
            return "Channel " + channelSignature + " (peer: " + (peerChannel == null ? "none" : peerChannel.channelSignature) + "): ";
        }

    } // end class TcpChannel

    //--- Logger -------------------------------------------------------------------

    public class Logger
    {

        private TextWriter logFile;
        private int logLevel;           // 0 = log only fatal errors, 9 = debug output

        public Logger(TextWriter logFile, int logLevel)
        {
            this.logFile = logFile;
            this.logLevel = logLevel;
        }

        public bool CheckLevel(int logLevel)
        {
            return logLevel <= this.logLevel && logFile != null;
        }

        public void Log(string msg)
        {
            if (logFile == null) return;
            lock (logFile)
            {
                logFile.WriteLine(DateTime.Now.ToString(@"yyyy\-MM\-dd HH\:mm\:ss") + " " + msg);
                logFile.Flush();
            }
        }

        public void Log(int logLevel, string msg)
        {
            if (CheckLevel(logLevel))
                Log(msg);
        }

        public void Log(int logLevel, string msg, Exception e)
        {
            Log(logLevel, msg + e.ToString());
        }

        public void LogFatalError(string prefix, Exception e)
        {
            Log(0, prefix + "Fatal error: ", e);
        }

    } // end class Logger

    //------------------------------------------------------------------------------

} // end namespace
