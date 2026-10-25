// ********************************************************************************************************************************************
//      CSpect MCP server - minimal HTTP/1.1 server for MCP's "Streamable HTTP" transport (POST JSON-RPC, JSON replies)
//      Written by:
//                  Mike Dailly
//
//      Released under the GNU 3 license - please see license file for more details
//
//      A raw TcpListener rather than HttpListener: HttpListener needs a URL reservation (admin) on Windows, and
//      behaves differently on Mono. Only listens on the loopback address.
// ********************************************************************************************************************************************
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;

namespace MCP
{
    // *********************************************************************************************************
    /// <summary>
    ///     What the server hands the MCP layer, and what it sends back
    /// </summary>
    // *********************************************************************************************************
    public class HttpExchange
    {
        public string Method;
        public string Path;
        public Dictionary<string, string> Headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        public string Body = "";

        public int Status = 200;
        public string ContentType = "application/json";
        public string ResponseBody = "";
        public Dictionary<string, string> ResponseHeaders = new Dictionary<string, string>();
    }

    // *********************************************************************************************************
    /// <summary>
    ///     The server
    /// </summary>
    // *********************************************************************************************************
    public class HttpServer
    {
        TcpListener Listener;
        Thread AcceptThread;
        volatile bool Running;
        readonly Action<HttpExchange> Handler;
        public int Port { get; private set; }

        const int MAX_BODY = 16 * 1024 * 1024;

        public HttpServer(Action<HttpExchange> _handler)
        {
            Handler = _handler;
        }

        // ****************************************************************************************
        /// <summary>
        ///     Start listening on 127.0.0.1:_port
        /// </summary>
        // ****************************************************************************************
        public void Start(int _port)
        {
            Port = _port;
            Listener = new TcpListener(IPAddress.Loopback, _port);
            Listener.Start();
            Running = true;
            AcceptThread = new Thread(AcceptLoop) { IsBackground = true, Name = "MCP accept" };
            AcceptThread.Start();
        }

        public void Stop()
        {
            Running = false;
            try { Listener?.Stop(); } catch (Exception) { }
        }

        void AcceptLoop()
        {
            while (Running)
            {
                TcpClient c;
                try { c = Listener.AcceptTcpClient(); }
                catch (Exception) { if (!Running) return; continue; }
                Thread t = new Thread(() => Connection(c)) { IsBackground = true, Name = "MCP connection" };
                t.Start();
            }
        }

        // ****************************************************************************************
        /// <summary>
        ///     One connection: requests until the client closes it (keep-alive)
        /// </summary>
        // ****************************************************************************************
        void Connection(TcpClient _client)
        {
            try
            {
                using (_client)
                using (NetworkStream ns = _client.GetStream())
                {
                    _client.NoDelay = true;
                    while (Running)
                    {
                        HttpExchange x = ReadRequest(ns);
                        if (x == null) return;
                        bool close = x.Headers.TryGetValue("Connection", out string conn) && conn.Equals("close", StringComparison.OrdinalIgnoreCase);
                        try { Handler(x); }
                        catch (Exception ex)
                        {
                            x.Status = 500;
                            x.ContentType = "text/plain";
                            x.ResponseBody = ex.Message;
                        }
                        WriteResponse(ns, x, close);
                        if (close) return;
                    }
                }
            }
            catch (Exception) { }
        }

        static string ReadLine(Stream _s)
        {
            StringBuilder sb = new StringBuilder();
            while (true)
            {
                int b = _s.ReadByte();
                if (b < 0) return sb.Length == 0 ? null : sb.ToString();
                if (b == '\n') break;
                if (b != '\r') sb.Append((char)b);
                if (sb.Length > 65536) throw new IOException("header line too long");
            }
            return sb.ToString();
        }

        static HttpExchange ReadRequest(Stream _s)
        {
            string line = ReadLine(_s);
            while (line != null && line.Length == 0) line = ReadLine(_s);      // stray CRLF between requests
            if (line == null) return null;
            string[] parts = line.Split(' ');
            if (parts.Length < 2) return null;

            HttpExchange x = new HttpExchange();
            x.Method = parts[0].ToUpperInvariant();
            x.Path = parts[1];
            int q = x.Path.IndexOf('?');
            if (q >= 0) x.Path = x.Path.Substring(0, q);

            while (true)
            {
                string h = ReadLine(_s);
                if (h == null) return null;
                if (h.Length == 0) break;
                int c = h.IndexOf(':');
                if (c > 0) x.Headers[h.Substring(0, c).Trim()] = h.Substring(c + 1).Trim();
            }

            int length = 0;
            if (x.Headers.TryGetValue("Content-Length", out string cl)) int.TryParse(cl, out length);
            if (length < 0 || length > MAX_BODY) return null;
            if (length > 0)
            {
                byte[] body = new byte[length];
                int got = 0;
                while (got < length)
                {
                    int n = _s.Read(body, got, length - got);
                    if (n <= 0) return null;
                    got += n;
                }
                x.Body = Encoding.UTF8.GetString(body);
            }
            return x;
        }

        static void WriteResponse(Stream _s, HttpExchange _x, bool _close)
        {
            byte[] body = Encoding.UTF8.GetBytes(_x.ResponseBody ?? "");
            StringBuilder sb = new StringBuilder();
            sb.Append("HTTP/1.1 ").Append(_x.Status).Append(' ').Append(Reason(_x.Status)).Append("\r\n");
            if (body.Length > 0 || _x.Status == 200) sb.Append("Content-Type: ").Append(_x.ContentType).Append("\r\n");
            sb.Append("Content-Length: ").Append(body.Length).Append("\r\n");
            foreach (KeyValuePair<string, string> h in _x.ResponseHeaders) sb.Append(h.Key).Append(": ").Append(h.Value).Append("\r\n");
            sb.Append(_close ? "Connection: close\r\n" : "Connection: keep-alive\r\n");
            sb.Append("\r\n");
            byte[] head = Encoding.ASCII.GetBytes(sb.ToString());
            _s.Write(head, 0, head.Length);
            if (body.Length > 0) _s.Write(body, 0, body.Length);
            _s.Flush();
        }

        static string Reason(int _status)
        {
            switch (_status)
            {
                case 200: return "OK";
                case 202: return "Accepted";
                case 204: return "No Content";
                case 400: return "Bad Request";
                case 403: return "Forbidden";
                case 404: return "Not Found";
                case 405: return "Method Not Allowed";
                default: return "Error";
            }
        }
    }
}
