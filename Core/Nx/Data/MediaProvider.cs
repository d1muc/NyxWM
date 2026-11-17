using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Windows.Media.Control;

namespace nyxWM.Nx
{
    /// <summary>
    /// Now-playing data through the Windows System Media Transport Controls (SMTC).
    /// Reads are served from a cached snapshot: the WinRT calls are asynchronous
    /// and far too slow for a render path, so a background poll keeps the cache warm.
    ///
    /// If the machine has no SMTC support (Server SKU, stripped builds, or a
    /// session that refused access) this provider degrades silently: every key
    /// returns an empty value and <c>hasSession</c> stays false.
    /// </summary>
    internal sealed class MediaProvider : NxProviderBase, IDisposable
    {
        private const double PollIntervalMs = 1000.0;
        private static readonly TimeSpan PositionPollInterval = TimeSpan.FromSeconds(1);

        private static readonly string[] KeyNames =
        {
            "title", "artist", "album", "albumartist", "track", "genre",
            "status", "playing", "paused", "stopped", "source", "app",
            "position", "duration", "progress", "remaining",
            "hassession", "canplay", "canpause", "canskip", "canseek"
        };

        private readonly object _sync = new();

        private readonly CancellationTokenSource _shutdown = new();
        private Task? _poller;

        private string _title = string.Empty;
        private string _artist = string.Empty;
        private string _album = string.Empty;
        private string _albumArtist = string.Empty;
        private string _track = string.Empty;
        private string _genre = string.Empty;
        private string _sourceApp = string.Empty;
        private string _status = string.Empty;

        private double _positionSeconds;
        private double _durationSeconds;

        private bool _hasSession;
        private bool _canPlay;
        private bool _canPause;
        private bool _canSkip;
        private bool _canSeek;

        private bool _isPlaying;

        /// <summary>Starts the background poller. Safe to call once; no-op while running.</summary>
        internal void Start()
        {
            if (_poller != null)
                return;

            _poller = Task.Run(() => PollLoopAsync(_shutdown.Token));
        }

        /// <summary>
        /// Called by the runtime every frame. Work is done by the poller, so this
        /// only extrapolates the playback position between polls so a progress bar
        /// animates smoothly at the display rate instead of stepping once a second.
        /// </summary>
        internal void Update(double deltaMs)
        {
            lock (_sync)
            {
                if (!_isPlaying || _durationSeconds <= 0.0)
                    return;

                _positionSeconds = Math.Min(_durationSeconds, _positionSeconds + deltaMs / 1000.0);
            }
        }

        public override string Namespace => "media";

        public override IEnumerable<string> Keys => KeyNames;

        public override NxValue? Read(string key, string? argument)
        {
            lock (_sync)
            {
                switch (key.ToLowerInvariant())
                {
                    case "title":
                        return Text(_title);
                    case "artist":
                        return Text(_artist);
                    case "album":
                        return Text(_album);
                    case "albumartist":
                        return Text(_albumArtist);
                    case "track":
                        return Text(_track);
                    case "genre":
                        return Text(_genre);
                    case "source":
                    case "app":
                        return Text(_sourceApp);

                    case "status":
                        return Text(_status);
                    case "playing":
                        return Flag(_isPlaying);
                    case "paused":
                        return Flag(string.Equals(_status, "Paused", StringComparison.OrdinalIgnoreCase));
                    case "stopped":
                        return Flag(string.Equals(_status, "Stopped", StringComparison.OrdinalIgnoreCase));

                    case "position":
                        return Number(Math.Round(_positionSeconds, 1));
                    case "duration":
                        return Number(Math.Round(_durationSeconds, 1));
                    case "progress":
                        return _durationSeconds <= 0.0
                            ? Number(0)
                            : Number(Math.Round(_positionSeconds / _durationSeconds, 3));
                    case "remaining":
                        return Number(Math.Round(Math.Max(0.0, _durationSeconds - _positionSeconds), 1));

                    case "hassession":
                        return Flag(_hasSession);
                    case "canplay":
                        return Flag(_canPlay);
                    case "canpause":
                        return Flag(_canPause);
                    case "canskip":
                        return Flag(_canSkip);
                    case "canseek":
                        return Flag(_canSeek);

                    default:
                        return null;
                }
            }
        }

