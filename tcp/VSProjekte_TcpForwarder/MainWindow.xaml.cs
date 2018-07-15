using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Numerics;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using System.Windows.Threading;
using TcpForwarder.Utils;


namespace TcpForwarder
{
    /// <summary>
    /// Interaktionslogik für MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {

        private string sslHost = null;
        private X509Certificate2 serverSslCertificate;

        private TcpConnectionForwarder forwarder;
        private Task forwarderTask;
        private int connectionCount;
        private bool logDataToFile;

        private volatile bool logDataContents;

        private bool showPlainText = false;
        private readonly Encoding iso88591Win1252 = Encoding.GetEncoding(1252);

        private long currentULCount, currentDLCount;
        private long lastSwRefresh;
        private readonly Stopwatch uldlCountSw = new Stopwatch();

        private DispatcherTimer timerSpeedRefresh = new DispatcherTimer();

        public MainWindow()
        {
            InitializeComponent();

            checkLogging.IsChecked = true;
            checkLogDataEvents.IsChecked = true;

            timerSpeedRefresh.Tick += timerSpeedRefresh_Tick;
            timerSpeedRefresh.Interval = new TimeSpan(50);
            timerSpeedRefresh.Start();

            System.Windows.Forms.NumericUpDown updown1 = (System.Windows.Forms.NumericUpDown)formsHost1.Child;
            System.Windows.Forms.NumericUpDown updown2 = (System.Windows.Forms.NumericUpDown)formsHost2.Child;
            updown1.Minimum = updown2.Minimum = 1;
            updown1.Maximum = updown2.Maximum = 10000;
            updown1.Value = updown2.Value = 100;
            formsHost1.Child.Enabled = formsHost2.Child.Enabled = false;

            updown1.ValueChanged += delegate(object sender, EventArgs e)
            {
                UpdownValueChanged(true);
            };
            updown2.ValueChanged += delegate(object sender, EventArgs e)
            {
                UpdownValueChanged(false);
            };

            RefreshConnectionCount();
            RefreshUlDlSpeed();
            uldlCountSw.Start();
        }

        private void UpdownValueChanged(bool client)
        {
            System.Windows.Forms.NumericUpDown updown =
                (System.Windows.Forms.NumericUpDown)(client ? formsHost1 : formsHost2).Child;
            int value = (int)Math.Round(updown.Value * 1024);

            if (client)
            {
                forwarder.SpeedClient = value;
            }
            else
            {
                forwarder.SpeedServer = value;
            }
        }


        private void timerSpeedRefresh_Tick(object sender, EventArgs e)
        {
            if (uldlCountSw.ElapsedMilliseconds - lastSwRefresh >= 1000)
            {
                lastSwRefresh += 1000;
                RefreshUlDlSpeed();
                currentULCount = currentDLCount = 0;
            }
        }

