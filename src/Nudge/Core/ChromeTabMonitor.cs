using System.Net;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Nudge.Core;

/// <summary>
/// Listens on a localhost WebSocket port for browser tab activity reports from
/// the Nudge Chrome extension. The extension sends the active tab's URL and title;
/// this monitor matches them against configured glob/wildcard patterns to determine
/// if a "browser-tab" source is active.
/// </summary>
public class ChromeTabMonitor : IDisposable
{
    private readonly int _port;
    private HttpListener? _httpListener;
    private CancellationTokenSource? _cts;
    private Task? _listenTask;
    private bool _disposed;

    // Lock protecting _currentTab and _allPatterns
    private readonly object _tabLock = new();

    // The most recent tab info reported by the extension.
    // Null means no extension connected or extension reported inactive.
    private TabInfo? _currentTab;

    // All tab patterns from all browser-tab sources across all tracked apps.
    // Updated by NudgeEngine when config loads or reloads.
    private List<string> _allPatterns = new();

    // The active WebSocket connection (if any) for sending match responses.
    private WebSocket? _activeWebSocket;
    private readonly object _wsLock = new();

    // Cache of compiled regex patterns from glob strings (pattern -> regex)
    private readonly Dictionary<string, Regex> _patternCache = new();

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    public ChromeTabMonitor(int port)
    {
        _port = port;
    }

    /// <summary>
    /// Updates the set of all tab patterns from config. Called by NudgeEngine
    /// at startup and whenever the config is reloaded.
    /// </summary>
    public void UpdatePatterns(List<string> allPatterns)
    {
        lock (_tabLock)
        {
            _allPatterns = allPatterns;
        }
    }

    /// <summary>
    /// Starts the WebSocket server listening on localhost:{port}.
    /// </summary>
    public void Start()
    {
        if (_port <= 0)
        {
            System.Diagnostics.Debug.WriteLine("[Nudge] ChromeTabMonitor disabled (port <= 0).");
            return;
        }

        _cts = new CancellationTokenSource();
        _httpListener = new HttpListener();
        _httpListener.Prefixes.Add($"http://localhost:{_port}/");

        try
        {
            _httpListener.Start();
            _listenTask = Task.Run(() => AcceptLoop(_cts.Token));
            System.Diagnostics.Debug.WriteLine($"[Nudge] ChromeTabMonitor started on port {_port}.");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine(
                $"[Nudge] ChromeTabMonitor failed to start on port {_port}: {ex.Message}");
        }
    }

    /// <summary>
    /// Stops the WebSocket server and cleans up resources.
    /// </summary>
    public void Stop()
    {
        _cts?.Cancel();

        try
        {
            _httpListener?.Stop();
        }
        catch
        {
            // Ignore errors during shutdown
        }

        try
        {
            _listenTask?.Wait(TimeSpan.FromSeconds(2));
        }
        catch
        {
            // Ignore timeout / cancellation
        }

        lock (_tabLock)
        {
            _currentTab = null;
        }

        System.Diagnostics.Debug.WriteLine("[Nudge] ChromeTabMonitor stopped.");
    }

