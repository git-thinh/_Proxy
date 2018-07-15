using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using ChromeAutomation;

namespace AutomateChrome
{
    class Program
    {
        static void Main(string[] args)
        {
            //chrome.exe --remote-debugging-port=9222 --user-data-dir=C:\Users\%USERNAME%\AppData\Local\Google\Chrome\User Data
            //chrome.exe --remote-debugging-port=9222 --user-data-dir=C:\myChromeUser

            var chrome = new Chrome("http://localhost:9222");
            
            var sessions = chrome.GetAvailableSessions();
            
            Console.WriteLine("Available debugging sessions");
            foreach (var s in sessions)
                Console.WriteLine(s.url);

            if (sessions.Count == 0)
                throw new Exception("All debugging sessions are taken.");

            var sessionWSEndpoint = sessions[0].webSocketDebuggerUrl;
            chrome.SetActiveSession(sessionWSEndpoint);

            //var result = chrome.Eval("alert(window.getSelection().toString())");
            var result = chrome.Eval("window.getSelection().toString()");
            //var result = chrome.Eval("window.getSelection()"); 
            Console.WriteLine(result);

            //////// Will drive first tab session
            //////var sessionWSEndpoint = sessions[0].webSocketDebuggerUrl;

            //////chrome.SetActiveSession(sessionWSEndpoint);

            //////chrome.NavigateTo("http://www.google.com");

            //////var result = chrome.Eval("document.getElementById('lst-ib').value='Hello World'");

            //////result = chrome.Eval("document.forms[0].submit()");

            Console.ReadLine();
        }
    }
}
