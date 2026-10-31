using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using DuckCam.Core;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Rendering;

namespace DuckCam.Rendering
{
    internal sealed class VideoRecorder
    {
        public static readonly int[] FrameRates = { 24, 30, 60, 120, 240 };
        public static readonly string[] Resolutions = { "Native", "720p", "1080p", "1440p", "4K" };
        private static readonly int[] ResolutionHeights = { 0, 720, 1080, 1440, 2160 };
        public static readonly string[] Speeds = { "Fast", "Balanced", "Best" };
        private static readonly string[] SpeedPresets = { "veryfast", "medium", "slow" };

        public int FrameRateIndex { get; set; } = 2;
        public int ResolutionIndex { get; set; }
        public int Quality { get; set; } = 18;
        public int SpeedIndex { get; set; } = 1;
        public bool Audio { get; set; } = true;
        public bool FlipVertical { get; set; } = true;

        public bool Recording { get; private set; }
        public bool Finishing { get; private set; }
        public string Status { get; private set; } = "idle";
        public int Frames { get; private set; }
        public float FrameStep => 1f / FrameRates[Mathf.Clamp(FrameRateIndex, 0, FrameRates.Length - 1)];
        public double Seconds => Frames * (double)FrameStep;

        public static string Directory => Path.Combine(DuckCamPlugin.DataRoot, "Videos");

        public static string PluginFfmpeg => Path.Combine(Path.GetDirectoryName(typeof(VideoRecorder).Assembly.Location) ?? "", "ffmpeg.exe");

        private Process _ffmpeg;
        private Thread _writer;
        private BlockingCollection<byte[]> _queue;
        private RenderTexture _rt;
        private int _width;
        private int _height;
        private int _pendingReadbacks;
        private int _savedCaptureFramerate;
        private string _videoPath;
        private string _finalPath;
        private string _audioPath;
        private FileStream _audioStream;
        private BinaryWriter _audioWriter;
        private long _audioSamples;
        private int _audioChannels;
        private int _audioRate;
        private Coroutine _routine;
        private MonoBehaviour _host;
        private volatile string _writeError;
        private readonly StringBuilder _ffmpegLog = new StringBuilder();

        private static string _ffmpegCache;
        private static float _ffmpegCheckedAt = -100f;

        public static string FindFfmpeg()
        {
            if (Time.unscaledTime - _ffmpegCheckedAt < 3f)
            {
                return _ffmpegCache;
            }
            _ffmpegCheckedAt = Time.unscaledTime;
            _ffmpegCache = LocateFfmpeg();
            return _ffmpegCache;
        }

        private static string LocateFfmpeg()
        {
            string custom = DuckCamConfig.FfmpegPath.Value;
            if (!string.IsNullOrWhiteSpace(custom) && File.Exists(custom))
            {
                return custom;
            }
            if (File.Exists(PluginFfmpeg))
            {
                return PluginFfmpeg;
            }
            string path = System.Environment.GetEnvironmentVariable("PATH") ?? "";
            foreach (string dir in path.Split(Path.PathSeparator))
            {
                try
                {
                    string candidate = Path.Combine(dir.Trim(), "ffmpeg.exe");
                    if (File.Exists(candidate))
                    {
                        return candidate;
                    }
                }
                catch (ArgumentException)
                {
                }
            }
            return null;
        }

