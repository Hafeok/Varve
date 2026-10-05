// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

// The browser storage backend's tests, in headless Chromium, driven with the
// BCL alone.
//
//   dotnet build tests/Varve.Store.BrowserTests -c Release
//   dotnet run eng/browser-tests.cs -- [--bundle <AppBundle>] [--chrome <path>] [--timeout <seconds>]
//
// ADR 0084. It serves the test app's AppBundle over HTTP on localhost (a
// secure context, which the origin private file system requires), starts
// Chromium headless with a DevTools port, connects to the page over the
// DevTools protocol with ClientWebSocket, and waits for the page to leave its
// report on `globalThis.varveReport`. It loads the page twice: once with .NET
// in a dedicated worker, where the OPFS backend is chosen, and once with .NET
// on the page's own thread, where synchronous access handles do not exist and
// the IndexedDB backend is chosen.
//
// It then runs the deterministic script (tests/Varve.Store.BrowserTests/
// DeterministicScript.cs, included below) on FileStorage here on the desktop,
// and requires the browser's log/ to be byte-identical: the worker's report
// carries a SHA-256 of every file under log/, and this compares them with the
// desktop run's, file by file.
//
// Chromium is found from --chrome, then VARVE_CHROME, then CHROME_PATH, then
// google-chrome, chromium and chromium-browser on PATH. No package and no
// browser download: ADR 0084 says why.
//
// Exit codes: 0 every report passed and the logs are identical, 1 a test or
// the comparison failed, 2 could not run.

// The desktop half needs the store and the script the browser app runs, so
// this is the one script in eng/ that references a Varve project. CA2266 asks
// a file with directives to begin with a shebang, and the licence-header gate
// requires the notice on line 1 (ADR 0031); the notice wins, and CA2266 is
// off for this file only (ADR 0084).
#:project ../src/Varve.Store/Varve.Store.csproj
#:include ../tests/Varve.Store.BrowserTests/DeterministicScript.cs
#:property NoWarn=$(NoWarn);CA2266

using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Varve.Store;
using Varve.Store.BrowserTests;

string repositoryRoot = FindRepositoryRoot();
string bundle = Path.Combine(repositoryRoot, "tests", "Varve.Store.BrowserTests", "bin", "Release", "net10.0", "browser-wasm", "AppBundle");
string? chrome = null;
int timeoutSeconds = 600;

for (int i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--bundle" when i + 1 < args.Length:
            bundle = Path.GetFullPath(args[++i]);
            break;
        case "--chrome" when i + 1 < args.Length:
            chrome = args[++i];
            break;
        case "--timeout" when i + 1 < args.Length:
            timeoutSeconds = int.Parse(args[++i], CultureInfo.InvariantCulture);
            break;
        default:
            Console.Error.WriteLine("browser-tests: unknown argument '" + args[i] + "'.");
            return 2;
    }
}

chrome ??= FindChrome();

if (chrome is null)
{
    Console.Error.WriteLine("browser-tests: no Chromium found. Pass --chrome <path>, or set VARVE_CHROME.");
    return 2;
}

if (!File.Exists(Path.Combine(bundle, "index.html")))
{
    Console.Error.WriteLine("browser-tests: no app bundle at '" + bundle + "'. Build tests/Varve.Store.BrowserTests first.");
    return 2;
}

// The desktop half of the determinism comparison: the same script, on the
// file backend, in a fresh directory.
SortedDictionary<string, string> desktop;
string desktopDirectory = Directory.CreateTempSubdirectory("varve-determinism-").FullName;

try
{
    await DeterministicScript.RunAsync(await FileStorage.OpenAsync(new Varve.Store.Log.DatasetDirectory(desktopDirectory), new FileStorageOptions { Clock = DeterministicScript.Clock() }), CancellationToken.None);
    desktop = HashLog(Path.Combine(desktopDirectory, "log"));
}
finally
{
    foreach (string file in Directory.GetFiles(desktopDirectory, "*", SearchOption.AllDirectories))
    {
        File.SetAttributes(file, FileAttributes.Normal);
    }

    Directory.Delete(desktopDirectory, recursive: true);
}

