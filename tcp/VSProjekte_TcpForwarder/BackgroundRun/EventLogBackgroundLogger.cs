using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TcpForwarder.BackgroundRun {
    /// <summary>
    /// IBackgroundLogger implementation that logs to a EventLog.
    /// </summary>
    class EventLogBackgroundLogger : IBackgroundLogger {

        private readonly EventLog el;

        public EventLogBackgroundLogger(EventLog el) {
            this.el = el;
        }


        public void LogInfo(string text) {
            el.WriteEntry(text, EventLogEntryType.Information);
        }

        public void LogError(string text) {
            el.WriteEntry(text, EventLogEntryType.Error);
        }
    }
}
