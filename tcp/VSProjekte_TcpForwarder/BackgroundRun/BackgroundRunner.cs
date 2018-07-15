using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography.X509Certificates;
using System.ServiceProcess;
using System.Xml.Linq;
using System.Text;
using System.Threading.Tasks;
using TcpForwarder.Utils;

namespace TcpForwarder.BackgroundRun {
    class BackgroundRunner {

        public static void RunService() {

            Func<EventLog, Task> runServiceCallback = el => RunBackgroundAsync(new EventLogBackgroundLogger(el));

            ServiceBase[] servicesToRun = { 
				new TcpForwarderService(runServiceCallback)
			};
            ServiceBase.Run(servicesToRun);

        }


        public static void RunConsole() {
            RunBackgroundAsync(NullBackgroundLogger.Instance).Wait();
        }

        /// <summary>
        /// Run the TcpForwarder as background application. This method will also be called when running as a service.
        /// In this mode, it will read "configuration.xml" to create TcpConnectionForwarder objects and run them.
        /// </summary>
        private static async Task RunBackgroundAsync(IBackgroundLogger logger) {
            IDictionary<long, TcpConnectionForwarder> forwarders = new SortedDictionary<long, TcpConnectionForwarder>();

            List<ForwarderConfiguration> configs = GetConfiguration(logger);
            StringBuilder logSb = new StringBuilder(); // for logging all created forwarders
            Random seedGenerator = new Random();

            // Create a forwarder for each config.
            long nextId = 0;
            foreach (ForwarderConfiguration config in configs) {

                long id = nextId++;
                TcpConnectionForwarder forwarder;
                try {
                    forwarder = new TcpConnectionForwarder(seedGenerator.Next(), config.RemoteHost, config.RemotePort, config.LocalPort, config.RemoteSslHostname,
                        TcpConnectionForwarderUtils.sslProtocols, config.LocalSslCert, TcpConnectionForwarderUtils.sslProtocols, config.MaxConcurrentConnections);
                } catch (Exception ex) {
                    if (ExceptionUtils.ShouldExceptionBeRethrown(ex))
                        throw;

                    logger.LogError("Error when creating forwarder with ID " + id.ToString(CultureInfo.InvariantCulture) + ":\r\n" + FormatExceptionForLog(ex));
                    continue;
                }
                forwarders.Add(id, forwarder);
                // Log the creation of a forwarder
                if (logSb.Length > 0)
                    logSb.Append("\r\n");

                logSb.Append("[ID: ").Append(id.ToString(CultureInfo.InvariantCulture))
                    .Append("] maxConcurrentConnections = ").Append(config.MaxConcurrentConnections)
                    .Append(", remoteHost = ").Append(config.RemoteHost)
                    .Append(", remotePort = " + config.RemotePort.ToString(CultureInfo.InvariantCulture))
                    .Append(", localPort = " + config.LocalPort.ToString(CultureInfo.InvariantCulture));
                if (config.LocalSslCert != null) {
                    logSb.Append("; Server SSL certificate:\r\n").Append(config.LocalSslCert.ToString());
                }

            }
            logger.LogInfo("Created " + forwarders.Count.ToString(CultureInfo.InvariantCulture) + " TCP Connection Forwarders:\r\n" + logSb.ToString());


            // Run the forwarders.
            List<Task> forwarderTasks = new List<Task>();
            foreach (KeyValuePair<long, TcpConnectionForwarder> kvp in forwarders) {
                TcpConnectionForwarder forwarder = kvp.Value;

                Task t = forwarder.RunAsync();
                if (t.IsCompleted) {
                    // The task has already completed without an async wait - this should mean there was some exception.
                    try {
                        await t; // Await the task to catch an exception (e.g. thrown by the TcpListener).
                    } catch (Exception ex) {
                        if (ExceptionUtils.ShouldExceptionBeRethrown(ex))
                            throw;

                        logger.LogError("Error when starting forwarder with ID " + kvp.Key.ToString(CultureInfo.InvariantCulture) + ":\r\n" + FormatExceptionForLog(ex));
                    }
                } else {
                    // Wrap the task so that exceptions immediately lead to the process termination.
                    Task newt = new Func<Task>(async () => await ExceptionUtils.WrapTaskForHandlingUnhandledExceptions(async () => await t))();
                    forwarderTasks.Add(newt);
                }
            }

            // Await the forwarder tasks.
            foreach (Task t in forwarderTasks) {
                await t;
            }
        }

