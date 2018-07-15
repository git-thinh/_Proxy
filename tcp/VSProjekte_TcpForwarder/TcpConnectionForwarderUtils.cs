using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;

namespace TcpForwarder {
    internal class TcpConnectionForwarderUtils {

        /// <summary>
        /// SSL protocols that are used as server and as client.
        /// </summary>
        public static readonly SslProtocols sslProtocols = // TODO: make this configurable
            SslProtocols.Tls // -> SSL 3.1
            | SslProtocols.Tls11
            | SslProtocols.Tls12;


        /// <summary>
        /// Returns the X509 certificate with the given fingerprint from the current user's or the local machine's certificate store,
        /// or null if such a certificate was not found.
        /// 
        /// Note that it does not check if the user has permission to access the private key of the certificate.
        /// </summary>
        /// <param name="certFingerprint">the fingerprint in hex format (other characters will be filtered automatically)</param>
        /// <returns></returns>
        public static X509Certificate2 GetCurrentUserOrLocalMachineCertificateFromFingerprint(string certFingerprint) {
            // filter non-hex characters
            Regex reg = new Regex("[^0-9a-fA-F]+");
            certFingerprint = reg.Replace(certFingerprint, "");

            StoreLocation[] locations = new StoreLocation[] { StoreLocation.CurrentUser, StoreLocation.LocalMachine };
            X509Certificate2 resultCertificate = null;
            for (int i = 0; i < locations.Length; i++) {
                X509Store store = new X509Store(locations[i]);
                try {
                    store.Open(OpenFlags.ReadOnly);

                    X509Certificate2Collection result = store.Certificates.Find(X509FindType.FindByThumbprint, certFingerprint, false);
                    if (result.Count != 0) {
                        resultCertificate = result[0];
                    }

                } finally {
                    store.Close();
                }
            }

            return resultCertificate;
        }

    }
}
