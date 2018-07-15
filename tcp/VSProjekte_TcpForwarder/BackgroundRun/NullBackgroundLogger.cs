using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TcpForwarder.BackgroundRun {
    /// <summary>
    /// IBackgroundLogger implementation that is a NOOP.
    /// </summary>
    class NullBackgroundLogger : IBackgroundLogger {

        public static readonly NullBackgroundLogger Instance = new NullBackgroundLogger();


        private NullBackgroundLogger() {

        }


        public void LogInfo(string text) {
            // NOOP
        }

        public void LogError(string text) {
            // NOOP
        }
    }
}
