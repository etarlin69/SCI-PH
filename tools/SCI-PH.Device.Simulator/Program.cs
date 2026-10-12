using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;

const string deviceIdHeader = "X-Device-Id";
const string deviceKeyHeader = "X-Device-Key";
const string heartbeatPath = "api/device/heartbeat";

var baseUrl = GetEnvironmentVariable(
    "DEVICE_API_BASE_URL",
    "http://localhost:5139");

var deviceId = GetRequiredEnvironmentVariable("DEVICE_ID");
var deviceKey = GetRequiredEnvironmentVariable("DEVICE_KEY");

var firmwareVersion = GetEnvironmentVariable(
    "DEVICE_FIRMWARE_VERSION",
    "sim-0.1.0");

var heartbeatIntervalSeconds = GetPositiveIntegerEnvironmentVariable(
    "DEVICE_HEARTBEAT_INTERVAL_SECONDS",
    30);

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

var uptime = Stopwatch.StartNew();

Console.WriteLine("SCI-PH Device Simulator");
Console.WriteLine($"Device: {deviceId}");
Console.WriteLine($"Device API: {httpClient.BaseAddress}");
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