        public bool Start(MonoBehaviour host, out string error)
        {
            error = null;
            if (Recording || Finishing)
            {
                error = Finishing ? "the last video is still being finished" : "already recording";
                return false;
            }
            string ffmpeg = FindFfmpeg();
            if (ffmpeg == null)
            {
                error = "ffmpeg.exe not found. Put it in " + Path.GetDirectoryName(PluginFfmpeg) + " (or set its path in the config).";
                return false;
            }

            _width = Screen.width & ~1;
            _height = Screen.height & ~1;
            int fps = FrameRates[Mathf.Clamp(FrameRateIndex, 0, FrameRates.Length - 1)];
            int targetHeight = ResolutionHeights[Mathf.Clamp(ResolutionIndex, 0, ResolutionHeights.Length - 1)];
            string scale = targetHeight > 0 ? $",scale=-2:{targetHeight}:flags=lanczos" : "";

            System.IO.Directory.CreateDirectory(Directory);
            string stamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
            _finalPath = Path.Combine(Directory, $"DuckCam_{stamp}_{GameContext.LocationId}.mp4");
            _videoPath = Audio ? Path.Combine(Directory, $"DuckCam_{stamp}.video.mp4") : _finalPath;
            _audioPath = Audio ? Path.Combine(Directory, $"DuckCam_{stamp}.audio.wav") : null;

            string args = $"-hide_banner -loglevel error -y -f rawvideo -pix_fmt rgba -s {_width}x{_height} -r {fps} -i - "
                          + $"-vf \"{(FlipVertical ? "vflip" : "null")}{scale}\" -c:v libx264 -preset {SpeedPresets[Mathf.Clamp(SpeedIndex, 0, 2)]} -crf {Mathf.Clamp(Quality, 0, 51)} "
                          + $"-pix_fmt yuv420p -movflags +faststart \"{_videoPath}\"";
            try
            {
                _ffmpegLog.Clear();
                _ffmpeg = Process.Start(new ProcessStartInfo
                {
                    FileName = ffmpeg,
                    Arguments = args,
                    UseShellExecute = false,
                    RedirectStandardInput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                });
                if (_ffmpeg == null)
                {
                    throw new InvalidOperationException("process did not start");
                }
                _ffmpeg.ErrorDataReceived += (s, e) =>
                {
                    if (e.Data != null)
                    {
                        lock (_ffmpegLog)
                        {
                            _ffmpegLog.AppendLine(e.Data);
                        }
                    }
                };
                _ffmpeg.BeginErrorReadLine();
            }
            catch (Exception ex)
            {
                error = "FFmpeg could not start: " + ex.Message;
                _ffmpeg = null;
                return false;
            }

            _rt = new RenderTexture(Screen.width, Screen.height, 0, RenderTextureFormat.ARGB32);
            _rt.Create();
            _queue = new BlockingCollection<byte[]>(new ConcurrentQueue<byte[]>());
            _writeError = null;
            Stream stdin = _ffmpeg.StandardInput.BaseStream;
            _writer = new Thread(() => WriteLoop(stdin)) { IsBackground = true, Name = "DuckCam video writer" };
            _writer.Start();

            _savedCaptureFramerate = Time.captureFramerate;
            Time.captureFramerate = fps;
            Frames = 0;
            _pendingReadbacks = 0;
            Recording = true;
            if (Audio)
            {
                StartAudio();
            }
            _host = host;
            _routine = host.StartCoroutine(CaptureLoop());
            Status = $"recording {_width}x{_height} @ {fps} fps";
            PhotoLog.Info($"Video recording started: {_videoPath} ({args})");
            return true;
        }

        public void Stop()
        {
            if (!Recording)
            {
                return;
            }
            Recording = false;
            if (_routine != null && _host != null)
            {
                _host.StopCoroutine(_routine);
            }
            _routine = null;
            Time.captureFramerate = _savedCaptureFramerate;
            StopAudio();
            Finishing = true;
            Status = "finishing video...";
            _host.StartCoroutine(FinishRoutine());
        }

        private IEnumerator CaptureLoop()
        {
            var endOfFrame = new WaitForEndOfFrame();
            while (Recording)
            {
                yield return endOfFrame;
                if (!Recording)
                {
                    yield break;
                }
                if (_writeError != null || _ffmpeg == null || _ffmpeg.HasExited)
                {
                    Status = "FFmpeg stopped: " + (_writeError ?? LastFfmpegLine());
                    PhotoLog.Error("Video recording failed: " + Status);
                    Stop();
                    yield break;
                }
                if (_pendingReadbacks > 3)
                {
                    AsyncGPUReadback.WaitAllRequests();
                }
                while (_queue.Count > 6 && _writeError == null && !_ffmpeg.HasExited)
                {
                    Thread.Sleep(2);
                }
                ScreenCapture.CaptureScreenshotIntoRenderTexture(_rt);
                _pendingReadbacks++;
                AsyncGPUReadback.Request(_rt, 0, 0, _width, 0, _height, 0, 1, TextureFormat.RGBA32, OnReadback);
                CaptureAudioFrame();
                Frames++;
            }
        }