using TcpListener listener = new(IPAddress.Loopback, 0);
listener.Start();
int port = ((IPEndPoint)listener.LocalEndpoint).Port;
using CancellationTokenSource serving = new();
Task server = ServeAsync(listener, bundle, serving.Token);

string profile = Directory.CreateTempSubdirectory("varve-chromium-").FullName;
Process? browser = null;
int exit;

try
{
    ProcessStartInfo start = new(chrome)
    {
        RedirectStandardError = true,
        RedirectStandardOutput = true,
        UseShellExecute = false,
    };

    foreach (string argument in (string[])
    [
        "--headless=new",
        "--remote-debugging-port=0",
        "--user-data-dir=" + profile,
        "--no-first-run",
        "--no-default-browser-check",
        "--disable-gpu",
        "--disable-extensions",
        "about:blank",
    ])
    {
        start.ArgumentList.Add(argument);
    }

    // Chromium refuses to start its sandbox as root, which is what a container is.
    if (OperatingSystem.IsLinux() && Environment.UserName == "root")
    {
        start.ArgumentList.Insert(0, "--no-sandbox");
    }

    browser = Process.Start(start) ?? throw new InvalidOperationException("Chromium did not start.");
    browser.OutputDataReceived += static (_, _) => { };
    browser.ErrorDataReceived += static (_, _) => { };
    browser.BeginOutputReadLine();
    browser.BeginErrorReadLine();

    string webSocket = await PageWebSocketAsync(profile, TimeSpan.FromSeconds(30));
    using ClientWebSocket devtools = new();
    devtools.Options.KeepAliveInterval = TimeSpan.Zero;
    await devtools.ConnectAsync(new Uri(webSocket), CancellationToken.None);
    DevTools session = new(devtools);
    await session.SendAsync("Runtime.enable", null);
    await session.SendAsync("Page.enable", null);
    Console.WriteLine("Chromium: " + await session.EvaluateStringAsync("navigator.userAgent"));

    exit = 0;

    foreach ((string page, string label) in ((string, string)[])
    [
        ("index.html", ".NET in a dedicated worker: the OPFS backend"),
        ("index.html?thread=main", ".NET on the page's thread: the IndexedDB fallback"),
    ])
    {
        Console.WriteLine();
        Console.WriteLine("--- " + label + " (" + page + ")");
        await session.SendAsync("Page.navigate", new Dictionary<string, object> { ["url"] = "http://localhost:" + port.ToString(CultureInfo.InvariantCulture) + "/" + page });
        string? report = await session.WaitForReportAsync(TimeSpan.FromSeconds(timeoutSeconds));

        if (report is null)
        {
            Console.WriteLine("FAIL: no report within " + timeoutSeconds.ToString(CultureInfo.InvariantCulture) + " s.");
            session.PrintConsole();
            exit = 1;
            continue;
        }

        Console.WriteLine(report);

        if (!report.TrimEnd().EndsWith("OK", StringComparison.Ordinal))
        {
            session.PrintConsole();
            exit = 1;
        }

        SortedDictionary<string, string> browserLog = new(StringComparer.Ordinal);

        foreach (string line in report.Split('\n'))
        {
            string[] parts = line.Trim().Split(' ');

            if (parts.Length == 3 && parts[0] == "determinism")
            {
                browserLog[parts[1]] = parts[2];
            }
        }

        if (browserLog.Count > 0)
        {
            exit = Math.Max(exit, CompareLogs(desktop, browserLog));
        }
        else if (page == "index.html")
        {
            Console.WriteLine("FAIL: the worker's report carries no determinism lines.");
            exit = 1;
        }
    }
}
catch (Exception e) when (e is IOException or WebSocketException or InvalidOperationException or TimeoutException or System.ComponentModel.Win32Exception)
{
    Console.Error.WriteLine("browser-tests: could not run: " + e.Message);
    exit = 2;
}
finally
{
    if (browser is { HasExited: false })
    {
        browser.Kill(entireProcessTree: true);
        await browser.WaitForExitAsync();
    }

    serving.Cancel();
    listener.Stop();

    try
    {
        Directory.Delete(profile, recursive: true);
    }
    catch (IOException)
    {
    }
}

