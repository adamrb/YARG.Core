using System;
using System.Threading.Tasks;
using System.Threading;
using YARG.Core.Logging;
using YARG.Core.Song;
using System.Diagnostics;

namespace YARG.Core.Audio
{
    public class PreviewContext : IDisposable
    {
        private const double DEFAULT_PREVIEW_DURATION = 30.0;
        private const double DEFAULT_START_TIME = 20.0;
        private const double DEFAULT_END_TIME = 50.0;

        public static async Task<PreviewContext?> Create(
            SongEntry entry,
            float volume,
            float speed,
            double delaySeconds,
            double fadeDuration,
            bool enableCensoring,
            CancellationToken token,
            Func<float, float>? levelVolume = null)
        {
            try
            {
                if (delaySeconds > 0)
                {
                    await Task.Delay(TimeSpan.FromSeconds(delaySeconds), token);
                }

                // Check if canceled
                if (token.IsCancellationRequested)
                {
                    return null;
                }

                // Load the song
                var mixer = await Task.Run(() => entry.LoadPreviewAudio(speed, enableCensoring), token);
                if (mixer == null || token.IsCancellationRequested)
                {
                    mixer?.Dispose();
                    return null;
                }

                double previewLength = mixer.Length;
                double previewStartTime = 0;
                if (mixer.Channels.Count > 0)
                {
                    double previewEndTime;
                    if ((entry.PreviewStartMilliseconds < 0 || entry.PreviewStartSeconds >= previewLength)
                    &&  (entry.PreviewEndMilliseconds <= 0  || entry.PreviewEndSeconds > previewLength))
                    {
                        if (DEFAULT_END_TIME <= previewLength)
                        {
                            previewStartTime = DEFAULT_START_TIME;
                            previewEndTime = DEFAULT_END_TIME;
                        }
                        else if (DEFAULT_PREVIEW_DURATION <= previewLength)
                        {
                            previewStartTime = (previewLength - DEFAULT_PREVIEW_DURATION) / 2;
                            previewEndTime = previewStartTime + DEFAULT_PREVIEW_DURATION;
                        }
                        else
                        {
                            previewStartTime = 0;
                            previewEndTime = previewLength;
                        }
                    }
                    else if (0 <= entry.PreviewStartSeconds && entry.PreviewStartSeconds < previewLength)
                    {
                        previewStartTime = entry.PreviewStartSeconds;
                        previewEndTime = entry.PreviewEndSeconds;
                        if (previewEndTime <= previewStartTime)
                        {
                            previewEndTime = previewStartTime + DEFAULT_PREVIEW_DURATION;
                        }

                        if (previewEndTime > previewLength)
                        {
                            previewEndTime = previewLength;
                        }
                    }
                    else
                    {
                        previewEndTime = entry.PreviewEndSeconds;
                        previewStartTime = previewEndTime - DEFAULT_PREVIEW_DURATION;
                        if (previewStartTime < 0)
                        {
                            previewStartTime = 0;
                        }
                    }
                    previewLength = previewEndTime - previewStartTime;
                }

                if (fadeDuration > previewLength / 4)
                {
                    fadeDuration = previewLength / 4;
                }
                return new PreviewContext(mixer, previewStartTime, previewLength, fadeDuration, volume, token, levelVolume);
            }
            catch (OperationCanceledException)
            {
                return null;
            }
            catch (Exception ex)
            {
                YargLogger.LogException(ex, "Error while loading song preview!");
                return null;
            }
        }

        private readonly StemMixer         _mixer;
        private readonly Task              _task;
        private readonly double            _previewStartTime;
        private readonly double            _previewLength;
        private readonly double            _fadeDuration;
        private          float             _volume;
        private readonly Func<float, float>? _levelVolume;
        private          bool              _leveled;
        private readonly CancellationToken _token;
        private          bool              _disposed;

        private PreviewContext(
            StemMixer mixer,
            double previewStartTime,
            double previewLength,
            double fadeDuration,
            float volume,
            CancellationToken token,
            Func<float, float>? levelVolume)
        {
            _mixer = mixer;
            _previewStartTime = previewStartTime;
            _previewLength = previewLength;
            _fadeDuration = fadeDuration;
            _volume = volume;
            _token = token;
            _levelVolume = levelVolume;

            _task = Task.Run(Loop);
        }

        public async Task WaitForCompletionAsync()
        {
            await _task;
        }

        private async Task Loop()
        {
            try
            {
                var watch = new Stopwatch();
                while (true)
                {
                    _mixer.SetPosition(_previewStartTime);
                    _mixer.FadeIn(_volume, _fadeDuration);
                    _mixer.Play();
                    watch.Restart();
                    if (_levelVolume != null && !_leveled)
                    {
                        _leveled = true;
                        await LevelOnce(watch);
                    }

                    while (watch.Elapsed.TotalSeconds < _previewLength - _fadeDuration && !_token.IsCancellationRequested)
                    {
                        if (_disposed)
                        {
                            return;
                        }
                        // ReSharper disable once MethodSupportsCancellation
                        await Task.Delay(1);
                    }

                    watch.Restart();
                    _mixer.FadeOut(_fadeDuration);
                    while (watch.Elapsed.TotalSeconds < _fadeDuration)
                    {
                        if (_disposed)
                        {
                            return;
                        }
                        // ReSharper disable once MethodSupportsCancellation
                        await Task.Delay(1);
                    }

                    _mixer.Pause();
                    if (_token.IsCancellationRequested)
                    {
                        Dispose();
                        return;
                    }
                }
            }
            catch (Exception ex)
            {
                YargLogger.LogException(ex, "Error while looping song preview!");
                // Nothing else will stop the audio once the loop is gone
                Dispose();
            }
        }

        private const double LEVEL_MEASURE_SECONDS = 0.5;
        private const int LEVEL_SAMPLE_MILLISECONDS = 50;

        /// <summary>
        /// Listens to the start of the first play and hands the clip's average RMS level (measured before
        /// the volume control) to the leveling function, which returns the volume to use from then on.
        /// Stops early for clips shorter than the measurement, and does nothing once the preview is canceled.
        /// </summary>
        private async Task LevelOnce(Stopwatch playTime)
        {
            double limit = Math.Min(LEVEL_MEASURE_SECONDS, _previewLength - _fadeDuration);
            var level = new float[1];
            float sum = 0f;
            int samples = 0;
            while (playTime.Elapsed.TotalSeconds + LEVEL_SAMPLE_MILLISECONDS / 1000.0 <= limit)
            {
                // ReSharper disable once MethodSupportsCancellation
                await Task.Delay(LEVEL_SAMPLE_MILLISECONDS);
                if (_disposed || _token.IsCancellationRequested)
                {
                    return;
                }

                if (_mixer.GetLevel(level) == 0 && level[0] > 0.001f)
                {
                    sum += level[0];
                    samples++;
                }
            }

            if (samples == 0)
            {
                return;
            }

            try
            {
                _volume = _levelVolume!(sum / samples);
            }
            catch (Exception ex)
            {
                // Keep playing at the original volume
                YargLogger.LogException(ex, "Error while leveling song preview!");
                return;
            }

            _mixer.FadeIn(_volume, _fadeDuration);
        }

        private void Dispose(bool disposing)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            if (disposing)
            {
                _mixer.Dispose();
            }
        }

        public void Dispose()
        {
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }
    }
}