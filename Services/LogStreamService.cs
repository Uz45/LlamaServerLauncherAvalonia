using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace LlamaServerLauncher.Services;

public class LogStreamService : IDisposable
{
    private const int MaxLogBufferLines = 2000;
    private const int MaxClients = 10;
    private static readonly string WebSocketMagicGuid = "258EAFA5-E914-47DA-95CA-C5AB0DC85B11";

    private readonly LogService _logService;
    private readonly ConcurrentQueue<string> _logBuffer = new();
    private int _logBufferCount;
    private readonly ConcurrentDictionary<string, WebSocket> _clients = new();
    private TcpListener? _tcpListener;
    private CancellationTokenSource? _cts;
    private bool _disposed;
    private int _port;
    private string? _token;
    private int _clientIdCounter;

    public bool IsRunning => _tcpListener != null;
    public int ConnectedClientCount => _clients.Count;
    public int Port => _port;

    public event Action? ClientCountChanged;

    public LogStreamService(LogService logService)
    {
        _logService = logService;
    }

    public void Start(int port, string? token)
    {
        if (_tcpListener != null) return;

        _port = port;
        _token = string.IsNullOrWhiteSpace(token) ? null : token.Trim();

        try
        {
            _tcpListener = new TcpListener(IPAddress.Any, _port);
            _tcpListener.Start();
            _cts = new CancellationTokenSource();
            _ = AcceptLoopAsync(_cts.Token);
            _logService.LogReceived += OnLogReceived;
            _logService.AppLog($"Log stream server started on port {_port}");
        }
        catch (Exception ex)
        {
            _tcpListener = null;
            _logService.Error($"Failed to start log stream server: {ex.Message}");
            throw;
        }
    }

    public void Stop()
    {
        if (_tcpListener == null) return;

        _logService.LogReceived -= OnLogReceived;
        _cts?.Cancel();

        foreach (var kvp in _clients.ToList())
        {
            try { kvp.Value.Dispose(); } catch { }
        }
        _clients.Clear();

        try { _tcpListener?.Stop(); } catch { }
        _tcpListener = null;
        _cts?.Dispose();
        _cts = null;

        _logService.AppLog("Log stream server stopped");
        ClientCountChanged?.Invoke();
    }

