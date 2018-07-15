using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.ServiceProcess;

using TcpForwarder.Utils;

namespace TcpForwarder.BackgroundRun {
    class TcpForwarderService : ServiceBase {

        public TcpForwarderService(Func<EventLog, Task> runServiceAsyncCallback) {
            InitializeComponent();
            this.runServiceAsyncCallback = runServiceAsyncCallback;
        }


        #region ServiceDetails


        private EventLog eventLog1;
        
        public const string MyServiceName = "TcpForwarderService";
        

        private void InitializeComponent() {
            this.eventLog1 = new EventLog();
            ((System.ComponentModel.ISupportInitialize)(this.eventLog1)).BeginInit();

            // 
            // Service1
            // 
            this.ServiceName = MyServiceName;

            ((System.ComponentModel.ISupportInitialize)(this.eventLog1)).EndInit();

            string eventLogSource = MyServiceName;

            if (!EventLog.SourceExists(eventLogSource)) {
                EventLog.CreateEventSource(eventLogSource, string.Empty);
            }

            eventLog1.Source = eventLogSource;
            eventLog1.Log = string.Empty;
        }


        #endregion


        private Func<EventLog, Task> runServiceAsyncCallback;
        private Task workTask;


        protected override void OnStart(string[] args) {
            LogInfo("Service started.");

            // Start EntryPoint.RunBackgroundAsync() in a ThreadPool context so that it can run multiple async tasks.
            workTask = Task.Run(async () => await ExceptionUtils.WrapTaskForHandlingUnhandledExceptions(async () => await runServiceAsyncCallback(eventLog1)));
        }

        protected override void OnStop() {
            LogInfo("Service stopped.");

            // TODO free resources (stop the forwarder and call workTask.Wait()).
            // However it seems at soon as the method returns the process is killed,
            // so for now leave it this way until the forwarder implements a Stop method.
        }



        private void LogInfo(string text) {
            eventLog1.WriteEntry(text, EventLogEntryType.Information);
        }

        private void LogWarning(string text) {
            eventLog1.WriteEntry(text, EventLogEntryType.Warning);
        }

        private void LogException(Exception ex) {
            eventLog1.WriteEntry("Exception: " + ex.ToString(), EventLogEntryType.Error);
        }

    }
}
