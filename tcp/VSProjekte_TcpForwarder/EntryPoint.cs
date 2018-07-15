using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using TcpForwarder.BackgroundRun;


// TCP Connection Forwarder
// Written by Konstantin Preißer.
// Documentation: https://www.preisser-it.de/Downloads/Tcp-Connection-Forwarder


namespace TcpForwarder {
    internal class EntryPoint {
        
        /// <summary>
        /// Application Entry Point.
        /// </summary>
        [STAThreadAttribute]
        static void Main(string[] args) {

            if (Array.IndexOf(args, "-service") >= 0) {

                // Run the application as a service that has been installed with InstallUtil.exe
                BackgroundRunner.RunService();

            } else if (Array.IndexOf(args, "-console") >= 0) {
                
                // Run the application as background.
                BackgroundRunner.RunConsole();

            } else {

                // Run the program with WPF GUI.
                System.Windows.Forms.Application.EnableVisualStyles();
                System.Windows.Forms.Application.SetCompatibleTextRenderingDefault(false);
                App.Main();

            }
        }

        
    }
}
