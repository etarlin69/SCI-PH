using System.Reflection;

namespace SCI_PH.Mobile.Common.Api;

/// <summary>
/// Dirección del Mobile BFF. Se resuelve una vez al arrancar:
/// <list type="number">
/// <item>Si se compiló con <c>-p:MobileBffBaseUrl=...</c>, esa URL (backend desplegado, dispositivo físico). En
/// Release solo se acepta HTTPS.</item>
/// <item>En Debug, el Mobile BFF local: <c>http://10.0.2.2:5191</c> en el emulador Android (10.0.2.2 es el PC) y
/// <c>http://localhost:5191</c> en el resto.</item>
/// <item>En Release sin URL configurada, ninguna: la app informa que no tiene servidor configurado.</item>
/// </list>
/// Nunca se versiona una IP personal; ver mobile/README.md.
/// </summary>
public sealed class MobileApiOptions
{
    /// <summary>Clave del <see cref="AssemblyMetadataAttribute"/> que genera la propiedad MSBuild <c>MobileBffBaseUrl</c>.</summary>
    public const string BaseUrlMetadataKey = "MobileBffBaseUrl";

    public const string AndroidEmulatorDevelopmentUrl = "http://10.0.2.2:5191";
    public const string LocalDevelopmentUrl = "http://localhost:5191";

    public Uri? BaseAddress { get; init; }

    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(20);

    public static MobileApiOptions ForCurrentBuild()
    {
        var configured = typeof(MobileApiOptions).Assembly
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(attribute => attribute.Key == BaseUrlMetadataKey)?.Value;
#if DEBUG
        const bool isDebug = true;
#else
        const bool isDebug = false;
#endif
        return new MobileApiOptions { BaseAddress = ResolveBaseAddress(configured, isDebug, OperatingSystem.IsAndroid()) };
    }

    public static Uri? ResolveBaseAddress(string? configuredUrl, bool isDebug, bool isAndroid)
    {
        if (!string.IsNullOrWhiteSpace(configuredUrl))
        {
            // HTTP solo en Debug (dispositivo físico contra el BFF local); Release exige HTTPS.
            return Uri.TryCreate(configuredUrl.Trim(), UriKind.Absolute, out var uri)
                && (uri.Scheme == Uri.UriSchemeHttps || (isDebug && uri.Scheme == Uri.UriSchemeHttp))
                ? uri
                : null;
        }

        if (!isDebug)
        {
            return null;
        }

        return new Uri(isAndroid ? AndroidEmulatorDevelopmentUrl : LocalDevelopmentUrl);
    }
}
