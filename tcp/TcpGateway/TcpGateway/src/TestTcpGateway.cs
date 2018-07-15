// Copyright 2009-2010 Christian d'Heureuse, Inventec Informatik AG, Zurich, Switzerland
// www.source-code.biz, www.inventec.ch/chdh
//
// License: GPL, GNU General Public License, V3 or later, http://www.gnu.org/licenses/gpl.html
// Please contact the author if you need another license.
//
// This module is provided "as is", without warranties of any kind.

// This is a simple test program for the TcpGateway class. It opens a
// single gateway. The port numbers are specified on the command line.

namespace Biz.Source_Code.TcpGateway
{

    using ApplicationException = System.ApplicationException;
    using Console = System.Console;
    using Exception = System.Exception;

    internal class TestTcpGateway
    {

        private const int logLevel = 9;

        private static TcpGateway tcpGateway;

        public static int Main(string[] args)
        {
            try
            {
                return Main2(args);
            }
            catch (Exception e)
            {
                Console.WriteLine("Error: " + e);
                return 99;
            }
        }

        private static int Main2(string[] args)
        {
            tcpGateway = new TcpGateway();
            ParseCommandLineArgs(args);
            tcpGateway.logger = new Logger(Console.Out, logLevel);
            tcpGateway.Open();
            Console.WriteLine("Gateway started, press Enter to close.");
            Console.ReadLine();
            tcpGateway.Close();
            return 0;
        }

        private static void ParseCommandLineArgs(string[] args)
        {
            if (args.Length != 2) throw new ApplicationException("Invalid number of command line arguments specified.");
            tcpGateway.portNo1 = int.Parse(args[0]);
            tcpGateway.portNo2 = int.Parse(args[1]);
        }

    } // end class TestTcpGateway
} // end namespace