        private async Task PollLoopAsync(CancellationToken token)
        {
            GlobalSystemMediaTransportControlsSessionManager? manager = null;
            DateTime lastProperties = DateTime.MinValue;
            DateTime lastTimeline = DateTime.MinValue;

            try
            {
                manager = await GlobalSystemMediaTransportControlsSessionManager
                    .RequestAsync()
                    .AsTask(token);
            }
            catch (Exception ex)
            {
                // No SMTC on this machine: stay empty rather than throw every frame.
                Console.WriteLine($"MediaProvider: SMTC unavailable: {ex.Message}");
                return;
            }

            while (!token.IsCancellationRequested)
            {
                try
                {
                    GlobalSystemMediaTransportControlsSession? session = manager.GetCurrentSession();

                    if (session == null)
                    {
                        ClearSession();
                    }
                    else
                    {
                        var info = session.GetPlaybackInfo();
                        UpdateTransport(info);

                        DateTime now = DateTime.UtcNow;

                        // Media properties are near-static; read them once a second
                        // or whenever the key changed.
                        if ((now - lastProperties).TotalSeconds >= PollIntervalMs / 1000.0)
                        {
                            lastProperties = now;
                            await ReadPropertiesAsync(session, token);
                        }

                        if ((now - lastTimeline).TotalSeconds >= PositionPollInterval.TotalSeconds)
                        {
                            lastTimeline = now;
                            ReadTimeline(session);
                        }
                    }
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"MediaProvider: poll failed: {ex.Message}");
                    ClearSession();
                }

                await Task.Delay((int)PollIntervalMs, token).ConfigureAwait(false);
            }
        }

        private void UpdateTransport(GlobalSystemMediaTransportControlsSessionPlaybackInfo? info)
        {
            lock (_sync)
            {
                if (info == null)
                {
                    _canPlay = false;
                    _canPause = false;
                    _canSkip = false;
                    _canSeek = false;
                    return;
                }

                _canPlay = info.Controls.IsPlayEnabled || info.Controls.IsPlayPauseToggleEnabled;
                _canPause = info.Controls.IsPauseEnabled || info.Controls.IsPlayPauseToggleEnabled;
                _canSkip = info.Controls.IsNextEnabled || info.Controls.IsPreviousEnabled;
                _canSeek = info.Controls.IsPlaybackPositionEnabled;

                _status = info.PlaybackStatus.ToString();
                _isPlaying = info.PlaybackStatus
                    == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
            }
        }

        private async Task ReadPropertiesAsync(
            GlobalSystemMediaTransportControlsSession session,
            CancellationToken token)
        {
            GlobalSystemMediaTransportControlsSessionMediaProperties? properties =
                await session.TryGetMediaPropertiesAsync().AsTask(token);

            lock (_sync)
            {
                _hasSession = true;
                _sourceApp = session.SourceAppUserModelId ?? string.Empty;
                _title = properties?.Title ?? string.Empty;
                _artist = properties?.Artist ?? string.Empty;
                _album = properties?.AlbumTitle ?? string.Empty;
                _albumArtist = properties?.AlbumArtist ?? string.Empty;
                _track = properties?.TrackNumber is { } number && number > 0
                    ? number.ToString()
                    : string.Empty;
                _genre = properties == null || properties.Genres.Count == 0
                    ? string.Empty
                    : properties.Genres[0];
            }
        }

        private void ReadTimeline(GlobalSystemMediaTransportControlsSession session)
        {
            GlobalSystemMediaTransportControlsSessionTimelineProperties? timeline =
                session.GetTimelineProperties();

            lock (_sync)
            {
                if (timeline == null)
                    return;

                _positionSeconds = Math.Max(0.0, timeline.Position.TotalSeconds);
                _durationSeconds = Math.Max(0.0, timeline.EndTime.TotalSeconds);
            }
        }

        private void ClearSession()
        {
            lock (_sync)
            {
                if (!_hasSession)
                    return;

                _hasSession = false;
                _isPlaying = false;
                _status = string.Empty;
                _title = string.Empty;
                _artist = string.Empty;
                _album = string.Empty;
                _albumArtist = string.Empty;
                _track = string.Empty;
                _genre = string.Empty;
                _sourceApp = string.Empty;
                _positionSeconds = 0.0;
                _durationSeconds = 0.0;
                _canPlay = false;
                _canPause = false;
                _canSkip = false;
                _canSeek = false;
            }
        }

        public void Dispose()
        {
            try
            {
                _shutdown.Cancel();
                _poller?.Wait(TimeSpan.FromMilliseconds(200));
            }
            catch (Exception)
            {
                // Shutdown races are harmless: the poller exits with the process.
            }
            finally
            {
                _shutdown.Dispose();
            }
        }
    }
}
