using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace TcpForwarder.Utils {
    class ExceptionUtils {

        /// <summary>
        /// Returns true if a exception should be rethrown instead of be catched.
        /// </summary>
        /// <param name="ex"></param>
        /// <returns></returns>
        public static bool ShouldExceptionBeRethrown(Exception ex) {
            return ex is OutOfMemoryException || ex is StackOverflowException
                || ex is ThreadAbortException;
        }

        public static void HandleUnhandledException(Exception ex) {
            Environment.FailFast("Unhandled Exception: " + ex.GetType() + ": " + ex.Message, ex);
        }

        /// <summary>
        /// Wraps a task to catch exceptions so that the app is terminated
        /// if an unhandled exception occurs.
        /// This should be used for long-runnning tasks which are not waited for.
        /// </summary>
        /// <param name="asyncFunc"></param>
        /// <returns></returns>
        public static async Task WrapTaskForHandlingUnhandledExceptions(Func<Task> asyncFunc) {
            try {
                await asyncFunc();
            } catch (Exception ex) {
                HandleUnhandledException(ex);
            }
        }

    }
}