Console.WriteLine();
Console.WriteLine(exit == 0 ? "browser-tests: passed" : "browser-tests: FAILED");
return exit;

static int CompareLogs(SortedDictionary<string, string> desktop, SortedDictionary<string, string> browser)
{
    Console.WriteLine();
    Console.WriteLine("Determinism: log/ on the desktop (FileStorage) and in the browser (OPFS)");
    int failures = 0;

    foreach (string name in desktop.Keys.Union(browser.Keys).Order(StringComparer.Ordinal))
    {
        string left = desktop.GetValueOrDefault(name, "(absent)");
        string right = browser.GetValueOrDefault(name, "(absent)");
        bool same = left == right;
        failures += same ? 0 : 1;
        Console.WriteLine((same ? "  same     " : "  DIFFERS  ") + name + " " + left + (same ? string.Empty : " / " + right));
    }

    Console.WriteLine(failures == 0 ? "  byte-identical: " + desktop.Count.ToString(CultureInfo.InvariantCulture) + " files" : "  FAIL: " + failures.ToString(CultureInfo.InvariantCulture) + " files differ");
    return failures == 0 ? 0 : 1;
}

static SortedDictionary<string, string> HashLog(string log)
{
    SortedDictionary<string, string> hashes = new(StringComparer.Ordinal);

    foreach (string file in Directory.GetFiles(log))
    {
        hashes["log/" + Path.GetFileName(file)] = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(file)));
    }

    return hashes;
}

static async Task<string> PageWebSocketAsync(string profile, TimeSpan timeout)
{
    string activePort = Path.Combine(profile, "DevToolsActivePort");
    Stopwatch waited = Stopwatch.StartNew();

    while (waited.Elapsed < timeout)
    {
        if (File.Exists(activePort))
        {
            string[] lines = await File.ReadAllLinesAsync(activePort);

            if (lines.Length >= 1 && int.TryParse(lines[0], NumberStyles.None, CultureInfo.InvariantCulture, out int devtoolsPort))
            {
                using HttpClient http = new();
                string list = await http.GetStringAsync("http://127.0.0.1:" + devtoolsPort.ToString(CultureInfo.InvariantCulture) + "/json/list");

                using JsonDocument targets = JsonDocument.Parse(list);

                foreach (JsonElement target in targets.RootElement.EnumerateArray())
                {
                    if (target.GetProperty("type").GetString() == "page")
                    {
                        return target.GetProperty("webSocketDebuggerUrl").GetString()!;
                    }
                }
            }
        }

        await Task.Delay(100);
    }

    throw new TimeoutException("Chromium opened no DevTools port within " + timeout.TotalSeconds.ToString(CultureInfo.InvariantCulture) + " s.");
}