        private void OnReadback(AsyncGPUReadbackRequest request)
        {
            _pendingReadbacks--;
            if (request.hasError || _queue == null || _queue.IsAddingCompleted)
            {
                return;
            }
            NativeArray<byte> data = request.GetData<byte>();
            _queue.Add(data.ToArray());
        }

        private void WriteLoop(Stream stdin)
        {
            try
            {
                foreach (byte[] frame in _queue.GetConsumingEnumerable())
                {
                    stdin.Write(frame, 0, frame.Length);
                }
                stdin.Flush();
            }
            catch (Exception ex)
            {
                _writeError = ex.Message;
            }
            finally
            {
                try
                {
                    stdin.Close();
                }
                catch (Exception)
                {
                }
            }
        }

        private IEnumerator FinishRoutine()
        {
            AsyncGPUReadback.WaitAllRequests();
            yield return null;
            _queue.CompleteAdding();
            while (_writer.IsAlive)
            {
                yield return null;
            }
            while (_ffmpeg != null && !_ffmpeg.HasExited)
            {
                yield return null;
            }
            int exit = _ffmpeg?.ExitCode ?? -1;
            _ffmpeg?.Dispose();
            _ffmpeg = null;
            if (_rt != null)
            {
                _rt.Release();
                UnityEngine.Object.Destroy(_rt);
                _rt = null;
            }
            if (exit != 0)
            {
                Status = $"FFmpeg failed (exit {exit}): {LastFfmpegLine()}";
                PhotoLog.Error("Video encode failed: " + _ffmpegLog);
                Finishing = false;
                yield break;
            }

            if (_audioPath != null && File.Exists(_audioPath) && _audioSamples > 0)
            {
                Status = "adding audio...";
                Process mux = null;
                try
                {
                    mux = Process.Start(new ProcessStartInfo
                    {
                        FileName = FindFfmpeg(),
                        Arguments = $"-hide_banner -loglevel error -y -i \"{_videoPath}\" -i \"{_audioPath}\" -c:v copy -c:a aac -b:a 256k -shortest -movflags +faststart \"{_finalPath}\"",
                        UseShellExecute = false,
                        CreateNoWindow = true
                    });
                }
                catch (Exception ex)
                {
                    PhotoLog.Warn("Audio mux could not start: " + ex.Message);
                }
                while (mux != null && !mux.HasExited)
                {
                    yield return null;
                }
                if (mux != null && mux.ExitCode == 0)
                {
                    TryDelete(_videoPath);
                    TryDelete(_audioPath);
                }
                else
                {
                    _finalPath = _videoPath;
                    PhotoLog.Warn("Audio mux failed, video and WAV kept separate.");
                }
                mux?.Dispose();
            }
            else if (_audioPath != null)
            {
                TryDelete(_audioPath);
                if (File.Exists(_videoPath))
                {
                    TryMove(_videoPath, _finalPath);
                }
            }

            Status = $"saved {Path.GetFileName(_finalPath)} ({Seconds:0.0} s, {Frames} frames)";
            PhotoLog.Info("Video saved: " + _finalPath);
            Finishing = false;
        }

        private void StartAudio()
        {
            try
            {
                _audioRate = AudioSettings.outputSampleRate;
                _audioChannels = Channels(AudioSettings.speakerMode);
                _audioSamples = 0;
                _audioStream = new FileStream(_audioPath, FileMode.Create, FileAccess.Write);
                _audioWriter = new BinaryWriter(_audioStream);
                WriteWavHeader(_audioWriter, _audioRate, _audioChannels, 0);
                if (!AudioRenderer.Start())
                {
                    throw new InvalidOperationException("AudioRenderer.Start returned false");
                }
            }
            catch (Exception ex)
            {
                PhotoLog.Warn("Audio capture failed to start: " + ex.Message);
                CloseAudio();
                TryDelete(_audioPath);
                _audioPath = null;
            }
        }

