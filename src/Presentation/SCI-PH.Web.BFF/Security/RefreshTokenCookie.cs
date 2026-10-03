namespace SCI_PH.Web.BFF.Security
{
    // The web refresh token lives only in this cookie: HttpOnly (no JavaScript access), Secure, SameSite=Strict,
    // host-only (__Host- prefix: Path=/ and no Domain). It is never returned in a JSON body.
    public static class RefreshTokenCookie
    {
        public const string Name = "__Host-sciph-refresh";

        public static void Write(HttpResponse response, string refreshToken, DateTimeOffset expiresAtUtc) =>
            response.Cookies.Append(Name, refreshToken, CreateOptions(expiresAtUtc));

        public static string? Read(HttpRequest request) =>
            request.Cookies.TryGetValue(Name, out var value) && !string.IsNullOrEmpty(value) ? value : null;

        public static void Delete(HttpResponse response) => response.Cookies.Delete(Name, CreateOptions(null));

        private static CookieOptions CreateOptions(DateTimeOffset? expiresAtUtc) =>
            new()
            {
                HttpOnly = true,
                Secure = true,
                SameSite = SameSiteMode.Strict,
                Path = "/",
                IsEssential = true,
                Expires = expiresAtUtc
            };
    }
}
