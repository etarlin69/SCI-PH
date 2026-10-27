using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

const string deviceIdHeader = "X-Device-Id";
const string deviceKeyHeader = "X-Device-Key";
const string heartbeatPath = "api/device/heartbeat";
const string pendingSessionPath = "api/device/fingerprint/sessions/pending";

string[] fingerprintOutcomes = ["Match", "NoMatch", "LowQuality", "SensorError"];

var baseUrl = GetEnvironmentVariable(
    "DEVICE_API_BASE_URL",
    "http://localhost:5139");

var deviceId = GetRequiredEnvironmentVariable("DEVICE_ID");
var deviceKey = GetRequiredEnvironmentVariable("DEVICE_KEY");

// heartbeat (default): periodic heartbeats. fingerprint: an R307 reader that polls for its capture job and reports
// what the sensor matched (the sensor itself is simulated: no template ever leaves the device).
var mode = GetEnvironmentVariable("DEVICE_MODE", "heartbeat").ToLowerInvariant();

var firmwareVersion = GetEnvironmentVariable(
    "DEVICE_FIRMWARE_VERSION",
    "sim-0.1.0");

var heartbeatIntervalSeconds = GetPositiveIntegerEnvironmentVariable(
    "DEVICE_HEARTBEAT_INTERVAL_SECONDS",
    30);

if (mode is not ("heartbeat" or "fingerprint"))
{
    Console.Error.WriteLine("DEVICE_MODE must be heartbeat or fingerprint.");

    return 1;
}

if (firmwareVersion.Length > 32)
{
    Console.Error.WriteLine(
        "DEVICE_FIRMWARE_VERSION cannot exceed 32 characters.");

    return 1;
}

if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var apiBaseUri) ||
    apiBaseUri.Scheme is not ("http" or "https"))
{
    Console.Error.WriteLine(
        "DEVICE_API_BASE_URL must be a valid HTTP or HTTPS URL.");

    return 1;
}

using var cancellation = new CancellationTokenSource();

Console.CancelKeyPress += (_, eventArgs) =>
{
    eventArgs.Cancel = true;
    cancellation.Cancel();
};

using var httpClient = new HttpClient
{
    BaseAddress = new Uri(
        apiBaseUri.ToString().TrimEnd('/') + "/"),
    Timeout = TimeSpan.FromSeconds(10)
};

httpClient.DefaultRequestHeaders.Add(deviceIdHeader, deviceId);
httpClient.DefaultRequestHeaders.Add(deviceKeyHeader, deviceKey);

Console.WriteLine("SCI-PH Device Simulator");
Console.WriteLine($"Device: {deviceId}");
Console.WriteLine($"Device API: {httpClient.BaseAddress}");
Console.WriteLine($"Mode: {mode}");

if (mode == "fingerprint")
{
    return await RunFingerprintReaderAsync(httpClient, fingerprintOutcomes, cancellation.Token);
}

var uptime = Stopwatch.StartNew();

Console.WriteLine($"Firmware: {firmwareVersion}");
Console.WriteLine(
    $"Heartbeat interval: {heartbeatIntervalSeconds} seconds");
Console.WriteLine("Press Ctrl+C to stop.");
Console.WriteLine();

try
{
    while (!cancellation.IsCancellationRequested)
    {
        var shouldContinue = await SendHeartbeatAsync(
            httpClient,
            firmwareVersion,
            uptime,
            cancellation.Token);

        if (!shouldContinue)
        {
            return 2;
        }

        await Task.Delay(
            TimeSpan.FromSeconds(heartbeatIntervalSeconds),
            cancellation.Token);
    }
}
catch (OperationCanceledException)
    when (cancellation.IsCancellationRequested)
{
    // Normal shutdown requested by Ctrl+C.
}

Console.WriteLine();
Console.WriteLine("Simulator stopped.");

return 0;


