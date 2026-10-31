using System;
using System.Threading;
using System.Threading.Tasks;
using Velopack;
using Velopack.Sources;

namespace CosmOrganizer
{
    public class UpdateService : IDisposable
    {
        private const string RepoUrl = "https://github.com/CosmoNetinfo/CosmOrganizer";
        private readonly Func<bool> _isAutoUpdateEnabled;
        private readonly Action<string> _onUpdateReady;
        private readonly System.Threading.Timer _timer;
        private UpdateManager? _updateManager;
        private UpdateInfo? _downloadedUpdate;
        private bool _isChecking;

        public UpdateService(Func<bool> isAutoUpdateEnabled, Action<string> onUpdateReady)
        {
            _isAutoUpdateEnabled = isAutoUpdateEnabled;
            _onUpdateReady = onUpdateReady;

            try
            {
                var source = new GithubSource(RepoUrl, null, false);
                _updateManager = new UpdateManager(source);
            }
            catch
            {
                _updateManager = null;
            }

            // Check every 6 hours (6 * 3600 * 1000 ms)
            _timer = new System.Threading.Timer(async _ => await CheckAndDownloadAsync(), null, Timeout.Infinite, Timeout.Infinite);
        }

        public void Start()
        {
            // Initial check delayed by 5 seconds so app UI loads first
            _ = Task.Run(async () =>
            {
                await Task.Delay(5000);
                await CheckAndDownloadAsync();
                _timer.Change(TimeSpan.FromHours(6), TimeSpan.FromHours(6));
            });
        }

        public async Task CheckAndDownloadAsync()
        {
            if (_updateManager == null || !_updateManager.IsInstalled)
                return;

            if (!_isAutoUpdateEnabled())
                return;

            if (_isChecking || _downloadedUpdate != null)
                return;

            try
            {
                _isChecking = true;
                var updateInfo = await _updateManager.CheckForUpdatesAsync();
                if (updateInfo == null)
                    return;

                await _updateManager.DownloadUpdatesAsync(updateInfo);
                _downloadedUpdate = updateInfo;

                // Notify UI: "Aggiornamento {version} pronto — Riavvia per installare"
                string targetVersion = updateInfo.TargetFullRelease?.Version?.ToString() ?? "nuovo";
                _onUpdateReady(targetVersion);
            }
            catch
            {
                // Never fail or disturb the user if offline or update check fails
            }
            finally
            {
                _isChecking = false;
            }
        }

        public void ApplyOnShutdown()
        {
            if (_updateManager != null && _updateManager.IsInstalled && _downloadedUpdate != null)
            {
                try
                {
                    _updateManager.WaitExitThenApplyUpdates(_downloadedUpdate);
                }
                catch
                {
                }
            }
        }

        public void RestartAndApply()
        {
            if (_updateManager != null && _updateManager.IsInstalled && _downloadedUpdate != null)
            {
                try
                {
                    _updateManager.ApplyUpdatesAndRestart(_downloadedUpdate);
                }
                catch
                {
                }
            }
        }

        public void Dispose()
        {
            _timer?.Dispose();
        }
    }
}
