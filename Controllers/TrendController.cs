using Microsoft.AspNetCore.Mvc;
using WebAPIDateTrendSelector.Models;
using WebAPIDateTrendSelector.Services;

namespace WebAPIDateTrendSelector.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class TrendController : ControllerBase
    {
        private readonly TrendService      _trendService;
        private readonly DesigoAuthService _auth;

        public TrendController(
            TrendService      trendService,
            DesigoAuthService auth)
        {
            _trendService = trendService;
            _auth         = auth;
        }

        // ── GET /api/trend/status ──
        [HttpGet("status")]
        public IActionResult GetStatus()
        {
            return Ok(new { isRunning = _trendService.IsRunning });
        }

        // ── POST /api/trend/start?connectionId=xxx ──
        [HttpPost("start")]
        public async Task<IActionResult> StartAcquisition(
            [FromBody]  TrendRequestDto request,
            [FromQuery] string connectionId)
        {
            if (string.IsNullOrEmpty(connectionId))
                return BadRequest(new { error = "connectionId manquant." });

            var result = await _trendService.RunAsync(request, connectionId);

            if (!result.Success)
                return BadRequest(result);

            return Ok(result);
        }

        // ── POST /api/trend/cancel ──
        [HttpPost("cancel")]
        public IActionResult CancelAcquisition()
        {
            if (!_trendService.IsRunning)
                return Ok(new
                {
                    Success = false,
                    Message = "Aucune acquisition en cours."
                });

            _trendService.Cancel();

            return Ok(new
            {
                Success = true,
                Message = "Arrêt demandé. L'acquisition va s'interrompre."
            });
        }

        // ── GET /api/trend/info ──
        [HttpGet("info")]
        public async Task<IActionResult> GetInfo()
        {
            try
            {
                var systemId    = await _auth.GetSystemIdAsync();
                var projectName = _auth.ProjectName;
                var tokenValid  = !string.IsNullOrEmpty(_auth.Token);

                return Ok(new
                {
                    ProjectName = projectName,
                    SystemId    = systemId,
                    TokenValid  = tokenValid,
                    IsRunning   = _trendService.IsRunning,
                    ExpiresIn   = _auth.ExpiresIn
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new
                {
                    ProjectName = "—",
                    SystemId    = "—",
                    TokenValid  = false,
                    ExpiresIn   = 0,
                    Error       = ex.Message
                });
            }
        }

        // ── POST /api/trend/refresh-token ──
        [HttpPost("refresh-token")]
        public async Task<IActionResult> RefreshToken()
        {
            try
            {
                await _auth.ForceRefreshTokenAsync();

                return Ok(new
                {
                    Success     = true,
                    ProjectName = _auth.ProjectName,
                    SystemId    = _auth.SystemId,
                    TokenValid  = !string.IsNullOrEmpty(_auth.Token),
                    RefreshedAt = DateTime.Now.ToString("HH:mm:ss"),
                    ExpiresIn   = _auth.ExpiresIn,
                    Message     = "Token renouvelé avec succès."
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new
                {
                    Success   = false,
                    ExpiresIn = 0,
                    Message   = ex.Message
                });
            }
        }

        // ── 🆕 GET /api/trend/list ──
        [HttpGet("list")]
        public async Task<IActionResult> GetTrendList()
        {
            try
            {
                var items = await _trendService.ListAsync();
                return Ok(items);
            }
            catch (UnauthorizedAccessException)
            {
                return Unauthorized(new { error = "Token expiré (401)." });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = ex.Message });
            }
        }
    }
}