static async Task<bool> SendHeartbeatAsync(
    HttpClient httpClient,
    string firmwareVersion,
    Stopwatch uptime,
    CancellationToken cancellationToken)
{
    var request = new
    {
        firmwareVersion,
        uptimeSeconds = (long)uptime.Elapsed.TotalSeconds
    };

    try
    {
        using var response = await httpClient.PostAsJsonAsync(
            heartbeatPath,
            request,
            cancellationToken);

        var timestamp = DateTimeOffset.Now.ToString("HH:mm:ss");

        if (response.StatusCode == HttpStatusCode.NoContent)
        {
            Console.WriteLine(
                $"[{timestamp}] Heartbeat accepted " +
                $"(uptime {request.uptimeSeconds}s).");

            return true;
        }

        if (response.StatusCode is
            HttpStatusCode.Unauthorized or
            HttpStatusCode.Forbidden)
        {
            Console.Error.WriteLine(
                $"[{timestamp}] Device authentication failed " +
                $"({(int)response.StatusCode} {response.StatusCode}).");

            Console.Error.WriteLine(
                "Check DEVICE_ID, DEVICE_KEY and the device scopes.");

            return false;
        }

        if ((int)response.StatusCode == 429)
        {
            Console.Error.WriteLine(
                $"[{timestamp}] Device API rate limit reached. " +
                "The simulator will retry on the next interval.");

            return true;
        }

        if ((int)response.StatusCode >= 500)
        {
            Console.Error.WriteLine(
                $"[{timestamp}] Device API returned " +
                $"{(int)response.StatusCode} {response.StatusCode}. " +
                "The simulator will retry.");

            return true;
        }

        var responseBody =
            await response.Content.ReadAsStringAsync(cancellationToken);

        Console.Error.WriteLine(
            $"[{timestamp}] Unexpected response: " +
            $"{(int)response.StatusCode} {response.StatusCode}.");

        if (!string.IsNullOrWhiteSpace(responseBody))
        {
            Console.Error.WriteLine(responseBody);
        }

        return true;
    }
    catch (HttpRequestException exception)
    {
        Console.Error.WriteLine(
            $"[{DateTimeOffset.Now:HH:mm:ss}] " +
            $"Unable to reach Device API: {exception.Message}");

        return true;
    }
    catch (TaskCanceledException)
        when (!cancellationToken.IsCancellationRequested)
    {
        Console.Error.WriteLine(
            $"[{DateTimeOffset.Now:HH:mm:ss}] " +
            "Device API request timed out.");

        return true;
    }
}


// Fingerprint reader: polls for the Pending session of this device, "captures" after a delay and reports the configured
// outcome. Only the session id and its expiry are ever received; the slot is configuration and is never printed.
static async Task<int> RunFingerprintReaderAsync(
    HttpClient httpClient,
    string[] outcomes,
    CancellationToken cancellationToken)
{
    var outcome = GetEnvironmentVariable("FINGERPRINT_OUTCOME", "Match");
    if (!outcomes.Contains(outcome))
    {
        Console.Error.WriteLine($"FINGERPRINT_OUTCOME must be one of: {string.Join(", ", outcomes)}.");

        return 1;
    }

    int? slot = null;
    if (outcome == "Match")
    {
        var slotText = Environment.GetEnvironmentVariable("FINGERPRINT_SLOT");
        if (!int.TryParse(slotText, out var parsedSlot) || parsedSlot < 0)
        {
            Console.Error.WriteLine("FINGERPRINT_SLOT (a non-negative integer) is required for a Match.");

            return 1;
        }

        slot = parsedSlot;
    }

    var pollSeconds = GetPositiveIntegerEnvironmentVariable("FINGERPRINT_POLL_INTERVAL_SECONDS", 2);
    var captureDelaySeconds = GetNonNegativeIntegerEnvironmentVariable("FINGERPRINT_RESULT_DELAY_SECONDS", 1);
    var repeatResult = GetBooleanEnvironmentVariable("FINGERPRINT_REPEAT_RESULT");
    var once = GetBooleanEnvironmentVariable("FINGERPRINT_ONCE");

    Console.WriteLine($"Fingerprint outcome: {outcome}");
    Console.WriteLine($"Poll interval: {pollSeconds} seconds; capture delay: {captureDelaySeconds} seconds");
    Console.WriteLine("Press Ctrl+C to stop.");
    Console.WriteLine();

    try
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            var (status, sessionId) = await GetPendingSessionAsync(httpClient, cancellationToken);
            if (status is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            {
                Console.Error.WriteLine($"[{Now()}] Device authentication failed ({(int)status} {status}).");
                Console.Error.WriteLine("Check DEVICE_ID, DEVICE_KEY and the Device.Fingerprint.Identify scope.");

                return 2;
            }

            if (sessionId is { } session)
            {
                Console.WriteLine($"[{Now()}] Capture requested (session {session.ToString()[..8]}).");
                await Task.Delay(TimeSpan.FromSeconds(captureDelaySeconds), cancellationToken);

                var accepted = await SubmitResultAsync(httpClient, session, outcome, slot, cancellationToken);
                if (accepted && repeatResult)
                {
                    // A lost response: the same result again must be accepted without changing anything.
                    accepted = await SubmitResultAsync(httpClient, session, outcome, slot, cancellationToken);
                }

                if (once)
                {
                    return accepted ? 0 : 3;
                }
            }

            await Task.Delay(TimeSpan.FromSeconds(pollSeconds), cancellationToken);
        }
    }
    catch (OperationCanceledException)
        when (cancellationToken.IsCancellationRequested)
    {
        // Normal shutdown requested by Ctrl+C.
    }

    Console.WriteLine();
    Console.WriteLine("Simulator stopped.");

    return 0;
}