    /// <summary>
    /// Checks whether the current active browser tab matches any of the given wildcard patterns.
    /// Patterns use * (match any characters) and ? (match single character) wildcards.
    /// Both the tab title and URL are tested against each pattern.
    /// </summary>
    /// <param name="patterns">List of glob patterns to match (e.g., "*Tibia*", "*tibia.com*").</param>
    /// <returns>True if the active tab matches at least one pattern.</returns>
    public bool IsTabMatchActive(List<string> patterns)
    {
        TabInfo? tab;
        lock (_tabLock)
        {
            tab = _currentTab;
        }

        // No tab info means no extension connected or tab is not active
        if (tab == null || !tab.Active)
            return false;

        foreach (var pattern in patterns)
        {
            var regex = GetOrCreateRegex(pattern);

            // Match against both title and URL (case-insensitive)
            if ((!string.IsNullOrEmpty(tab.Title) && regex.IsMatch(tab.Title))
                || (!string.IsNullOrEmpty(tab.Url) && regex.IsMatch(tab.Url)))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Accept loop: waits for incoming HTTP connections and upgrades them to WebSocket.
    /// </summary>
    private async Task AcceptLoop(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var context = await _httpListener!.GetContextAsync().ConfigureAwait(false);

                if (context.Request.IsWebSocketRequest)
                {
                    // Handle WebSocket connection in the background
                    _ = Task.Run(() => HandleWebSocket(context, ct), ct);
                }
                else
                {
                    // Add CORS headers so the Chrome extension popup can fetch this endpoint.
                    // The popup runs in a chrome-extension:// origin, which is cross-origin
                    // relative to localhost.
                    context.Response.Headers.Add("Access-Control-Allow-Origin", "*");
                    context.Response.Headers.Add("Access-Control-Allow-Methods", "GET, OPTIONS");
                    context.Response.Headers.Add("Access-Control-Allow-Headers", "Content-Type");

                    if (context.Request.HttpMethod == "OPTIONS")
                    {
                        // Handle CORS preflight request
                        context.Response.StatusCode = 204;
                        context.Response.Close();
                    }
                    else
                    {
                        // Return a simple status response
                        context.Response.StatusCode = 200;
                        context.Response.ContentType = "text/plain";
                        var body = Encoding.UTF8.GetBytes("Nudge ChromeTabMonitor active");
                        await context.Response.OutputStream.WriteAsync(body, ct).ConfigureAwait(false);
                        context.Response.Close();
                    }
                }
            }
            catch (HttpListenerException) when (ct.IsCancellationRequested)
            {
                // Expected when stopping
                break;
            }
            catch (ObjectDisposedException)
            {
                // HttpListener was disposed during shutdown
                break;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"[Nudge] ChromeTabMonitor accept error: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Handles a single WebSocket connection from the Chrome extension.
    /// Receives JSON messages with tab info and updates the current tab state.
    /// </summary>
    private async Task HandleWebSocket(HttpListenerContext context, CancellationToken ct)
    {
        WebSocket? ws = null;
        try
        {
            var wsContext = await context.AcceptWebSocketAsync(null).ConfigureAwait(false);
            ws = wsContext.WebSocket;

            // Store reference so ProcessMessage can send responses
            lock (_wsLock)
            {
                _activeWebSocket = ws;
            }

            System.Diagnostics.Debug.WriteLine("[Nudge] Chrome extension connected via WebSocket.");

            var buffer = new byte[4096];

            while (ws.State == WebSocketState.Open && !ct.IsCancellationRequested)
            {
                var result = await ws.ReceiveAsync(new ArraySegment<byte>(buffer), ct).ConfigureAwait(false);

                if (result.MessageType == WebSocketMessageType.Close)
                {
                    await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closing", ct)
                        .ConfigureAwait(false);
                    break;
                }

                if (result.MessageType == WebSocketMessageType.Text)
                {
                    var json = Encoding.UTF8.GetString(buffer, 0, result.Count);
                    await ProcessMessageAsync(json, ws, ct).ConfigureAwait(false);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Expected during shutdown
        }
        catch (WebSocketException ex)
        {
            System.Diagnostics.Debug.WriteLine(
                $"[Nudge] WebSocket error: {ex.Message}");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine(
                $"[Nudge] ChromeTabMonitor connection error: {ex.Message}");
        }
        finally
        {
            // On disconnect, clear tab state and WebSocket reference
            lock (_tabLock)
            {
                _currentTab = null;
            }

            lock (_wsLock)
            {
                if (_activeWebSocket == ws)
                    _activeWebSocket = null;
            }

            ws?.Dispose();
            System.Diagnostics.Debug.WriteLine("[Nudge] Chrome extension disconnected.");
        }
    }

    /// <summary>
    /// Parses an incoming JSON message from the extension, updates current tab state,
    /// and sends back a { "matched": true/false } response so the extension can
    /// show a badge indicator when the active tab is being tracked.
    /// Expected input format: { "url": "...", "title": "...", "active": true/false }
    /// </summary>
    private async Task ProcessMessageAsync(string json, WebSocket ws, CancellationToken ct)
    {
        try
        {
            var tabInfo = JsonSerializer.Deserialize<TabInfo>(json, JsonOptions);
            if (tabInfo == null)
                return;

            bool matched;
            lock (_tabLock)
            {
                _currentTab = tabInfo;
                matched = CheckTabAgainstPatterns(tabInfo, _allPatterns);
            }

            System.Diagnostics.Debug.WriteLine(
                $"[Nudge] Tab update: active={tabInfo.Active}, matched={matched}, title=\"{tabInfo.Title}\", url=\"{tabInfo.Url}\"");

            // Send match result back to the extension
            if (ws.State == WebSocketState.Open)
            {
                var response = JsonSerializer.Serialize(new { matched }, JsonOptions);
                var responseBytes = Encoding.UTF8.GetBytes(response);
                await ws.SendAsync(
                    new ArraySegment<byte>(responseBytes),
                    WebSocketMessageType.Text,
                    true, ct).ConfigureAwait(false);
            }
        }
        catch (JsonException ex)
        {
            System.Diagnostics.Debug.WriteLine(
                $"[Nudge] Failed to parse tab message: {ex.Message}");
        }
        catch (WebSocketException ex)
        {
            System.Diagnostics.Debug.WriteLine(
                $"[Nudge] Failed to send match response: {ex.Message}");
        }
    }

    /// <summary>
    /// Checks whether a tab matches any of the given patterns.
    /// Must be called under _tabLock if accessing shared pattern list.
    /// </summary>
    private bool CheckTabAgainstPatterns(TabInfo tab, List<string> patterns)
    {
        if (!tab.Active || patterns.Count == 0)
            return false;

        foreach (var pattern in patterns)
        {
            var regex = GetOrCreateRegex(pattern);

            if ((!string.IsNullOrEmpty(tab.Title) && regex.IsMatch(tab.Title))
                || (!string.IsNullOrEmpty(tab.Url) && regex.IsMatch(tab.Url)))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Gets a cached compiled Regex for a glob pattern, or creates one.
    /// Glob patterns support * (any chars) and ? (single char).
    /// Matching is case-insensitive.
    /// </summary>
    private Regex GetOrCreateRegex(string pattern)
    {
        if (_patternCache.TryGetValue(pattern, out var cached))
            return cached;

        // Convert glob pattern to regex:
        // Escape everything except * and ?, then replace * -> .* and ? -> .
        var escaped = Regex.Escape(pattern)
            .Replace(@"\*", ".*")
            .Replace(@"\?", ".");

        var regex = new Regex($"^{escaped}$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        _patternCache[pattern] = regex;
        return regex;
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            Stop();
            _cts?.Dispose();
            _httpListener?.Close();
            _disposed = true;
        }
    }
}

/// <summary>
/// Represents a tab info message received from the Chrome extension.
/// </summary>
internal class TabInfo
{
    /// <summary>
    /// The URL of the active tab.
    /// </summary>
    public string Url { get; set; } = string.Empty;

    /// <summary>
    /// The title of the active tab.
    /// </summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>
    /// Whether the browser window is active (focused). False when the user switches
    /// away from Chrome entirely.
    /// </summary>
    public bool Active { get; set; }
}
