// Copyright 2009-2010 Christian d'Heureuse, Inventec Informatik AG, Zurich, Switzerland
// www.source-code.biz, www.inventec.ch/chdh
//
// License: GPL, GNU General Public License, V3 or later, http://www.gnu.org/licenses/gpl.html
// Please contact the author if you need another license.
//
// This module is provided "as is", without warranties of any kind.

// This is a stress test program for the TCP gateway module. It connects to
// one of the two ports of the gateway and sends and receives data. A second
// instance of the program must be used to connect to the other port of
// the gateway. The host name and port number are specified on the command
// line.

using AddressFamily          = System.Net.Sockets.AddressFamily;
using ApplicationException   = System.ApplicationException;
using BitConverter           = System.BitConverter;
using Console                = System.Console;
using Dns                    = System.Net.Dns;
using Exception              = System.Exception;
using IPAddress              = System.Net.IPAddress;
using IPEndPoint             = System.Net.IPEndPoint;
using IPHostEntry            = System.Net.IPHostEntry;
using Math                   = System.Math;
using ProtocolType           = System.Net.Sockets.ProtocolType;
using Random                 = System.Random;
using Socket                 = System.Net.Sockets.Socket;
using SocketException        = System.Net.Sockets.SocketException;
using SocketError            = System.Net.Sockets.SocketError;
using SocketFlags            = System.Net.Sockets.SocketFlags;
using SocketOptionLevel      = System.Net.Sockets.SocketOptionLevel;
using SocketOptionName       = System.Net.Sockets.SocketOptionName;
using SocketShutdown         = System.Net.Sockets.SocketShutdown;
using SocketType             = System.Net.Sockets.SocketType;
using Thread                 = System.Threading.Thread;

internal class TcpStressTest {

private const int            maxDataLen = 1024*1024;
private const int            maxBlockLen = 2048;

private static string        hostName;
private static int           portNo;
private static IPAddress     hostIpAddr;
private static Random        random = new Random();
private static byte[]        blockBuf = new byte[maxBlockLen];

public static int Main (string[] args) {
   try {
      return Main2(args); }
    catch (Exception e) {
      Console.WriteLine ("Error: "+e);
      return 99; }}

private static int Main2 (string[] args) {
   ParseCommandLineArgs (args);
   hostIpAddr = Resolve(hostName);
   while (true) {
      ConnectAndTransfer();
      Console.Write ("|"); }}

private static void ParseCommandLineArgs (string[] args) {
   if (args.Length != 2) throw new ApplicationException ("Invalid number of command line arguments specified.");
   hostName = args[0];
   portNo = int.Parse(args[1]); }

private static void ConnectAndTransfer() {
   using (Socket socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp)) {
      IPEndPoint ep = new IPEndPoint(hostIpAddr, portNo);
      socket.Connect (ep);
      Transfer (socket);
      socket.Close(); }}

private static void Transfer (Socket socket) {
   int txDataLen = random.Next(maxDataLen);
   int rxDataLen;
   TransferHeaders (socket, txDataLen, out rxDataLen);
   // System.Console.WriteLine ("txDataLen="+txDataLen+" rxDataLen="+rxDataLen);
   socket.Blocking = false;
   socket.SetSocketOption (SocketOptionLevel.Tcp, SocketOptionName.NoDelay, 1);
      // Disable the Nagle algorithm for send coalescing.
   int txPos = 0;
   int rxPos = 0;
   while (txPos < txDataLen || rxPos < rxDataLen) {
      // System.Console.WriteLine ("txPos="+txPos+"/"+txDataLen+" rxPos="+rxPos+"/"+rxDataLen);
      int txReqLen = Math.Min(txDataLen - txPos, 1+random.Next(maxBlockLen));
      int txTrLen = Send(socket, txReqLen);
      txPos += txTrLen;
      int rxReqLen = Math.Min(rxDataLen - rxPos, maxBlockLen);
      int rxTrLen = Receive(socket, rxReqLen);
      rxPos += rxTrLen;
      if (random.Next(1000) == 0)
         Thread.Sleep (50);
       else if ((txTrLen < txReqLen && rxTrLen < rxReqLen) || random.Next(50) == 0)
         Thread.Sleep (1); }
   socket.Shutdown(SocketShutdown.Both); }

private static int Send (Socket socket, int reqLen) {
   if (reqLen <= 0) return 0;
   SocketError errorCode;
   int trLen = socket.Send(blockBuf, 0, reqLen, SocketFlags.None, out errorCode);
   switch (errorCode) {
      case SocketError.Success: break;
      case SocketError.WouldBlock: return 0;
      default: throw new ApplicationException ("Socket.Send() returned SocketError "+errorCode+"."); }
   return trLen; }

private static int Receive (Socket socket, int reqLen) {
   if (reqLen <= 0) return 0;
   SocketError errorCode;
   int trLen = socket.Receive(blockBuf, 0, reqLen, SocketFlags.None, out errorCode);
   switch (errorCode) {
      case SocketError.Success: break;
      case SocketError.WouldBlock: return 0;
      default: throw new ApplicationException ("Socket.Receive() returned SocketError "+errorCode+"."); }
   if (trLen == 0) throw new ApplicationException ("Socket.Receive() returned 0.");
   return trLen; }

private static void TransferHeaders (Socket socket, int txDataLen, out int rxDataLen) {
   socket.Blocking = true;
   socket.Send (BitConverter.GetBytes(txDataLen));
   byte[] buf4 = new byte[4];
   int len = socket.Receive(buf4);
   if (len != 4) throw new ApplicationException ("Incomplete header received, len="+len);
   rxDataLen = BitConverter.ToInt32(buf4, 0); }

private static IPAddress Resolve (string hostName) {
   IPHostEntry he = Dns.GetHostEntry(hostName);
   if (he.AddressList.Length < 1) throw new ApplicationException("IPHostEntry.AdressList is empty.");
   return he.AddressList[0]; }

} // end of main class
