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
        private readonly float[]           _level = new float[1];
        private          float             _levelSum;
        private          int               _levelSamples;
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
                    double nextLevelSample = LEVEL_SAMPLE_SECONDS;
                    while (watch.Elapsed.TotalSeconds < _previewLength - _fadeDuration && !_token.IsCancellationRequested)
                    {
                        if (_disposed)
                        {
                            return;
                        }

                        if (_levelVolume != null && !_leveled && watch.Elapsed.TotalSeconds >= nextLevelSample)
                        {
                            nextLevelSample += LEVEL_SAMPLE_SECONDS;
                            SampleLevel();
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

        private const double LEVEL_SAMPLE_SECONDS = 0.05;
        private const int LEVEL_SAMPLES = 10;

        /// <summary>
        /// Takes one reading of the clip's RMS level (measured before the volume control) while it plays.
        /// Silent readings are skipped, so a quiet intro does not count. Once half a second of audible
        /// readings is in, possibly across loops, the leveling function turns their average into the
        /// volume used from then on.
        /// </summary>
        private void SampleLevel()
        {
            if (_mixer.GetLevel(_level) != 0 || _level[0] <= 0.001f)
            {
                return;
            }

            _levelSum += _level[0];
            if (++_levelSamples < LEVEL_SAMPLES)
            {
                return;
            }

            _leveled = true;
            try
            {
                _volume = _levelVolume!(_levelSum / _levelSamples);
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