// A static file server for the AppBundle: GET only, no directory listing, no
// path outside the bundle. Enough for dotnet.js, which fetches _framework/.
static async Task ServeAsync(TcpListener listener, string root, CancellationToken cancellationToken)
{
    string fullRoot = Path.GetFullPath(root) + Path.DirectorySeparatorChar;

    while (!cancellationToken.IsCancellationRequested)
    {
        TcpClient client;

        try
        {
            client = await listener.AcceptTcpClientAsync(cancellationToken);
        }
        catch (Exception e) when (e is OperationCanceledException or ObjectDisposedException or SocketException)
        {
            return;
        }

        _ = Task.Run(async () =>
        {
            using (client)
            {
                try
                {
                    NetworkStream stream = client.GetStream();

                    while (true)
                    {
                        string? requestLine = await ReadLineAsync(stream);

                        if (string.IsNullOrEmpty(requestLine))
                        {
                            return;
                        }

                        string? header;
                        bool close = false;

                        while (!string.IsNullOrEmpty(header = await ReadLineAsync(stream)))
                        {
                            close |= header.StartsWith("Connection:", StringComparison.OrdinalIgnoreCase) && header.Contains("close", StringComparison.OrdinalIgnoreCase);
                        }

                        string[] parts = requestLine.Split(' ');
                        string target = parts.Length > 1 ? parts[1] : "/";
                        int query = target.IndexOfAny(['?', '#']);
                        string relative = Uri.UnescapeDataString(query < 0 ? target : target[..query]).TrimStart('/');
                        string path = Path.GetFullPath(Path.Combine(fullRoot, relative.Length == 0 ? "index.html" : relative));

                        byte[] body;
                        string status;
                        string type;

                        if (parts[0] == "GET" && path.StartsWith(fullRoot, StringComparison.Ordinal) && File.Exists(path))
                        {
                            body = await File.ReadAllBytesAsync(path);
                            status = "200 OK";
                            type = ContentType(path);
                        }
                        else
                        {
                            body = "not found"u8.ToArray();
                            status = "404 Not Found";
                            type = "text/plain";
                        }

                        string head = "HTTP/1.1 " + status + "\r\nContent-Type: " + type + "\r\nContent-Length: " + body.Length.ToString(CultureInfo.InvariantCulture)
                            + "\r\nCache-Control: no-store\r\nConnection: " + (close ? "close" : "keep-alive") + "\r\n\r\n";
                        await stream.WriteAsync(Encoding.ASCII.GetBytes(head));
                        await stream.WriteAsync(body);

                        if (close)
                        {
                            return;
                        }
                    }
                }
                catch (IOException)
                {
                }
            }
        }, CancellationToken.None);
    }
}

static async Task<string?> ReadLineAsync(NetworkStream stream)
{
    StringBuilder line = new();
    byte[] one = new byte[1];

    while (true)
    {
        int read = await stream.ReadAsync(one);

        if (read == 0)
        {
            return line.Length == 0 ? null : line.ToString();
        }

        if (one[0] == (byte)'\n')
        {
            return line.ToString().TrimEnd('\r');
        }

        line.Append((char)one[0]);
    }
}

static string ContentType(string path) => Path.GetExtension(path) switch
{
    ".html" => "text/html; charset=utf-8",
    ".js" or ".mjs" => "text/javascript",
    ".json" => "application/json",
    ".wasm" => "application/wasm",
    _ => "application/octet-stream",
};

static string? FindChrome()
{
    foreach (string variable in (string[])["VARVE_CHROME", "CHROME_PATH"])
    {
        if (Environment.GetEnvironmentVariable(variable) is { Length: > 0 } configured)
        {
            return configured;
        }
    }

    foreach (string name in (string[])["google-chrome", "google-chrome-stable", "chromium", "chromium-browser"])
    {
        foreach (string directory in (Environment.GetEnvironmentVariable("PATH") ?? string.Empty).Split(Path.PathSeparator))
        {
            string candidate = Path.Combine(directory, name);

            if (File.Exists(candidate))
            {
                return candidate;
            }
        }
    }

    return null;
}

static string FindRepositoryRoot()
{
    string? directory = AppContext.GetData("EntryPointFileDirectoryPath") as string ?? Directory.GetCurrentDirectory();

    for (DirectoryInfo? current = new(directory); current is not null; current = current.Parent)
    {
        if (File.Exists(Path.Combine(current.FullName, "Varve.slnx")))
        {
            return current.FullName;
        }
    }

    return Directory.GetCurrentDirectory();
}

