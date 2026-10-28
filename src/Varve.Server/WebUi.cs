// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.FileProviders;
using Microsoft.Net.Http.Headers;
using Varve.Protocol.Model;

namespace Varve.Server;

/// <summary>
/// The Web UI, served under <c>/ui/</c> from the directory
/// <c>Varve:WebUi:Path</c> names (ADR 0202, variant A). Same origin as the
/// API, so it needs no CORS.
/// </summary>
/// <remarks>
/// <list type="bullet">
///   <item>A <c>.br</c> or <c>.gz</c> sibling is served when the request
///   accepts it and the sibling is not older than the file, so an edited
///   <c>appsettings.json</c> is never shadowed by its stale compressed
///   copy.</item>
///   <item>Fingerprinted <c>_framework</c> files are cached for a year,
///   immutable; everything else is <c>no-cache</c>, revalidated by its
///   tag.</item>
///   <item>A path whose last segment has no extension is a route of the
///   application, and gets <c>index.html</c>; a missing file with one is a
///   <c>404</c>.</item>
///   <item>Every response carries the Content Security Policy and the three
///   isolation headers, problems included.</item>
/// </list>
/// </remarks>
internal static partial class ServerHost
{
    internal const string WebUiPrefix = "/ui";

    private static readonly FileExtensionContentTypeProvider WebUiTypes = new();

    internal static void MapWebUi(WebApplication app, string directory, string? authority)
    {
        PhysicalFileProvider files = new(directory);
        string policy = WebUiContentSecurityPolicy(authority);
        app.Map(WebUiPrefix, ui => ui.Run(context => ServeWebUiAsync(context, files, policy)));
    }

    /// <summary>
    /// The policy (ADR 0202): what Blazor's boot needs, the issuer's origin for
    /// the token requests when there is one, and nothing else.
    /// </summary>
    internal static string WebUiContentSecurityPolicy(string? authority)
    {
        string issuer = Uri.TryCreate(authority, UriKind.Absolute, out Uri? address) ? " " + address.GetLeftPart(UriPartial.Authority) : string.Empty;
        return "default-src 'none'; script-src 'self' 'wasm-unsafe-eval'; style-src 'self'; img-src 'self'; font-src 'self'; "
            + "connect-src 'self'" + issuer + "; manifest-src 'self'; base-uri 'self'; form-action 'none'; frame-ancestors 'none'; object-src 'none'";
    }

    // A file the build named by its content: name.<10 lower-case letters or digits>.ext.
    [GeneratedRegex(@"^_framework/[^/]+\.[a-z0-9]{10}\.(wasm|js|dat|pdb)$", RegexOptions.CultureInvariant)]
    private static partial Regex Fingerprinted();

    private static async Task ServeWebUiAsync(HttpContext context, PhysicalFileProvider files, string policy)
    {
        HttpResponse response = context.Response;
        response.Headers.ContentSecurityPolicy = policy;
        response.Headers.XContentTypeOptions = "nosniff";
        response.Headers["Referrer-Policy"] = "no-referrer";
        response.Headers["Cross-Origin-Opener-Policy"] = "same-origin";

        string method = context.Request.Method;

        if (!HttpMethods.IsGet(method) && !HttpMethods.IsHead(method))
        {
            response.Headers.Allow = "GET, HEAD";
            await WriteProblemAsync(context, ProblemType.MethodNotAllowed, null).ConfigureAwait(false);
            return;
        }

        // The base is /ui/: without the slash, every relative URL resolves wrong.
        if (!context.Request.Path.HasValue)
        {
            response.StatusCode = StatusCodes.Status308PermanentRedirect;
            response.Headers.Location = context.Request.PathBase + "/" + context.Request.QueryString;
            return;
        }

        string relative = context.Request.Path.Value!.TrimStart('/');

        if (relative.Length == 0)
        {
            relative = "index.html";
        }

        IFileInfo file = files.GetFileInfo(relative);

        if (!file.Exists || file.IsDirectory)
        {
            string last = relative[(relative.LastIndexOf('/') + 1)..];

            if (last.Contains('.', StringComparison.Ordinal))
            {
                await WriteProblemAsync(context, ProblemType.NotFound, null).ConfigureAwait(false);
                return;
            }

            relative = "index.html";
            file = files.GetFileInfo(relative);
        }

        response.ContentType = WebUiTypes.TryGetContentType(relative, out string? type) ? type : "application/octet-stream";
        response.Headers.CacheControl = Fingerprinted().IsMatch(relative) ? "public, max-age=31536000, immutable" : "no-cache";
        response.Headers.Vary = HeaderNames.AcceptEncoding;

        IFileInfo sent = file;

        if (Compressed(files, context.Request, relative, file, "br") is { } br)
        {
            sent = br;
            response.Headers.ContentEncoding = "br";
        }
        else if (Compressed(files, context.Request, relative, file, "gzip") is { } gzip)
        {
            sent = gzip;
            response.Headers.ContentEncoding = "gzip";
        }

        // The tag names the representation: the file's, and its encoding's.
        string tag = "\"" + sent.Length.ToString("x", System.Globalization.CultureInfo.InvariantCulture) + "-"
            + sent.LastModified.UtcTicks.ToString("x", System.Globalization.CultureInfo.InvariantCulture) + "\"";
        response.Headers.ETag = tag;
        response.Headers.LastModified = file.LastModified.ToString("R", System.Globalization.CultureInfo.InvariantCulture);

        if (context.Request.Headers.IfNoneMatch == tag)
        {
            response.StatusCode = StatusCodes.Status304NotModified;
            return;
        }

        response.ContentLength = sent.Length;

        if (HttpMethods.IsHead(method))
        {
            return;
        }

        await response.SendFileAsync(sent, context.RequestAborted).ConfigureAwait(false);
    }

    private static IFileInfo? Compressed(PhysicalFileProvider files, HttpRequest request, string relative, IFileInfo file, string coding)
    {
        if (!Accepts(request, coding))
        {
            return null;
        }

        IFileInfo sibling = files.GetFileInfo(relative + (coding == "br" ? ".br" : ".gz"));
        return sibling.Exists && !sibling.IsDirectory && sibling.LastModified >= file.LastModified ? sibling : null;
    }

    private static bool Accepts(HttpRequest request, string coding)
    {
        foreach (StringWithQualityHeaderValue value in request.GetTypedHeaders().AcceptEncoding)
        {
            if (value.Value.Equals(coding, StringComparison.OrdinalIgnoreCase) && value.Quality is not 0)
            {
                return true;
            }
        }

        return false;
    }
}