// 200 with the session id, 204 without one; any failure is reported and retried on the next poll.
static async Task<(HttpStatusCode? Status, Guid? SessionId)> GetPendingSessionAsync(
    HttpClient httpClient,
    CancellationToken cancellationToken)
{
    try
    {
        using var response = await httpClient.GetAsync(pendingSessionPath, cancellationToken);
        if (response.StatusCode == HttpStatusCode.OK)
        {
            using var pending = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
            return (response.StatusCode, pending.RootElement.GetProperty("sessionId").GetGuid());
        }

        if (response.StatusCode is not (HttpStatusCode.NoContent or HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden))
        {
            Console.Error.WriteLine($"[{Now()}] Pending session check returned {(int)response.StatusCode} {response.StatusCode}; retrying.");
        }

        return (response.StatusCode, null);
    }
    catch (HttpRequestException exception)
    {
        Console.Error.WriteLine($"[{Now()}] Unable to reach Device API: {exception.Message}");

        return (null, null);
    }
    catch (TaskCanceledException)
        when (!cancellationToken.IsCancellationRequested)
    {
        Console.Error.WriteLine($"[{Now()}] Device API request timed out.");

        return (null, null);
    }
}


static async Task<bool> SubmitResultAsync(
    HttpClient httpClient,
    Guid sessionId,
    string outcome,
    int? slot,
    CancellationToken cancellationToken)
{
    object body = slot is { } matchedSlot ? new { outcome, slot = matchedSlot } : new { outcome };
    try
    {
        using var response = await httpClient.PostAsJsonAsync(
            $"api/device/fingerprint/sessions/{sessionId}/result", body, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NoContent)
        {
            Console.WriteLine($"[{Now()}] Result {outcome} accepted.");

            return true;
        }

        Console.Error.WriteLine($"[{Now()}] Result {outcome} rejected ({(int)response.StatusCode} {response.StatusCode}).");

        return false;
    }
    catch (HttpRequestException exception)
    {
        Console.Error.WriteLine($"[{Now()}] Unable to reach Device API: {exception.Message}");

        return false;
    }
}


static string Now() => DateTimeOffset.Now.ToString("HH:mm:ss");


static string GetRequiredEnvironmentVariable(string name)
{
    var value = Environment.GetEnvironmentVariable(name);

    if (string.IsNullOrWhiteSpace(value))
    {
        Console.Error.WriteLine(
            $"Required environment variable {name} is not configured.");

        Environment.Exit(1);
    }

    return value!;
}


static string GetEnvironmentVariable(
    string name,
    string defaultValue)
{
    var value = Environment.GetEnvironmentVariable(name);

    return string.IsNullOrWhiteSpace(value)
        ? defaultValue
        : value.Trim();
}


static int GetPositiveIntegerEnvironmentVariable(
    string name,
    int defaultValue)
{
    var value = Environment.GetEnvironmentVariable(name);

    if (string.IsNullOrWhiteSpace(value))
    {
        return defaultValue;
    }

    if (int.TryParse(value, out var parsedValue) &&
        parsedValue > 0)
    {
        return parsedValue;
    }

    Console.Error.WriteLine(
        $"{name} must be a positive integer.");

    Environment.Exit(1);

    return defaultValue;
}


static int GetNonNegativeIntegerEnvironmentVariable(
    string name,
    int defaultValue)
{
    var value = Environment.GetEnvironmentVariable(name);

    if (string.IsNullOrWhiteSpace(value))
    {
        return defaultValue;
    }

    if (int.TryParse(value, out var parsedValue) &&
        parsedValue >= 0)
    {
        return parsedValue;
    }

    Console.Error.WriteLine(
        $"{name} must be a non-negative integer.");

    Environment.Exit(1);

    return defaultValue;
}


static bool GetBooleanEnvironmentVariable(string name) =>
    bool.TryParse(Environment.GetEnvironmentVariable(name), out var value) && value;
