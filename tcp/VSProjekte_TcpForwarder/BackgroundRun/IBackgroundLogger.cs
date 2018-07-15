using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TcpForwarder.BackgroundRun {
    interface IBackgroundLogger {

        void LogInfo(string text);

        void LogError(string text);

    }
}