        private void startButton_Click(object sender, RoutedEventArgs e)
        {

            try
            {
                string remoteHost = textHost.Text;
                int remotePort = int.Parse(textPortRemote.Text, CultureInfo.InvariantCulture);
                int localPort = int.Parse(textPortLocal.Text, CultureInfo.InvariantCulture);

                forwarder = new TcpConnectionForwarder(
                    new Random().Next(), remoteHost, remotePort, localPort, sslHost,
                    TcpConnectionForwarderUtils.sslProtocols, serverSslCertificate,
                    TcpConnectionForwarderUtils.sslProtocols, 10000 /* TODO: let the user specify this number */);
                UpdownValueChanged(false);
                UpdownValueChanged(true);
                forwarder.ConnectionAccepted += con =>
                {
                    ConnectionData data = null; // used by the GUI thread

                    // Add events to the connection
                    con.LocalConnectionAuthenticated += (protocol,
                        cipherAlgorithmType, cipherAlgorithmStrength,
                        hashAlgorithmType, hashAlgorithmStrength,
                        exchangeAlgorithmType, exchangeAlgorithmStrength)
                        => Dispatcher.Invoke(new Action(() => con_LocalConnectionAuthenticated(data, protocol,
                            cipherAlgorithmType, cipherAlgorithmStrength, hashAlgorithmType, hashAlgorithmStrength, exchangeAlgorithmType, exchangeAlgorithmStrength)));
                    con.RemoteConnectionEstablished += (usedIpv6, remoteLocalEndpoint, remoteRemoteEndpoint) =>
                        Dispatcher.Invoke(new Action(() => con_RemoteConnectionEstablished(data, usedIpv6, remoteLocalEndpoint, remoteRemoteEndpoint)));
                    con.RemoteConnectionAuthenticated += (protocol, remoteCertificate,
                        cipherAlgorithmType, cipherAlgorithmStrength,
                        hashAlgorithmType, hashAlgorithmStrength,
                        exchangeAlgorithmType, exchangeAlgorithmStrength)
                        => Dispatcher.Invoke(new Action(() => con_RemoteConnectionAuthenticated(data, protocol, remoteCertificate, cipherAlgorithmType, cipherAlgorithmStrength,
                            hashAlgorithmType, hashAlgorithmStrength, exchangeAlgorithmType, exchangeAlgorithmStrength)));
                    con.LocalConnectionClosed += () => Dispatcher.Invoke(new Action(() => con_LocalConnectionClosed(data)));
                    con.RemoteConnectionClosed += () => Dispatcher.Invoke(new Action(() => con_RemoteConnectionClosed(data)));
                    con.ConnectionClosedCompletely += () => Dispatcher.Invoke(new Action(() => con_ConnectionClosedCompletely(data)));
                    con.ConnectionAborted += (fromClient, ex) => Dispatcher.Invoke(new Action(() => con_ConnectionAborted(data, fromClient, ex)));
                    con.DataReceivedLocal += (buf, offset, count) =>
                    {
                        byte[] copy = null;
                        if (logDataContents || logDataToFile)
                        {
                            // Need to copy the byte[] array because when the following Action is processed
                            // it may already be used for other data.
                            // TODO: Don't copy it since we use Invoke() which waits until the method in the GUI thread
                            // has completed.
                            copy = new byte[count];
                            Array.Copy(buf, offset, copy, 0, count);
                        }
                        Dispatcher.Invoke(new Action(() => con_DataReceivedLocal(data, copy, 0, count)));
                    };
                    con.DataReceivedRemote += (buf, offset, count) =>
                    {
                        byte[] copy = null;
                        if (logDataContents || logDataToFile)
                        {
                            // Need to copy the byte[] array because when the following Action is processed
                            // it may already be used for other data.
                            copy = new byte[count];
                            Array.Copy(buf, offset, copy, 0, count);
                        }
                        Dispatcher.Invoke(new Action(() => con_DataReceivedRemote(data, copy, 0, count)));
                    };
                    con.DataForwardedLocal += () => Dispatcher.Invoke(new Action(() => con_DataForwardedLocal(data)));
                    con.DataForwardedRemote += () => Dispatcher.Invoke(new Action(() => con_DataForwardedRemote(data)));

                    Dispatcher.Invoke(new Action(() =>
                    {
                        data = new ConnectionData() { Connection = con, LocalLocalEndpoint = con.LocalLocalIPEndpoint, LocalRemoteEndpoint = con.LocalRemoteIPEndpoint };

                        forwarder_ConnectionAccepted(data);
                    }));
                };

                forwarderTask = forwarder.RunAsync();

                if (forwarderTask.IsCompleted)
                {
                    // Task has already completed - this should mean there was some exception.
                    // Wait for the task to get the exception.
                    forwarderTask.Wait();
                }
                else
                {
                    // Wrap the task to handle unhandled exceptions.
                    forwarderTask = new Func<Task>(async () => await ExceptionUtils.WrapTaskForHandlingUnhandledExceptions(async () => await forwarderTask))();
                }

                // OK
                logDataToFile = checkLogToFile.IsChecked.Value;
                startButton.IsEnabled = textHost.IsEnabled = textPortLocal.IsEnabled = textPortRemote.IsEnabled = false;
                checkPauseClient.IsEnabled = checkPauseServer.IsEnabled = true;
                formsHost1.Child.Enabled = formsHost2.Child.Enabled = true;
                checkLimitSpeedClient.IsEnabled = checkLimitSpeedServer.IsEnabled = true;
                checkUseSsl.IsEnabled = false;
                checkUseServerSsl.IsEnabled = false;
                checkLogToFile.IsEnabled = false;

            }
            catch (Exception ex)
            {
                if (ExceptionUtils.ShouldExceptionBeRethrown(ex))
                    throw;

                forwarder = null;
                forwarderTask = null;
                MessageBox.Show(ex.ToString(), "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }

        }


        private string GetExceptionText(Exception ex)
        {
            StringBuilder sb = new StringBuilder();

            // Get internal exception
            for (int i = 0; i < 10 && ex != null; i++)
            {
                if (i > 0)
                {
                    sb.Append(Environment.NewLine);
                }
                sb.Append(ex.GetType() + ": " + ex.Message);
                if (ex is SocketException)
                {
                    SocketException sockex = (SocketException)ex;
                    sb.Append("; SocketError: " + sockex.SocketErrorCode.ToString() + " (" + (int)sockex.SocketErrorCode + ")");
                }
                ex = ex.InnerException;
            }

            return sb.ToString();
        }

        private static FileStream CreateDataLogFileStream(bool incoming, BigInteger conId)
        {
            return new FileStream("Data-" + conId.ToString(CultureInfo.InvariantCulture) + "-" + (incoming ? "in" : "out") + ".txt",
                FileMode.Create, FileAccess.Write, FileShare.Read | FileShare.Delete);
        }


        private void forwarder_ConnectionAccepted(ConnectionData data)
        {
            ForwardingConnection con = data.Connection;

            connectionCount++;
            RefreshConnectionCount();

            AddLogEntry(logClient, con.ConId, "Local connection established from remote endpoint \"" + con.LocalRemoteIPEndpoint.ToString()
                + "\" to local endpoint \"" + con.LocalLocalIPEndpoint.ToString() + "\"."
                + (forwarder.LocalSslCertificate != null ? "\r\nAuthenticating..." : ""));

            if (forwarder.LocalSslCertificate == null)
            {
                AddLogEntry(logServer, con.ConId, "Connecting...");
            }

            if (logDataToFile)
            {
                // Create new Filestream for incoming connection.
                data.fileIncoming = CreateDataLogFileStream(true, con.ConId);
            }

        }

        private void con_LocalConnectionAuthenticated(ConnectionData data, System.Security.Authentication.SslProtocols protocol,
                System.Security.Authentication.CipherAlgorithmType cipherAlgorithmType, int cipherAlgorithmStrength,
                System.Security.Authentication.HashAlgorithmType hashAlgorithmType, int hashAlgorithmStrength,
                System.Security.Authentication.ExchangeAlgorithmType exchangeAlgorithmType, int exchangeAlgorithmStrength)
        {

            ForwardingConnection con = data.Connection;

            AddLogEntry(logClient, con.ConId, "Local connection authenticated. Protocol: " + protocol.ToString() + ", Cipher: " + cipherAlgorithmType.ToString()
                + " (" + cipherAlgorithmStrength + " Bit), "
                + "Hash: " + hashAlgorithmType.ToString() + " (" + hashAlgorithmStrength + " Bit), Exchange: " + exchangeAlgorithmType
                + " (" + exchangeAlgorithmStrength + " Bit)");

            AddLogEntry(logServer, con.ConId, "Connecting...");

        }

        private void con_RemoteConnectionEstablished(ConnectionData data, bool usedIpv6, IPEndPoint remoteLocalEndpoint, IPEndPoint remoteRemoteEndpoint)
        {
            ForwardingConnection con = data.Connection;

            AddLogEntry(logServer, con.ConId, "Remote connection established using IPv" + (usedIpv6 ? "6" : "4") + " from local endpoint \""
                + remoteLocalEndpoint.ToString() + "\" to remote endpoint \"" + remoteRemoteEndpoint.ToString() + "\"."
                + (forwarder.RemoteSslHost != null ? "\r\nAuthenticating..." : ""));

            if (logDataToFile)
            {
                // Create new Filestream for outgoing connection.
                data.fileOutgoing = CreateDataLogFileStream(false, con.ConId);
            }
        }

        private void con_RemoteConnectionAuthenticated(ConnectionData data, System.Security.Authentication.SslProtocols protocol,
                System.Security.Cryptography.X509Certificates.X509Certificate2 remoteCertificate,
                System.Security.Authentication.CipherAlgorithmType cipherAlgorithmType, int cipherAlgorithmStrength,
                System.Security.Authentication.HashAlgorithmType hashAlgorithmType, int hashAlgorithmStrength,
                System.Security.Authentication.ExchangeAlgorithmType exchangeAlgorithmType, int exchangeAlgorithmStrength)
        {

            ForwardingConnection con = data.Connection;

            AddLogEntry(logServer, con.ConId, "Remote connection authenticated. Protocol: " + protocol.ToString() + ", Cipher: " + cipherAlgorithmType.ToString()
                + " (" + cipherAlgorithmStrength + " Bit), "
                + "Hash: " + hashAlgorithmType.ToString() + " (" + hashAlgorithmStrength + " Bit), Exchange: " + exchangeAlgorithmType
                + " (" + exchangeAlgorithmStrength + " Bit)\r\n"
                + "Certiticate Subject: " + remoteCertificate.Subject + "\r\n"
                + "Certificate Issuer: " + remoteCertificate.Issuer + "\r\n"
                + "Valid: Not before " + new DateTimeOffset(remoteCertificate.NotBefore).ToString()
                + ", not after " + new DateTimeOffset(remoteCertificate.NotAfter).ToString() + "\r\n"
                + "Fingerprint: " + remoteCertificate.Thumbprint);

        }

        private void con_LocalConnectionClosed(ConnectionData data)
        {
            ForwardingConnection con = data.Connection;
            if (!data.IsAbortedOrClosed)
            {

                int count = data.CloseCount++;

                AddLogEntry(logClient, con.ConId, "Local connection closed (" + (count + 1) + ") \u2013 Data Offset: 0x" + data.DataCountLocal.ToString("X") + ".");

                if (logDataToFile)
                {
                    data.fileIncoming.Dispose();
                }

            }
        }

        private void con_RemoteConnectionClosed(ConnectionData data)
        {
            ForwardingConnection con = data.Connection;
            if (!data.IsAbortedOrClosed)
            {

                int count = data.CloseCount++;

                AddLogEntry(logServer, con.ConId, "Remote connection closed (" + (count + 1) + ") \u2013 Data Offset: 0x" + data.DataCountRemote.ToString("X") + ".");

                if (logDataToFile)
                {
                    data.fileOutgoing.Dispose();
                }

            }
        }

        private void con_ConnectionClosedCompletely(ConnectionData data)
        {
            ForwardingConnection con = data.Connection;
            // Fully closed.

            // This event can be raised multiple times (or the connection may already be aborted by one thread),
            // therefore we need to to this check
            if (!data.IsAbortedOrClosed)
            {
                data.IsAbortedOrClosed = true;

                connectionCount--;
                RefreshConnectionCount();
            }
        }

        private void con_ConnectionAborted(ConnectionData data, bool fromClient, Exception ex)
        {
            ForwardingConnection con = data.Connection;
            // This event can be raised multiple times (or the connection may already be closed),
            // therefore we need to to this check
            if (!data.IsAbortedOrClosed)
            {
                data.IsAbortedOrClosed = true;

                connectionCount--;
                RefreshConnectionCount();

                string reason = Environment.NewLine + GetExceptionText(ex);

                string logStr = "Connection aborted from " + (fromClient ? "client" : "server") + " \u2013 Data Offset: 0x" + data.DataCountLocal.ToString("X") + ". Reason: " + reason;
                AddLogEntry(logClient, con.ConId, logStr);
                logStr = "Connection aborted from " + (fromClient ? "client" : "server") + " \u2013 Data Offset: 0x" + data.DataCountRemote.ToString("X") + ". Reason: " + reason;
                AddLogEntry(logServer, con.ConId, logStr);

                if (logDataToFile)
                {
                    if (data.fileIncoming != null)
                        data.fileIncoming.Dispose();
                    if (data.fileOutgoing != null)
                        data.fileOutgoing.Dispose();
                }
            }
        }

        private void con_DataReceivedLocal(ConnectionData data, byte[] buf, int offset, int count)
        {
            ForwardingConnection con = data.Connection;
            if (!data.IsAbortedOrClosed)
            {

                if (checkLogDataEvents.IsChecked.Value)
                {
                    AddLogEntry(logClient, con.ConId, "Data - Offset: 0x" + data.DataCountLocal.ToString("X") + ", Count: 0x" + count.ToString("X") + ".");
                }
                if (logDataContents && buf != null)
                {
                    AddDataLog(logClient, buf, offset, count);
                }

                data.DataCountLocal += count;

                currentULCount += count;

                if (logDataToFile)
                {
                    data.fileIncoming.Write(buf, offset, count);
                    data.fileIncoming.Flush();
                }

            }
        }

        private void con_DataReceivedRemote(ConnectionData data, byte[] buf, int offset, int count)
        {
            ForwardingConnection con = data.Connection;
            if (!data.IsAbortedOrClosed)
            {

                if (checkLogDataEvents.IsChecked.Value)
                {
                    AddLogEntry(logServer, con.ConId, "Data - Offset: 0x" + data.DataCountRemote.ToString("X") + ", Count: 0x" + count.ToString("X") + ".");
                }
                if (logDataContents && buf != null)
                {
                    AddDataLog(logServer, buf, offset, count);
                }

                data.DataCountRemote += count;

                currentDLCount += count;

                if (logDataToFile)
                {
                    data.fileOutgoing.Write(buf, offset, count);
                    data.fileOutgoing.Flush();
                }

            }
        }

        private void con_DataForwardedLocal(ConnectionData data)
        {
            ForwardingConnection con = data.Connection;
            if (!data.IsAbortedOrClosed)
            {

                if (checkLogDataEvents.IsChecked.Value)
                {
                    AddLogEntry(logClient, con.ConId, "Data forwarding completed.");
                }

            }
        }

        private void con_DataForwardedRemote(ConnectionData data)
        {
            ForwardingConnection con = data.Connection;
            if (!data.IsAbortedOrClosed)
            {

                if (checkLogDataEvents.IsChecked.Value)
                {
                    AddLogEntry(logServer, con.ConId, "Data forwarding completed.");
                }

            }
        }



        private void RefreshConnectionCount()
        {
            labelConnectionsCount.Content = "Active connections: " + connectionCount.ToString();
        }

        private void RefreshUlDlSpeed()
        {
            labelUlSpeed.Content = "UL: " + ((double)currentULCount / (1024d * 1024d)).ToString("0.00") + " MiB/s";
            labelDlSpeed.Content = "DL: " + ((double)currentDLCount / (1024d * 1024d)).ToString("0.00") + " MiB/s";
        }


        private void Window_Closed(object sender, EventArgs e)
        {
            if (forwarder != null)
            {
                forwarder.Stop();
            }
        }


        private void AddLogEntry(RichTextBox box, BigInteger conId, string text)
        {
            if (checkLogging.IsChecked.Value)
            {
                Paragraph p = new Paragraph() { Margin = new Thickness() };
                p.Inlines.Add(new Bold(new Run("[ID " + conId.ToString(CultureInfo.InvariantCulture) + "] " + text)));
                box.Document.Blocks.Add(p);
                box.ScrollToEnd();
            }
        }

        private void AddDataLog(RichTextBox box, byte[] data, int offset, int count)
        {
            if (checkLogging.IsChecked.Value)
            {
                Paragraph p = new Paragraph() { Margin = new Thickness() };
                p.FontFamily = new System.Windows.Media.FontFamily("Consolas");
                char replacementChar = '.';

                if (showPlainText)
                {
                    string dataText = iso88591Win1252.GetString(data, offset, count);
                    p.Inlines.Add(new Run(dataText));
                }
                else
                {
                    char[] dataText = new char[count];
                    byte[] encBufByte = new byte[1];
                    char[] encBufChar = new char[1];
                    for (int i = 0; i < dataText.Length; i++)
                    {
                        byte b = data[offset + i];
                        char c;
                        if (b < 0x20 || b == 0x7F)
                        {
                            c = replacementChar;
                        }
                        else if (b <= 0x7F)
                        {
                            c = (char)b;
                        }
                        else
                        {
                            if (b == 0x81 || b == 0x8D || b == 0x8F || b == 90)
                            {
                                c = replacementChar;
                            }
                            else
                            {
                                encBufByte[0] = b;
                                iso88591Win1252.GetChars(encBufByte, 0, 1, encBufChar, 0);
                                c = encBufChar[0];
                            }
                        }

                        dataText[i] = c;
                    }

                    // display in Hex-Editor like style, split with 80 chars.
                    int hexStyleWidth = 20;
                    for (int i = 0; i < count; i += hexStyleWidth)
                    {
                        if (i != 0)
                        {
                            p.Inlines.Add(new Run("\r\n"));
                        }

                        StringBuilder sb = new StringBuilder();
                        for (int j = i; j < Math.Min(i + hexStyleWidth, count); j++)
                        {
                            sb.Append(data[offset + j].ToString("X2"));
                            sb.Append(" ");
                        }
                        if (count < i + hexStyleWidth)
                        {
                            sb.Append(' ', (i + hexStyleWidth - count) * 3);
                        }
                        sb.Append("  ");
                        for (int j = i; j < Math.Min(i + hexStyleWidth, count); j++)
                        {
                            sb.Append(dataText[j]);
                        }
                        p.Inlines.Add(new Run(sb.ToString()));

                    }
                }

                box.Document.Blocks.Add(p);
                box.ScrollToEnd();
            }
        }


        private class ConnectionData
        {
            public ForwardingConnection Connection;
            public bool IsAbortedOrClosed { get; set; }
            public IPEndPoint LocalLocalEndpoint { get; set; }
            public IPEndPoint LocalRemoteEndpoint { get; set; }
            public int CloseCount { get; set; }
            public long DataCountLocal { get; set; }
            public long DataCountRemote { get; set; }

            public FileStream fileIncoming;
            public FileStream fileOutgoing;
        }

        private void checkShowDataContents_CheckedUnchecked(object sender, RoutedEventArgs e)
        {
            logDataContents = checkShowDataContents.IsChecked.Value;
        }

        private void checkLogging_Changed(object sender, RoutedEventArgs e)
        {
            checkLogDataEvents.IsEnabled = checkLogging.IsChecked.Value;
        }

        private void buttonClearClient_Click(object sender, RoutedEventArgs e)
        {
            logClient.Document = new FlowDocument();
        }

        private void buttonClearServer_Click(object sender, RoutedEventArgs e)
        {
            logServer.Document = new FlowDocument();
        }

        private void checkPauseClient_Checked(object sender, RoutedEventArgs e)
        {
            forwarder.PauseClient = checkPauseClient.IsChecked.Value;
        }

        private void checkPauseServer_Checked(object sender, RoutedEventArgs e)
        {
            forwarder.PauseServer = checkPauseServer.IsChecked.Value;
        }

        private void checkLimitSpeedClient_Checked(object sender, RoutedEventArgs e)
        {
            forwarder.ThrottleSpeedClient = checkLimitSpeedClient.IsChecked.Value;
        }

        private void checkLimitSpeedServer_Checked(object sender, RoutedEventArgs e)
        {
            forwarder.ThrottleSpeedServer = checkLimitSpeedServer.IsChecked.Value;
        }

        private void checkLogDataEvents_Checked(object sender, RoutedEventArgs e)
        {
            checkShowDataContents.IsEnabled = checkLogDataEvents.IsChecked.Value;
        }

        private void CheckBox_Checked(object sender, RoutedEventArgs e)
        {
            CheckBox box = (CheckBox)sender;
            if (box.IsChecked.Value)
            {
                string hostname = Microsoft.VisualBasic.Interaction.InputBox("Enter the hostname that the server certificate will be validated against:", "Enter hostname", textHost.Text);
                if (hostname.Length == 0)
                {
                    box.IsChecked = false;
                }
                else
                {
                    sslHost = hostname;
                    box.Content = "Use SSL/TLS for remote connection - Hostname: " + hostname;
                }
            }
            else
            {
                box.Content = "Use SSL/TLS for remote connection";
                sslHost = null;
            }
        }

        private void checkUseServerSsl_Checked(object sender, RoutedEventArgs e)
        {
            CheckBox box = (CheckBox)sender;
            if (box.IsChecked.Value)
            {
                string certFingerprint = Microsoft.VisualBasic.Interaction.InputBox("Enter the fingerprint (hex; other characters will automatically be filtered) of the certificate in the local machine's or current user's Certificate Store which should be used as server certificate:\r\n\r\nNote: If using a certificate from the local machine's store, run this program as administrator.", "Enter Certificate Name");
                if (certFingerprint.Length == 0)
                {
                    box.IsChecked = false;
                    return;
                }

                X509Certificate2 resultCertificate = TcpConnectionForwarderUtils.GetCurrentUserOrLocalMachineCertificateFromFingerprint(certFingerprint);

                if (resultCertificate == null)
                {

                    MessageBox.Show("Certificate not found.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    box.IsChecked = false;
                    return;

                }
                else
                {

                    // check if we have enough privileges to get the private key of the certificate (when using the local machine's store,
                    // the program might need administrative rights to access the private key)
                    try
                    {
                        System.Security.Cryptography.AsymmetricAlgorithm am = resultCertificate.PrivateKey;
                        GC.KeepAlive(am); // Ensure the compiler does not optimize-away the above call
                    }
                    catch (Exception ex)
                    {
                        if (ExceptionUtils.ShouldExceptionBeRethrown(ex))
                            throw;

                        box.IsChecked = false;
                        MessageBox.Show("Error when retrieving the private key of the certificate. Make sure you run this program with correct privileges.\r\n\r\n"
                            + ex.GetType().ToString() + ": " + ex.Message, "Private Key", MessageBoxButton.OK, MessageBoxImage.Error);

                        return;

                    }

                }

                serverSslCertificate = resultCertificate;

            }
        }

    }
}