    private async Task AcceptLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var tcpClient = await _tcpListener!.AcceptTcpClientAsync(ct);
                _ = HandleClientAsync(tcpClient, ct);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (ObjectDisposedException)
            {
                break;
            }
            catch (Exception)
            {
            }
        }
    }

    private async Task HandleClientAsync(TcpClient tcpClient, CancellationToken ct)
    {
        NetworkStream? stream = null;
        try
        {
            tcpClient.NoDelay = true;
            stream = tcpClient.GetStream();
            stream.ReadTimeout = 5000;
            stream.WriteTimeout = 5000;

            var request = await ReadHttpRequestAsync(stream, ct);
            if (request == null)
            {
                tcpClient.Close();
                return;
            }

            var path = request.GetValueOrDefault("Path");
            var tokenFromQuery = request.GetValueOrDefault("Token");

            if (path == "/ws")
            {
                if (_token != null && tokenFromQuery != _token)
                {
                    await SendHttpResponseAsync(stream, "403 Forbidden", "text/plain", "Invalid token");
                    tcpClient.Close();
                    return;
                }

                if (_clients.Count >= MaxClients)
                {
                    await SendHttpResponseAsync(stream, "429 Too Many Requests", "text/plain", "Max clients reached");
                    tcpClient.Close();
                    return;
                }

                var secWebSocketKey = request.GetValueOrDefault("Sec-WebSocket-Key");
                if (string.IsNullOrEmpty(secWebSocketKey))
                {
                    await SendHttpResponseAsync(stream, "400 Bad Request", "text/plain", "Missing Sec-WebSocket-Key");
                    tcpClient.Close();
                    return;
                }

                var acceptKey = ComputeWebSocketAcceptKey(secWebSocketKey);
                await SendWebSocketUpgradeResponseAsync(stream, acceptKey);

                stream.ReadTimeout = Timeout.Infinite;
                stream.WriteTimeout = Timeout.Infinite;

                var webSocket = WebSocket.CreateFromStream(stream, true, null, TimeSpan.FromMinutes(2));
                var clientId = Interlocked.Increment(ref _clientIdCounter);
                var clientKey = clientId.ToString();

                _clients[clientKey] = webSocket;
                ClientCountChanged?.Invoke();

                try
                {
                    await SendHistoryAsync(webSocket, ct);
                    await ClientReceiveLoopAsync(webSocket, ct);
                }
                finally
                {
                    _clients.TryRemove(clientKey, out _);
                    ClientCountChanged?.Invoke();
                    try { webSocket.Dispose(); } catch { }
                }
            }
            else if (path == "/api/logs/history")
            {
                if (_token != null && tokenFromQuery != _token)
                {
                    await SendHttpResponseAsync(stream, "403 Forbidden", "text/plain", "Invalid token");
                    tcpClient.Close();
                    return;
                }

                var lines = _logBuffer.ToArray();
                var json = "[\n" + string.Join(",\n", lines.Select(l => "\"" + EscapeJsonString(l) + "\"")) + "\n]";
                await SendHttpResponseAsync(stream, "200 OK", "application/json", json);
                tcpClient.Close();
            }
            else if (path == "/api/status")
            {
                if (_token != null && tokenFromQuery != _token)
                {
                    await SendHttpResponseAsync(stream, "403 Forbidden", "text/plain", "Invalid token");
                    tcpClient.Close();
                    return;
                }

                var statusJson = $"{{\"clientCount\":{_clients.Count},\"port\":{_port}}}";
                await SendHttpResponseAsync(stream, "200 OK", "application/json", statusJson);
                tcpClient.Close();
            }
            else
            {
                var html = GetHtmlPage();
                await SendHttpResponseAsync(stream, "200 OK", "text/html; charset=utf-8", html);
                tcpClient.Close();
            }
        }
        catch (Exception)
        {
            tcpClient.Close();
        }
        finally
        {
            stream?.Dispose();
        }
    }

    private async Task<Dictionary<string, string>?> ReadHttpRequestAsync(NetworkStream stream, CancellationToken ct)
    {
        var buffer = new byte[4096];
        var totalRead = 0;

        while (totalRead < buffer.Length)
        {
            var bytesRead = await stream.ReadAsync(buffer.AsMemory(totalRead, buffer.Length - totalRead), ct);
            if (bytesRead == 0) return null;
            totalRead += bytesRead;

            var headerEnd = System.Text.Encoding.UTF8.GetString(buffer, 0, totalRead).IndexOf("\r\n\r\n");
            if (headerEnd >= 0) break;
        }

        var headerText = System.Text.Encoding.UTF8.GetString(buffer, 0, totalRead);
        var lines = headerText.Split("\r\n");
        if (lines.Length == 0) return null;

        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var firstLine = lines[0];
        var parts = firstLine.Split(' ');
        if (parts.Length < 2) return null;

        var fullPath = parts[1];
        var queryIndex = fullPath.IndexOf('?');
        if (queryIndex >= 0)
        {
            result["Path"] = fullPath[..queryIndex];
            var query = fullPath[(queryIndex + 1)..];
            foreach (var param in query.Split('&'))
            {
                var eqIndex = param.IndexOf('=');
                if (eqIndex >= 0)
                {
                    var key = Uri.UnescapeDataString(param[..eqIndex]);
                    var val = Uri.UnescapeDataString(param[(eqIndex + 1)..]);
                    if (key.Equals("token", StringComparison.OrdinalIgnoreCase))
                        result["Token"] = val;
                }
            }
        }
        else
        {
            result["Path"] = fullPath;
        }

        for (var i = 1; i < lines.Length; i++)
        {
            var colonIndex = lines[i].IndexOf(':');
            if (colonIndex > 0)
            {
                var headerName = lines[i][..colonIndex].Trim();
                var headerValue = lines[i][(colonIndex + 1)..].Trim();
                result[headerName] = headerValue;
            }
        }

        return result;
    }

    private static async Task SendHttpResponseAsync(NetworkStream stream, string status, string contentType, string body)
    {
        var bodyBytes = System.Text.Encoding.UTF8.GetBytes(body);
        var header = $"HTTP/1.1 {status}\r\nContent-Type: {contentType}\r\nContent-Length: {bodyBytes.Length}\r\nConnection: close\r\n\r\n";
        var headerBytes = System.Text.Encoding.UTF8.GetBytes(header);
        await stream.WriteAsync(headerBytes);
        await stream.WriteAsync(bodyBytes);
        await stream.FlushAsync();
    }

    private static async Task SendWebSocketUpgradeResponseAsync(NetworkStream stream, string acceptKey)
    {
        var response = $"HTTP/1.1 101 Switching Protocols\r\nUpgrade: websocket\r\nConnection: Upgrade\r\nSec-WebSocket-Accept: {acceptKey}\r\n\r\n";
        var bytes = System.Text.Encoding.UTF8.GetBytes(response);
        await stream.WriteAsync(bytes);
        await stream.FlushAsync();
    }

    private static string ComputeWebSocketAcceptKey(string secWebSocketKey)
    {
        var combined = secWebSocketKey + WebSocketMagicGuid;
        var hash = SHA1.HashData(System.Text.Encoding.ASCII.GetBytes(combined));
        return Convert.ToBase64String(hash);
    }

    private async Task SendHistoryAsync(WebSocket webSocket, CancellationToken ct)
    {
        if (_logBuffer.IsEmpty) return;
        var lines = _logBuffer.ToArray();
        var combined = string.Join("\n", lines);
        var bytes = System.Text.Encoding.UTF8.GetBytes(combined);
        var segment = new ArraySegment<byte>(bytes);
        await webSocket.SendAsync(segment, WebSocketMessageType.Text, true, ct);
    }

    private static async Task ClientReceiveLoopAsync(WebSocket webSocket, CancellationToken ct)
    {
        var buffer = new byte[1024];
        try
        {
            while (webSocket.State == WebSocketState.Open && !ct.IsCancellationRequested)
            {
                await webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), ct);
            }
        }
        catch (OperationCanceledException) { }
        catch (WebSocketException) { }
        catch (Exception) { }
    }

    private void OnLogReceived(object? sender, string logLine)
    {
        _logBuffer.Enqueue(logLine);
        if (Interlocked.Increment(ref _logBufferCount) > MaxLogBufferLines)
        {
            _logBuffer.TryDequeue(out _);
            Interlocked.Decrement(ref _logBufferCount);
        }

        BroadcastLineAsync(logLine);
    }

    private void BroadcastLineAsync(string line)
    {
        if (_clients.IsEmpty) return;

        var bytes = System.Text.Encoding.UTF8.GetBytes(line);
        var segment = new ArraySegment<byte>(bytes);

        foreach (var kvp in _clients.ToList())
        {
            var ws = kvp.Value;
            _ = SendToClientAsync(ws, segment);
        }
    }

    private static async Task SendToClientAsync(WebSocket ws, ArraySegment<byte> data)
    {
        try
        {
            if (ws.State == WebSocketState.Open)
                await ws.SendAsync(data, WebSocketMessageType.Text, true, CancellationToken.None);
        }
        catch { }
    }

    private static string EscapeJsonString(string s)
    {
        var sb = new StringBuilder(s.Length);
        foreach (var c in s)
        {
            switch (c)
            {
                case '\\': sb.Append("\\\\"); break;
                case '"': sb.Append("\\\""); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break;
                case '\t': sb.Append("\\t"); break;
                default:
                    if (c < 32)
                        sb.Append($"\\u{(int)c:X4}");
                    else
                        sb.Append(c);
                    break;
            }
        }
        return sb.ToString();
    }

    private static string GetHtmlPage()
    {
        return @"<!DOCTYPE html>
<html lang='en'>
<head>
<meta charset='UTF-8'>
<meta name='viewport' content='width=device-width, initial-scale=1.0'>
<title>Log Stream — llama-server launcher</title>
<style>
*{margin:0;padding:0;box-sizing:border-box}
body{background:#1a1a2e;color:#d4d4d4;font-family:'Consolas','SF Mono','Monaco','Menlo',monospace;font-size:13px;height:100vh;display:flex;flex-direction:column}
.header{background:#16213e;padding:8px 16px;display:flex;align-items:center;gap:12px;border-bottom:1px solid #0f3460;flex-shrink:0;flex-wrap:wrap}
.header h1{font-size:14px;color:#e0e0e0;white-space:nowrap}
.status{display:flex;align-items:center;gap:6px;font-size:12px}
.dot{width:8px;height:8px;border-radius:50%;display:inline-block}
.dot.on{background:#4ecca3}
.dot.off{background:#e74c3c}
.client-count{color:#888;font-size:11px}
.controls{display:flex;gap:8px;margin-left:auto}
.controls button{background:#0f3460;color:#d4d4d4;border:1px solid #1a4080;padding:4px 12px;border-radius:4px;cursor:pointer;font-family:inherit;font-size:12px}
.controls button:hover{background:#1a4080}
.controls button.active{background:#4ecca3;color:#1a1a2e;border-color:#4ecca3}
.controls label{display:flex;align-items:center;gap:4px;font-size:12px;color:#888;cursor:pointer}
.controls input[type=checkbox]{cursor:pointer}
#log{flex:1;overflow-y:auto;padding:8px 16px;white-space:pre-wrap;word-break:break-all;line-height:1.5}
#log .line{display:block}
#log .line:hover{background:rgba(255,255,255,0.03)}
.empty{color:#555;font-style:italic;padding:20px;text-align:center}
.token-prompt{position:fixed;top:0;left:0;right:0;bottom:0;background:rgba(0,0,0,0.7);display:flex;align-items:center;justify-content:center;z-index:100}
.token-prompt.hidden{display:none}
.token-box{background:#16213e;padding:24px;border-radius:8px;border:1px solid #0f3460;text-align:center;min-width:300px}
.token-box h2{margin-bottom:12px;font-size:16px;color:#e0e0e0}
.token-box input{width:100%;padding:8px;background:#1a1a2e;border:1px solid #0f3460;color:#d4d4d4;border-radius:4px;font-family:inherit;font-size:14px;margin-bottom:12px}
.token-box button{padding:8px 24px;background:#4ecca3;color:#1a1a2e;border:none;border-radius:4px;cursor:pointer;font-family:inherit;font-size:14px;font-weight:bold}
.token-box button:hover{background:#3db892}
</style>
</head>
<body>
<div class='header'>
<h1>llama-server launcher — Log Stream</h1>
<div class='status'><span class='dot' id='dot'></span><span id='statusText'>Connecting...</span></div>
<span class='client-count' id='clientCount'></span>
<div class='controls'>
<label><input type='checkbox' id='autoScroll' checked> Auto-scroll</label>
<button id='btnClear'>Clear</button>
<button id='btnReconnect'>Reconnect</button>
</div>
</div>
<div id='log'></div>
<div class='token-prompt hidden' id='tokenPrompt'>
<div class='token-box'>
<h2>Enter Access Token</h2>
<input type='text' id='tokenInput' placeholder='Token' autofocus>
<button id='tokenSubmit'>Connect</button>
</div>
</div>
<script>
(function(){
var log=document.getElementById('log');
var dot=document.getElementById('dot');
var statusText=document.getElementById('statusText');
var clientCount=document.getElementById('clientCount');
var autoScrollCb=document.getElementById('autoScroll');
var btnClear=document.getElementById('btnClear');
var btnReconnect=document.getElementById('btnReconnect');
var tokenPrompt=document.getElementById('tokenPrompt');
var tokenInput=document.getElementById('tokenInput');
var tokenSubmit=document.getElementById('tokenSubmit');
var ws=null;
var lineCount=0;
var maxLines=5000;
var token=localStorage.getItem('logstream_token')||'';

function getWsUrl(){
var loc=window.location;
var proto=loc.protocol==='https:'?'wss:':'ws:';
var t=token?'?token='+encodeURIComponent(token):'';
return proto+'//'+loc.host+'/ws'+t;
}

function setStatus(connected){
dot.className='dot '+(connected?'on':'off');
statusText.textContent=connected?'Connected':'Disconnected';
}

function addLine(text){
if(lineCount>=maxLines){
var first=log.firstChild;
if(first)log.removeChild(first);
lineCount--;
}
var span=document.createElement('span');
span.className='line';
span.textContent=text;
log.appendChild(span);
lineCount++;
if(autoScrollCb.checked)log.scrollTop=log.scrollHeight;
}

function connect(){
if(ws){try{ws.close();}catch(e){}}
ws=new WebSocket(getWsUrl());
ws.onopen=function(){
setStatus(true);
var url=window.location.href;
if(url.indexOf('?token=')===-1&&url.indexOf('&token=')===-1){
try{history.replaceState(null,'',url);}catch(e){}
}
};
ws.onclose=function(){
setStatus(false);
setTimeout(connect,3000);
};
ws.onerror=function(){
setStatus(false);
};
ws.onmessage=function(e){
if(typeof e.data==='string'){
var lines=e.data.split('\n');
for(var i=0;i<lines.length;i++)addLine(lines[i]);
}
};
}

function showTokenPrompt(){
tokenPrompt.classList.remove('hidden');
tokenInput.focus();
}

function hideTokenPrompt(){
tokenPrompt.classList.add('hidden');
}

function tryConnect(){
token=tokenInput.value.trim();
localStorage.setItem('logstream_token',token);
hideTokenPrompt();
connect();
}

btnClear.onclick=function(){log.innerHTML='';lineCount=0;};
btnReconnect.onclick=function(){if(ws)ws.close();};

tokenSubmit.onclick=tryConnect;
tokenInput.onkeydown=function(e){if(e.key==='Enter')tryConnect();};

if(token){
connect();
}else{
var urlParams=new URLSearchParams(window.location.search);
var urlToken=urlParams.get('token');
if(urlToken){
token=urlToken;
localStorage.setItem('logstream_token',token);
try{
var cleanUrl=window.location.pathname;
history.replaceState(null,'',cleanUrl);
}catch(e){}
connect();
}else{
showTokenPrompt();
}
}
})();
</script>
</body>
</html>";
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Stop();
    }
}