        private void CaptureAudioFrame()
        {
            if (_audioWriter == null)
            {
                return;
            }
            int count = AudioRenderer.GetSampleCountForCaptureFrame();
            if (count <= 0)
            {
                return;
            }
            var samples = new NativeArray<float>(count * _audioChannels, Allocator.Temp);
            try
            {
                if (AudioRenderer.Render(samples))
                {
                    for (int i = 0; i < samples.Length; i++)
                    {
                        _audioWriter.Write(samples[i]);
                    }
                    _audioSamples += count;
                }
            }
            finally
            {
                samples.Dispose();
            }
        }

        private void StopAudio()
        {
            if (_audioWriter == null)
            {
                return;
            }
            try
            {
                AudioRenderer.Stop();
                _audioWriter.Seek(0, SeekOrigin.Begin);
                WriteWavHeader(_audioWriter, _audioRate, _audioChannels, _audioSamples);
            }
            catch (Exception ex)
            {
                PhotoLog.Warn("Audio capture stop failed: " + ex.Message);
            }
            CloseAudio();
        }

        private void CloseAudio()
        {
            try
            {
                _audioWriter?.Flush();
                _audioWriter?.Dispose();
                _audioStream?.Dispose();
            }
            catch (Exception)
            {
            }
            _audioWriter = null;
            _audioStream = null;
        }

        private static void WriteWavHeader(BinaryWriter w, int rate, int channels, long frames)
        {
            int blockAlign = channels * 4;
            long dataBytes = frames * blockAlign;
            w.Write(Encoding.ASCII.GetBytes("RIFF"));
            w.Write((int)Math.Min(int.MaxValue, 36 + dataBytes));
            w.Write(Encoding.ASCII.GetBytes("WAVE"));
            w.Write(Encoding.ASCII.GetBytes("fmt "));
            w.Write(16);
            w.Write((short)3);
            w.Write((short)channels);
            w.Write(rate);
            w.Write(rate * blockAlign);
            w.Write((short)blockAlign);
            w.Write((short)32);
            w.Write(Encoding.ASCII.GetBytes("data"));
            w.Write((int)Math.Min(int.MaxValue, dataBytes));
        }

        private static int Channels(AudioSpeakerMode mode)
        {
            switch (mode)
            {
                case AudioSpeakerMode.Mono: return 1;
                case AudioSpeakerMode.Quad: return 4;
                case AudioSpeakerMode.Surround: return 5;
                case AudioSpeakerMode.Mode5point1: return 6;
                case AudioSpeakerMode.Mode7point1: return 8;
                case AudioSpeakerMode.Prologic: return 2;
                default: return 2;
            }
        }

        public void Abort()
        {
            if (!Recording && !Finishing)
            {
                return;
            }
            if (Recording)
            {
                Recording = false;
                Time.captureFramerate = _savedCaptureFramerate;
                StopAudio();
            }
            try
            {
                AsyncGPUReadback.WaitAllRequests();
                _queue?.CompleteAdding();
                _writer?.Join(5000);
            }
            catch (Exception ex)
            {
                PhotoLog.Warn("Video abort: " + ex.Message);
            }
            if (_rt != null)
            {
                _rt.Release();
                UnityEngine.Object.Destroy(_rt);
                _rt = null;
            }
            _ffmpeg = null;
            Finishing = false;
            Status = "stopped by the raid ending; the video (and audio WAV) were kept as separate files";
            PhotoLog.Warn("Video recording cut short: " + _videoPath);
        }

        private string LastFfmpegLine()
        {
            lock (_ffmpegLog)
            {
                string[] lines = _ffmpegLog.ToString().Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries);
                return lines.Length > 0 ? lines[lines.Length - 1].Trim() : "no output";
            }
        }

        private static void TryDelete(string path)
        {
            try
            {
                if (path != null && File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch (Exception ex)
            {
                PhotoLog.Warn($"Could not delete {path}: {ex.Message}");
            }
        }

        private static void TryMove(string from, string to)
        {
            try
            {
                File.Move(from, to);
            }
            catch (Exception ex)
            {
                PhotoLog.Warn($"Could not rename {from}: {ex.Message}");
            }
        }
    }
}