        private static List<ForwarderConfiguration> GetConfiguration(IBackgroundLogger errorLogger) {
            List<ForwarderConfiguration> configs = new List<ForwarderConfiguration>();

            try {
                // Read the XML configuration file.
                XDocument doc;
                using (FileStream fs = new FileStream(Path.Combine(Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location), "configuration.xml"), FileMode.Open, FileAccess.Read)) {
                    doc = XDocument.Load(fs);
                }

                XElement root = doc.Root;
                if (root.Name.LocalName == "TcpForwarderConfiguration") {
                    foreach (XNode node in root.Nodes()) {
                        try {
                            if (node is XElement && ((XElement)node).Name.LocalName == "Forwarder") {
                                // Found an entry.
                                XElement el = (XElement)node;
                                
                                XAttribute remoteHost = el.Attribute("remoteHost");
                                XAttribute remotePort = el.Attribute("remotePort");
                                XAttribute localPort = el.Attribute("localPort");
                                
                                if (remoteHost == null || remotePort == null || localPort == null)
                                    throw new ArgumentException("One of the required attributes (maxConcurrentConnections, remoteHost, remotePort, localPort) is missing.");

                                int maxConcurrentConnections;
                                XAttribute maxConcurrentConnectionsAttr = el.Attribute("maxConcurrentConnections");
                                if (maxConcurrentConnectionsAttr != null)
                                    maxConcurrentConnections = int.Parse(maxConcurrentConnectionsAttr.Value, CultureInfo.InvariantCulture);
                                else
                                    maxConcurrentConnections = 10000;

                                string remoteSslHostname = null;
                                XAttribute at = el.Attribute("remoteSslHostname");
                                if (at != null)
                                    remoteSslHostname = at.Value;

                                X509Certificate2 cert = null;
                                at = el.Attribute("localSslCertFingerprint");
                                if (at != null) {
                                    cert = TcpConnectionForwarderUtils.GetCurrentUserOrLocalMachineCertificateFromFingerprint(at.Value);
                                    if (cert == null)
                                        throw new Exception("The certificate with fingerprint \"" + at.Value + "\" was not found.");

                                    // check if we have enough privileges to get the private key of the certificate (when using the local machine's store,
                                    // the program might need administrative rights to access the private key)
                                    try {
                                        System.Security.Cryptography.AsymmetricAlgorithm am = cert.PrivateKey;
                                        GC.KeepAlive(am); // Ensure the compiler does not optimize-away the above call
                                    } catch (Exception ex) {
                                        if (ExceptionUtils.ShouldExceptionBeRethrown(ex))
                                            throw;

                                        throw new Exception("Error when retrieving the private key of the certificate. Make sure " 
                                            + "you run this program with correct privileges.\r\n\r\n"
                                            + ex.GetType().ToString() + ": " + ex.Message, ex);
                                    }
                                }

                                ForwarderConfiguration conf = new ForwarderConfiguration() {
                                    MaxConcurrentConnections = maxConcurrentConnections,
                                    RemoteHost = remoteHost.Value,
                                    RemotePort = ushort.Parse(remotePort.Value, CultureInfo.InvariantCulture),
                                    LocalPort = ushort.Parse(localPort.Value, CultureInfo.InvariantCulture),
                                    RemoteSslHostname = remoteSslHostname,
                                    LocalSslCert = cert
                                };

                                configs.Add(conf);
                            }
                        } catch (Exception ex) {
                            if (ExceptionUtils.ShouldExceptionBeRethrown(ex))
                                throw;

                            errorLogger.LogError("An error occured when parsing the following XML configuration entry:\r\n" 
                                + node.ToString() + "\r\n\r\nError Details:\r\n" + FormatExceptionForLog(ex));
                        }
                    }
                }
            } catch (Exception ex) {
                if (ExceptionUtils.ShouldExceptionBeRethrown(ex))
                    throw;

                errorLogger.LogError("An error when reading the XML configuration.\r\n\r\nError Details:\r\n" + FormatExceptionForLog(ex));
            }

            return configs;

        }


        private class ForwarderConfiguration {
            public int MaxConcurrentConnections { get; set; }
            public string RemoteHost { get; set; }
            public int RemotePort { get; set; }
            public int LocalPort { get; set; }
            public string RemoteSslHostname { get; set; }
            public X509Certificate2 LocalSslCert { get; set; }
        }


        private static string FormatExceptionForLog(Exception ex) {
            return ex.GetType().ToString() + (ex.Message == null ? "" : (": " + ex.Message));
        }

    }
}
