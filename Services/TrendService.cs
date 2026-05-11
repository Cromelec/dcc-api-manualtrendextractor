using System.Net.Sockets;
using Newtonsoft.Json;
using Microsoft.AspNetCore.SignalR;
using WebAPIDateTrendSelector.Hubs;
using WebAPIDateTrendSelector.Models;

namespace WebAPIDateTrendSelector.Services
{
    public class TrendService
    {
        private const int MANAGEMENT_VIEW = 0;
        private const int LOGICAL_VIEW    = 2;

        private readonly DesigoAuthService     _auth;
        private readonly IHubContext<TrendHub> _hub;
        private readonly ILogger<TrendService> _logger;

        private volatile bool _isRunning = false;

        private CancellationTokenSource? _cts = null;

        public bool IsRunning => _isRunning;

        public TrendService(
            DesigoAuthService auth,
            IHubContext<TrendHub> hub,
            ILogger<TrendService> logger)
        {
            _auth   = auth;
            _hub    = hub;
            _logger = logger;
        }

        // ── Annulation ──
        public void Cancel()
        {
            if (_cts != null && !_cts.IsCancellationRequested)
            {
                _cts.Cancel();
                _logger.LogWarning("⛔ Annulation demandée par l'utilisateur.");
            }
        }

        // ══════════════════════════════════════════
        //  🆕 Lister tous les trends disponibles
        // ══════════════════════════════════════════
        public async Task<List<TrendItemDto>> ListAsync()
        {
            var systemId = await _auth.GetSystemIdAsync();
            var client   = await _auth.GetAuthenticatedClientAsync();

            var listResponse = await client.GetAsync(
                $"{_auth.ApiUrl}/api/trendseriesinfo/{systemId}");

            if (listResponse.StatusCode == System.Net.HttpStatusCode.Unauthorized)
                throw new UnauthorizedAccessException("Token expiré (401).");

            listResponse.EnsureSuccessStatusCode();

            var listJson       = await listResponse.Content.ReadAsStringAsync();
            var trendCollector = JsonConvert.DeserializeObject<List<TrendCollector>>(listJson)
                                 ?? new List<TrendCollector>();

            var result = new List<TrendItemDto>();

            foreach (var trend in trendCollector)
            {
                if (trend.ObjectId == null || trend.TrendseriesId == null)
                    continue;

                var location          = await GetLocationAsync(client, systemId, trend.ObjectId);
                var formattedLocation = FormatLocation(location);

                result.Add(new TrendItemDto
                {
                    TrendseriesId     = trend.TrendseriesId,
                    ObjectId          = trend.ObjectId,
                    FormattedLocation = formattedLocation
                });
            }

            return result;
        }

