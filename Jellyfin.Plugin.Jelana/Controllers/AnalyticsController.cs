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

    [HttpGet("Client.css")]
    [AllowAnonymous]
    public IActionResult Css() => Embedded("Web.jelana.css", "text/css; charset=utf-8");

    [HttpGet("Client.js")]
    [AllowAnonymous]
    public IActionResult Js() => Embedded("Web.jelana.js", "text/javascript; charset=utf-8");

    private FileStreamResult Embedded(string suffix, string contentType)
    {
        var name = $"{typeof(Plugin).Namespace}.{suffix}";
        return File(typeof(Plugin).Assembly.GetManifestResourceStream(name)
            ?? throw new InvalidOperationException($"Missing resource {name}."), contentType);
    }
}
