using System.Net.Http.Headers;
using Newtonsoft.Json;
using WebAPIDateTrendSelector.Models;

namespace WebAPIDateTrendSelector.Services
{
    public class DesigoConfig
    {
        public string ApiUrl                 { get; set; } = "";
        public string Username               { get; set; } = "";
        public string Password               { get; set; } = "";
        public string TrendsFolderPath       { get; set; } = "./trends/";
        public int    TrendCycleDelaySeconds { get; set; } = 120;
    }

    public class DesigoAuthService
    {
        private readonly IHttpClientFactory         _factory;
        private readonly DesigoConfig               _config;
        private readonly ILogger<DesigoAuthService> _logger;

        private string   _currentToken       = "";
        private string   _currentSystemId    = "";
        private string   _currentProjectName = "";
        private DateTime _tokenExpiry        = DateTime.MinValue;
        private int      _expiresInSeconds   = 0; // ← 🆕 Stockage du expires_in réel

        private readonly SemaphoreSlim _tokenLock = new(1, 1);

        public string ApiUrl         => _config.ApiUrl;
        public string SystemId       => _currentSystemId;
        public string Token          => _currentToken;
        public string FolderPath     => _config.TrendsFolderPath;
        public string ProjectName    => _currentProjectName;
        public int    ExpiresIn      => _expiresInSeconds; // ← 🆕 Propriété publique

        public DesigoAuthService(
            IHttpClientFactory factory,
            IConfiguration configuration,
            ILogger<DesigoAuthService> logger)
        {
            _factory = factory;
            _logger  = logger;
            _config  = configuration.GetSection("DesigoCC").Get<DesigoConfig>()
                       ?? new DesigoConfig();
        }

        // ── Obtenir un token valide (avec renouvellement auto) ──
        public async Task<string> GetValidTokenAsync()
        {
            await _tokenLock.WaitAsync();
            try
            {
                if (!string.IsNullOrEmpty(_currentToken) && DateTime.Now < _tokenExpiry)
                    return _currentToken;

                var client = _factory.CreateClient("DesigoClient");
                var values = new Dictionary<string, string>
                {
                    { "grant_type", "password"      },
                    { "username",   _config.Username },
                    { "password",   _config.Password }
                };

                var response = await client.PostAsync(
                    $"{_config.ApiUrl}/api/token",
                    new FormUrlEncodedContent(values));

                response.EnsureSuccessStatusCode();

                var json      = await response.Content.ReadAsStringAsync();
                var tokenData = JsonConvert.DeserializeObject<TokenResponse>(json);

                _currentToken     = tokenData?.access_token ?? "";
                _expiresInSeconds = tokenData?.expires_in ?? 1500; // ← 🆕 Valeur réelle
                _tokenExpiry      = DateTime.Now.AddSeconds(_expiresInSeconds); // ← 🆕 Basé sur expires_in réel

                _logger.LogInformation(
                    $"Token obtenu — expire dans {_expiresInSeconds}s " +
                    $"(à {_tokenExpiry:HH:mm:ss})");

                return _currentToken;
            }
            finally
            {
                _tokenLock.Release();
            }
        }

        // ── Forcer le renouvellement du token ──
        public async Task<string> ForceRefreshTokenAsync()
        {
            await _tokenLock.WaitAsync();
            try
            {
                _currentToken       = "";
                _currentSystemId    = "";
                _currentProjectName = "";
                _tokenExpiry        = DateTime.MinValue;
                _expiresInSeconds   = 0; // ← 🆕 Reset
            }
            finally
            {
                _tokenLock.Release();
            }

            var token = await GetValidTokenAsync();
            await GetSystemIdAsync();
            return token;
        }

        // ── Récupérer le System ID local (+ nom du projet) ──
        public async Task<string> GetSystemIdAsync()
        {
            if (!string.IsNullOrEmpty(_currentSystemId))
                return _currentSystemId;

            var token  = await GetValidTokenAsync();
            var client = _factory.CreateClient("DesigoClient");
            client.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", token);

            var response = await client.GetAsync($"{_config.ApiUrl}/api/systems/local");
            response.EnsureSuccessStatusCode();

            var json    = await response.Content.ReadAsStringAsync();
            var systems = JsonConvert.DeserializeObject<SystemsRepresentation>(json);

            if (systems?.Systems?.Count > 0)
            {
                _currentSystemId    = systems.Systems[0].Id.ToString() ?? "";
                _currentProjectName = systems.Systems[0].Name ?? "Projet inconnu";
                _logger.LogInformation(
                    $"System ID : {_currentSystemId} — Projet : {_currentProjectName}");
                return _currentSystemId;
            }

            throw new Exception("Aucun système trouvé dans Desigo CC.");
        }

        // ── Heartbeat ──
        public async Task HeartbeatAsync()
        {
            var token  = await GetValidTokenAsync();
            var client = _factory.CreateClient("DesigoClient");
            client.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", token);

            await client.PostAsync($"{_config.ApiUrl}/api/heartbeat", null);
        }

        // ── Créer un client HTTP authentifié ──
        public async Task<HttpClient> GetAuthenticatedClientAsync()
        {
            var token  = await GetValidTokenAsync();
            var client = _factory.CreateClient("DesigoClient");
            client.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", token);
            client.Timeout = TimeSpan.FromSeconds(180);
            return client;
        }
    }
}