/// <summary>One DevTools protocol session over a WebSocket: commands with ids, events kept for the console.</summary>
internal sealed class DevTools(ClientWebSocket socket)
{
    private readonly List<string> _console = [];
    private int _id;

    public async Task<JsonElement> SendAsync(string method, Dictionary<string, object>? parameters)
    {
        int id = ++_id;
        Dictionary<string, object> message = new() { ["id"] = id, ["method"] = method };

        if (parameters is not null)
        {
            message["params"] = parameters;
        }

        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(message, DevToolsJson.Default.DictionaryStringObject);
        await socket.SendAsync(bytes, WebSocketMessageType.Text, endOfMessage: true, CancellationToken.None);

        while (true)
        {
            using JsonDocument reply = await ReceiveAsync();
            JsonElement root = reply.RootElement;

            if (root.TryGetProperty("id", out JsonElement replyId) && replyId.GetInt32() == id)
            {
                if (root.TryGetProperty("error", out JsonElement error))
                {
                    throw new InvalidOperationException(method + ": " + error.GetRawText());
                }

                return root.GetProperty("result").Clone();
            }

            Remember(root);
        }
    }

    public async Task<string> EvaluateStringAsync(string expression)
    {
        JsonElement result = await SendAsync("Runtime.evaluate", new Dictionary<string, object> { ["expression"] = expression, ["returnByValue"] = true });
        JsonElement value = result.GetProperty("result");
        return value.TryGetProperty("value", out JsonElement text) && text.ValueKind == JsonValueKind.String ? text.GetString()! : string.Empty;
    }

    public async Task<string?> WaitForReportAsync(TimeSpan timeout)
    {
        Stopwatch waited = Stopwatch.StartNew();
        _console.Clear();

        while (waited.Elapsed < timeout)
        {
            await Task.Delay(250);
            string report = await EvaluateStringAsync("typeof globalThis.varveReport === 'string' ? globalThis.varveReport : ''");

            if (report.Length > 0)
            {
                return report;
            }
        }

        return null;
    }

    public void PrintConsole()
    {
        foreach (string line in _console)
        {
            Console.WriteLine("  console: " + line);
        }
    }

    private void Remember(JsonElement root)
    {
        if (!root.TryGetProperty("method", out JsonElement method))
        {
            return;
        }

        if (method.GetString() == "Runtime.consoleAPICalled")
        {
            StringBuilder line = new();

            foreach (JsonElement argument in root.GetProperty("params").GetProperty("args").EnumerateArray())
            {
                line.Append(argument.TryGetProperty("value", out JsonElement value) ? value.ToString() : argument.GetProperty("type").GetString()).Append(' ');
            }

            _console.Add(line.ToString());
        }
        else if (method.GetString() == "Runtime.exceptionThrown")
        {
            _console.Add("exception: " + root.GetProperty("params").GetProperty("exceptionDetails").GetRawText());
        }
    }

    private async Task<JsonDocument> ReceiveAsync()
    {
        using MemoryStream message = new();
        byte[] buffer = new byte[64 * 1024];

        while (true)
        {
            WebSocketReceiveResult result = await socket.ReceiveAsync(buffer, CancellationToken.None);

            if (result.MessageType == WebSocketMessageType.Close)
            {
                throw new InvalidOperationException("Chromium closed the DevTools connection.");
            }

            message.Write(buffer, 0, result.Count);

            if (result.EndOfMessage)
            {
                return JsonDocument.Parse(message.ToArray());
            }
        }
    }
}

[System.Text.Json.Serialization.JsonSerializable(typeof(Dictionary<string, object>))]
[System.Text.Json.Serialization.JsonSerializable(typeof(Dictionary<string, string>))]
[System.Text.Json.Serialization.JsonSerializable(typeof(int))]
[System.Text.Json.Serialization.JsonSerializable(typeof(bool))]
[System.Text.Json.Serialization.JsonSerializable(typeof(string))]
internal sealed partial class DevToolsJson : System.Text.Json.Serialization.JsonSerializerContext;
