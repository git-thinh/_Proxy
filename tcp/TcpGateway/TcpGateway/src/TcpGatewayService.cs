// Copyright 2009-2010 Christian d'Heureuse, Inventec Informatik AG, Zurich, Switzerland
// www.source-code.biz, www.inventec.ch/chdh
//
// License: GPL, GNU General Public License, V3 or later, http://www.gnu.org/licenses/gpl.html
// Please contact the author if you need another license.
//
// This module is provided "as is", without warranties of any kind.

namespace Biz.Source_Code.TcpGateway {

using AppDomain               = System.AppDomain;
using ApplicationException    = System.ApplicationException;
using ArrayList               = System.Collections.ArrayList;
using Console                 = System.Console;
using Environment             = System.Environment;
using EventLog                = System.Diagnostics.EventLog;
using EventLogInstaller       = System.Diagnostics.EventLogInstaller;
using Exception               = System.Exception;
using Installer               = System.Configuration.Install.Installer;
using Math                    = System.Math;
using Path                    = System.IO.Path;
using ServiceAccount          = System.ServiceProcess.ServiceAccount;
using ServiceBase             = System.ServiceProcess.ServiceBase;
using ServiceInstaller        = System.ServiceProcess.ServiceInstaller;
using ServiceProcessInstaller = System.ServiceProcess.ServiceProcessInstaller;
using ServiceStartMode        = System.ServiceProcess.ServiceStartMode;
using StreamWriter            = System.IO.StreamWriter;
using XmlAttribute            = System.Xml.XmlAttribute;
using XmlDocument             = System.Xml.XmlDocument;
using XmlNode                 = System.Xml.XmlNode;
using XmlNodeList             = System.Xml.XmlNodeList;

//--- Main ---------------------------------------------------------------------

internal class TcpGatewayServiceMain {

public const string          applName = "TcpGatewayService";
public const string          applDisplayName = "Tcp Gateway Service";
public const string          applDescription = "Provides a TCP gateway.";

private class GatewayConfig {
   public int                portNo1;
   public int                portNo2; }

// Configuration parameters:
private static XmlDocument   configDoc;
private static string        logFileName;
private static int           logLevel;
private static ArrayList     gatewayConfigs;

private static bool          consoleMode;
private static string        applDir;
private static StreamWriter  logFile;
private static Logger        logger;
private static ArrayList     tcpGateways;

public static int Main() {
   try {
      Main2();
      return 0; }
    catch (Exception e) {
      LogFatalError ("Fatal error: "+e);
      return 99; }}

private static void Main2() {
   Init();
   TcpGatewayServiceController controller = new TcpGatewayServiceController();
   controller.logger = logger;
   if (consoleMode) {
      Start();
      Console.WriteLine (applName+" started, press Enter to close.");
      Console.ReadLine();
      Stop(); }
    else
      ServiceBase.Run (controller);
   controller.Dispose();
   Terminate(); }

private static void Init () {
   consoleMode = Environment.UserInteractive;
   applDir = AppDomain.CurrentDomain.SetupInformation.ApplicationBase;
   ReadConfigFile();
   logFile = new StreamWriter(logFileName, true);
   logger = new Logger(logFile, logLevel);
   logger.Log (applName+" started" + (consoleMode?" in console mode":"") + "."); }

private static void Terminate() {
   logger.Log (applName+" terminating.");
   logger = null;
   if (logFile != null) {logFile.Close(); logFile = null; }}

public static void Start() {
   tcpGateways = new ArrayList();
   foreach (GatewayConfig c in gatewayConfigs) {
      TcpGateway tcpGateway = new TcpGateway();
      tcpGateway.portNo1 = c.portNo1;
      tcpGateway.portNo2 = c.portNo2;
      tcpGateway.logger = logger;
      tcpGateway.Open();
      tcpGateways.Add (tcpGateway); }}

public static void Stop() {
   foreach (TcpGateway tcpGateway in tcpGateways)
      tcpGateway.Close();
   tcpGateways = null; }

private static void ReadConfigFile() {
   configDoc = new XmlDocument();
   string configFileName = Path.Combine(applDir, applName+".xml");
   configDoc.Load (configFileName);
   string logFileName0 = GetConfigParm("logFile");
   logFileName = Path.IsPathRooted(logFileName0) ? logFileName0 : Path.Combine(applDir, logFileName0);
   logLevel = GetConfigParmInt("logLevel");
   ReadGatewayConfigs();
   configDoc = null; }

private static string GetConfigParm (string parmName) {
   string v = GetConfigParmOpt(parmName);
   if (v == null || v.Length == 0) throw new ApplicationException("Missing configuration parameter "+parmName+".");
   return v; }
private static string GetConfigParmOpt (string parmName) {
   XmlNode node = configDoc.SelectSingleNode("/TcpGatewayServiceConfiguration/parm[@name='"+parmName+"']");
   if (node == null) return null;
   XmlAttribute attr = node.Attributes["value"];
   if (attr == null) throw new ApplicationException ("Missing value attribute for configuration parameter "+parmName+".");
   return attr.Value; }

private static int GetConfigParmInt (string parmName) {
   string s = GetConfigParm(parmName);
   try {
      return int.Parse(s); }
    catch (Exception e) {
      throw new ApplicationException ("Invalid integer value for configuration parameter "+parmName+".", e); }}

private static void ReadGatewayConfigs() {
   gatewayConfigs = new ArrayList();
   XmlNodeList nodeList = configDoc.SelectNodes("/TcpGatewayServiceConfiguration/gateway");
   foreach (XmlNode node in nodeList)
      ReadGatewayConfig (node);
   if (gatewayConfigs.Count == 0)
      throw new ApplicationException ("No gateway definitions in config file."); }

private static void ReadGatewayConfig (XmlNode node) {
   int portNo1 = GetAttrInt(node, "portNo1");
   int portNo2 = GetAttrInt(node, "portNo2");
   int multiply = GetAttrIntOpt(node, "multiply", 1);
   int portIncr = (Math.Abs(portNo2-portNo1) == 1) ? 2 : 1;
   for (int i=0; i<multiply; i++) {
      GatewayConfig c = new GatewayConfig();
      c.portNo1 = portNo1 + i * portIncr;
      c.portNo2 = portNo2 + i * portIncr;
      gatewayConfigs.Add(c); }}

private static int GetAttrInt (XmlNode node, string attrName) {
   string s = GetAttr(node, attrName);
   return DecodeAttrInt(node, attrName, s); }
private static int GetAttrIntOpt (XmlNode node, string attrName, int defaultValue) {
   string s = GetAttrOpt(node, attrName);
   if (s == null) return defaultValue;
   return DecodeAttrInt(node, attrName, s); }
private static int DecodeAttrInt(XmlNode node, string attrName, string s) {
   try {
      return int.Parse(s); }
    catch (Exception e) {
      throw new ApplicationException ("Invalid integer value for attribute "+attrName+" of configuration element "+node.Name+".", e); }}

private static string GetAttr (XmlNode node, string attrName) {
   string s = GetAttrOpt(node, attrName);
   if (s == null || s.Length == 0) throw new ApplicationException ("Missing "+attrName+" attribute for configuration element "+node.Name+".");
   return s; }
private static string GetAttrOpt (XmlNode node, string attrName) {
   XmlAttribute attr = node.Attributes[attrName];
   if (attr == null) return null;
   return attr.Value; }

private static void LogFatalError (string msg) {
   bool ok = false;
   if (consoleMode) {
      Console.WriteLine (msg);
      ok = true; }
   try {
      if (logger != null) {
         logger.Log (msg);
         ok = true; }}
    catch (Exception) {}
   if (!ok) {
      try {
         EventLog.WriteEntry (applName, msg); }
       catch (Exception) {}}}

} // end class TcpGatewayServiceMain

//--- Service controller -------------------------------------------------------

internal class TcpGatewayServiceController : ServiceBase {

public Logger                logger;

public TcpGatewayServiceController() {
   ServiceName = TcpGatewayServiceMain.applName;
   AutoLog = false;
   CanStop = true;
   CanPauseAndContinue = false; }

protected override void OnStart (string[] args) {
   logger.Log (2, "Service starting.");
   TcpGatewayServiceMain.Start(); }

protected override void OnStop() {
   logger.Log (2, "Service stopping.");
   TcpGatewayServiceMain.Stop(); }

} // end class TcpGatewayServiceController

//--- Installer ----------------------------------------------------------------

[System.ComponentModel.RunInstallerAttribute(true)]
public class ProjectInstaller : Installer {

public ProjectInstaller() {
   ServiceInstaller serviceInstaller = new ServiceInstaller();
   serviceInstaller.StartType = ServiceStartMode.Automatic;
   serviceInstaller.ServiceName = TcpGatewayServiceMain.applName;
   serviceInstaller.DisplayName = TcpGatewayServiceMain.applDisplayName;
   serviceInstaller.Description = TcpGatewayServiceMain.applDescription;
   Installers.Add (serviceInstaller);
   EventLogInstaller eventLogInstaller = new EventLogInstaller();
   eventLogInstaller.Source = TcpGatewayServiceMain.applName;
   eventLogInstaller.Log = "Application";
   Installers.Add (eventLogInstaller);
   ServiceProcessInstaller processInstaller = new ServiceProcessInstaller();
   processInstaller.Account = ServiceAccount.LocalSystem;
   Installers.Add (processInstaller); }

} // end class ProjectInstaller

//------------------------------------------------------------------------------

} // end namespace