        // ══════════════════════════════════════════
        //  Point d'entrée principal — RunAsync
        // ══════════════════════════════════════════
        public async Task<TrendResultDto> RunAsync(
            TrendRequestDto request,
            string connectionId)
        {
            if (_isRunning)
                return new TrendResultDto
                {
                    Success = false,
                    Message = "Une acquisition est déjà en cours."
                };

            _isRunning = true;
            _cts       = new CancellationTokenSource();
            var ct          = _cts.Token;
            var start       = DateTime.Now;
            int totalValues = 0;

            try
            {
                // ── Calcul des dates ──
                DateTime fromDateTime;
                DateTime toDateTime;

                if (request.Mode == "single" && !string.IsNullOrEmpty(request.DateSingle))
                {
                    var day      = DateTime.Parse(request.DateSingle);
                    fromDateTime = new DateTime(day.Year, day.Month, day.Day,  0,  0,  0);
                    toDateTime   = new DateTime(day.Year, day.Month, day.Day, 23, 59, 59);
                }
                else if (request.Mode == "range"
                    && !string.IsNullOrEmpty(request.DateFrom)
                    && !string.IsNullOrEmpty(request.DateTo))
                {
                    var from     = DateTime.Parse(request.DateFrom);
                    var to       = DateTime.Parse(request.DateTo);
                    fromDateTime = new DateTime(from.Year, from.Month, from.Day,  0,  0,  0);
                    toDateTime   = new DateTime(to.Year,   to.Month,   to.Day,   23, 59, 59);
                }
                else
                {
                    return new TrendResultDto
                    {
                        Success = false,
                        Message = "Paramètres de date invalides."
                    };
                }

                string fromStr = fromDateTime.ToString("yyyy-MM-ddTHH:mm:ss.fffZ");
                string toStr   = toDateTime.ToString("yyyy-MM-ddTHH:mm:ss.fffZ");

                await SendLog(connectionId,
                    $"📅 Période : {fromDateTime:dd/MM/yyyy} → {toDateTime:dd/MM/yyyy}");

                // ── Auth & System ID ──
                await SendLog(connectionId, "🔐 Authentification en cours...");
                var systemId = await _auth.GetSystemIdAsync();
                await SendLog(connectionId, $"✅ Connecté — System ID : {systemId}");

                var client = await _auth.GetAuthenticatedClientAsync();

                // ── Récupération de la liste des trends ──
                await SendLog(connectionId, "📡 Récupération de la liste des trends...");
                var listResponse = await client.GetAsync(
                    $"{_auth.ApiUrl}/api/trendseriesinfo/{systemId}", ct);

                if (listResponse.StatusCode == System.Net.HttpStatusCode.Unauthorized)
                {
                    await SendTokenExpired(connectionId);
                    return new TrendResultDto
                    {
                        Success = false,
                        Message = "Session expirée (401). Veuillez renouveler le token."
                    };
                }

                listResponse.EnsureSuccessStatusCode();

                var listJson       = await listResponse.Content.ReadAsStringAsync(ct);
                var trendCollector = JsonConvert.DeserializeObject<List<TrendCollector>>(listJson)
                                     ?? new List<TrendCollector>();

                // ── 🆕 Filtrage sur la sélection utilisateur ──
                var hasSelection = request.SelectedIds != null && request.SelectedIds.Count > 0;

                if (hasSelection)
                {
                    var selectedSet = new HashSet<string>(request.SelectedIds!);
                    trendCollector  = trendCollector
                        .Where(t => t.TrendseriesId != null
                                 && selectedSet.Contains(t.TrendseriesId))
                        .ToList();

                    await SendLog(connectionId,
                        $"🎯 Filtrage : {trendCollector.Count} trend(s) sélectionné(s).");
                }

                int total = trendCollector.Count;
                await SendLog(connectionId, $"📊 {total} trend(s) à traiter.");

                if (total == 0)
                    return new TrendResultDto
                    {
                        Success     = true,
                        Message     = "Aucun trend disponible.",
                        TotalSeries = 0,
                        TotalValues = 0,
                        DurationSec = (DateTime.Now - start).TotalSeconds
                    };

                // ── Boucle d'acquisition ──
                int processed = 0;

                for (int i = 0; i < total; i++)
                {
                    if (ct.IsCancellationRequested)
                    {
                        await SendLog(connectionId,
                            $"⛔ Acquisition annulée après {processed} série(s) traitée(s).");
                        break;
                    }

                    var trend = trendCollector[i];
                    if (trend.ObjectId == null) continue;

                    var location          = await GetLocationAsync(client, systemId, trend.ObjectId, ct);
                    var formattedLocation = FormatLocation(location);

                    processed++;
                    int percent = Math.Min((int)((double)processed / total * 100), 100);

                    await _hub.Clients.Client(connectionId).SendAsync("Progress", new ProgressUpdate
                    {
                        Current  = processed,
                        Total    = total,
                        Percent  = percent,
                        Location = formattedLocation,
                        Log      = $"[{processed}/{total}] {formattedLocation}"
                    }, ct);

                    try
                    {
                        var seriesResponse = await client.GetAsync(
                            $"{_auth.ApiUrl}/api/trendseries/{trend.TrendseriesId}" +
                            $"?from={fromStr}&to={toStr}", ct);

                        if (seriesResponse.StatusCode == System.Net.HttpStatusCode.Unauthorized)
                        {
                            await SendTokenExpired(connectionId);
                            return new TrendResultDto
                            {
                                Success     = false,
                                Message     = "Session expirée (401). Veuillez renouveler le token.",
                                TotalSeries = processed,
                                TotalValues = totalValues,
                                DurationSec = (DateTime.Now - start).TotalSeconds
                            };
                        }

                        seriesResponse.EnsureSuccessStatusCode();

                        var jsonResponse = await seriesResponse.Content.ReadAsStringAsync(ct);
                        var trendSeries  = JsonConvert.DeserializeObject<TrendSeries>(jsonResponse);

                        if (trendSeries?.SeriesPropertyId != null
                            && trendSeries.Series?.Count > 0)
                        {
                            totalValues += trendSeries.Series.Count;
                            SaveJson($"{formattedLocation}", jsonResponse);

                            await SendLog(connectionId,
                                $"  ✅ {trendSeries.Series.Count} valeur(s) — {formattedLocation}");
                        }
                        else
                        {
                            await SendLog(connectionId,
                                $"  ⚠️ Aucune donnée pour {trend.TrendseriesId}");
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        await SendLog(connectionId,
                            $"⛔ Requête annulée sur {trend.TrendseriesId}");
                        break;
                    }
                    catch (SocketException ex) when (ex.ErrorCode == 995)
                    {
                        await SendLog(connectionId,
                            $"  ❌ Timeout (995) sur {trend.TrendseriesId}");
                    }
                    catch (Exception ex)
                    {
                        await SendLog(connectionId,
                            $"  ❌ Erreur : {ex.Message}");
                    }
                }

                var duration  = (DateTime.Now - start).TotalSeconds;
                bool canceled = ct.IsCancellationRequested;

                await SendLog(connectionId, canceled
                    ? $"⛔ Arrêté en {duration:F1}s — {processed} séries — {totalValues} valeurs."
                    : $"🏁 Terminé en {duration:F1}s — {processed} séries — {totalValues} valeurs.");

                return new TrendResultDto
                {
                    Success     = !canceled,
                    Message     = canceled
                                    ? $"Acquisition annulée après {processed} série(s)."
                                    : "Acquisition terminée avec succès.",
                    TotalSeries = processed,
                    TotalValues = totalValues,
                    DurationSec = duration
                };
            }
            catch (OperationCanceledException)
            {
                var duration = (DateTime.Now - start).TotalSeconds;
                await SendLog(connectionId, $"⛔ Acquisition interrompue ({duration:F1}s).");
                return new TrendResultDto
                {
                    Success     = false,
                    Message     = "Acquisition annulée par l'utilisateur.",
                    TotalSeries = 0,
                    TotalValues = totalValues,
                    DurationSec = duration
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erreur lors de l'acquisition des trends.");
                await SendLog(connectionId, $"❌ Erreur critique : {ex.Message}");
                return new TrendResultDto
                {
                    Success = false,
                    Message = ex.Message
                };
            }
            finally
            {
                _isRunning = false;
                _cts?.Dispose();
                _cts = null;
            }
        }

        // ── Helpers ──

        private async Task<string> GetLocationAsync(
            HttpClient client, string systemId, string objectId,
            CancellationToken ct = default)
        {
            try
            {
                var escaped  = Uri.EscapeDataString(objectId);
                var response = await client.GetAsync(
                    $"{_auth.ApiUrl}/api/systembrowser/{systemId}" +
                    $"/?searchString={escaped}&searchOption=2", ct);

                if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
                    return objectId;

                response.EnsureSuccessStatusCode();

                var json   = await response.Content.ReadAsStringAsync(ct);
                var result = JsonConvert.DeserializeObject<SystemBrowserResponse>(json);

                if (result?.Total > 0)
                {
                    string location = "";
                    foreach (var node in result.Nodes)
                    {
                        if (location == "" && node.ViewType == MANAGEMENT_VIEW)
                            location = node.Location;
                        if (node.ViewType == LOGICAL_VIEW)
                            location = node.Location;
                    }
                    return location;
                }
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                _logger.LogWarning($"GetLocation failed for {objectId}: {ex.Message}");
            }
            return objectId;
        }

        private static string FormatLocation(string location)
        {
            if (location.Contains(':'))
                location = location.Split(':')[1];

            return location
                .Replace(".",  "@")
                .Replace(@"\", "_")
                .Replace("/",  "_");
        }

        private void SaveJson(string fileName, string content)
        {
            var folder = _auth.FolderPath;
            if (!Directory.Exists(folder))
                Directory.CreateDirectory(folder);

            File.WriteAllText(Path.Combine(folder, $"{fileName}_MANUALOUTPUT.json"), content);
        }

        private async Task SendLog(string connectionId, string message)
        {
            _logger.LogInformation(message);
            await _hub.Clients.Client(connectionId).SendAsync("Log", message);
        }

        private async Task SendTokenExpired(string connectionId)
        {
            const string msg = "⚠️ Session expirée — Token invalide (401).";
            _logger.LogWarning(msg);
            await _hub.Clients.Client(connectionId).SendAsync("Log", msg);
            await _hub.Clients.Client(connectionId).SendAsync("TokenExpired");
        }
    }
}