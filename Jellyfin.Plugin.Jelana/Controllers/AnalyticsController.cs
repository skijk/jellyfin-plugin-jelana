using Jellyfin.Plugin.Jelana.Models;
using Jellyfin.Plugin.Jelana.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Jellyfin.Plugin.Jelana.Controllers;

[ApiController]
[Route("Jelana")]
public sealed class AnalyticsController : ControllerBase
{
    private readonly SnapshotStore _snapshots;
    public AnalyticsController(SnapshotStore snapshots) => _snapshots = snapshots;

    [HttpGet("Snapshot")]
    [Authorize]
    [ProducesResponseType(typeof(AnalyticsSnapshot), StatusCodes.Status200OK)]
    public async Task<ActionResult<AnalyticsSnapshot>> Get(CancellationToken cancellationToken)
    {
        var snapshot = await _snapshots.ReadAsync(cancellationToken).ConfigureAwait(false);
        return snapshot is null
            ? StatusCode(StatusCodes.Status503ServiceUnavailable, new { Error = "Snapshot is being prepared." })
            : Ok(snapshot);
    }

    [HttpGet("Personal")]
    [Authorize]
    [ProducesResponseType(typeof(PersonalAnalytics), StatusCodes.Status200OK)]
    public async Task<ActionResult<PersonalAnalytics>> Personal(CancellationToken cancellationToken)
    {
        var userId = User.FindFirst("Jellyfin-UserId")?.Value;
        if (string.IsNullOrWhiteSpace(userId)) return Forbid();
        var personal = await _snapshots.ReadPersonalAsync(userId, cancellationToken).ConfigureAwait(false);
        return personal is null
            ? StatusCode(StatusCodes.Status503ServiceUnavailable, new { Error = "Personal snapshot is being prepared." })
            : Ok(personal);
    }

    [HttpGet("User")]
    [AllowAnonymous]
    [Produces("text/html")]
    public IActionResult UserPage()
    {
        Response.Headers.CacheControl = "no-cache";
        var name = $"{typeof(Plugin).Namespace}.Web.jelana.html";
        using var stream = typeof(Plugin).Assembly.GetManifestResourceStream(name)
            ?? throw new InvalidOperationException($"Missing resource {name}.");
        using var reader = new StreamReader(stream);
        var html = reader.ReadToEnd().Replace(
            "src=\"/Jelana/",
            $"src=\"{Request.PathBase}/Jelana/",
            StringComparison.Ordinal);
        return Content(html, "text/html; charset=utf-8");
    }

    [HttpGet("Client.css")]
    [AllowAnonymous]
    public IActionResult Css()
    {
        Response.Headers.CacheControl = "no-cache";
        return Embedded("Web.jelana.css", "text/css; charset=utf-8");
    }

    [HttpGet("Client.js")]
    [AllowAnonymous]
    public IActionResult Js()
    {
        Response.Headers.CacheControl = "no-cache";
        return Embedded("Web.jelana.js", "text/javascript; charset=utf-8");
    }

    [HttpGet("Menu.js")]
    [AllowAnonymous]
    public IActionResult MenuJs()
    {
        Response.Headers.CacheControl = "no-cache";
        return Embedded("Web.jelana-menu.js", "text/javascript; charset=utf-8");
    }

    private FileStreamResult Embedded(string suffix, string contentType)
    {
        var name = $"{typeof(Plugin).Namespace}.{suffix}";
        return File(typeof(Plugin).Assembly.GetManifestResourceStream(name)
            ?? throw new InvalidOperationException($"Missing resource {name}."), contentType);
    }
}
