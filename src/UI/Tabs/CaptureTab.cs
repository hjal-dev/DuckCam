using System;
using DuckCam.Core;
using DuckCam.Rendering;
using UnityEngine;

namespace DuckCam.UI
{
    internal sealed partial class DuckCamUI
    {
        private static readonly string[] SupersampleNames = { "Off", "2x", "4x" };

        private void DrawCapture()
        {
            ScreenshotController shots = _c.Screenshots;
            if (_kit.Action("Take Screenshot", shots.Capturing ? "Capturing" : KeyName(DuckCamConfig.ScreenshotKey)))
            {
                _c.TakeScreenshot();
            }
            DuckCamConfig.WriteSidecar.Value = _kit.Toggle("JSON Sidecar", DuckCamConfig.WriteSidecar.Value);
            if (_kit.Action("Gallery", KeyName(DuckCamConfig.GalleryKey)))
            {
                Gallery.Show();
            }

            _kit.Section("Resolution · Transparency");
            DuckCamConfig.PhotoResolution.Value = _kit.Enum("Photo Resolution", DuckCamConfig.PhotoResolution.Value, HiResRenderer.ResolutionNames);
            int ss = DuckCamConfig.PhotoSupersample.Value;
            int ssIndex = _kit.Selector("Anti-Alias Supersample", SupersampleNames, ss >= 4 ? 2 : ss >= 2 ? 1 : 0);
            DuckCamConfig.PhotoSupersample.Value = ssIndex == 2 ? 4 : ssIndex == 1 ? 2 : 1;
            DuckCamConfig.TransparentBackground.Value = _kit.Toggle("Transparent Background", DuckCamConfig.TransparentBackground.Value);
            HiResRenderer.Request request = ScreenshotController.CurrentRequest();
            if (request.Transparent)
            {
                DuckCamConfig.CutoutSubjects.Value = _kit.Enum("Keep", DuckCamConfig.CutoutSubjects.Value, new[] { "All Characters", "Photo Characters Only" });
                DuckCamConfig.CutoutKeepScene.Value = _kit.Toggle("Keep Scene Shadows", DuckCamConfig.CutoutKeepScene.Value);
            }
            Vector2Int outSize = HiResRenderer.OutputSize(request.Resolution);
            bool plain = request.Resolution == CaptureResolution.Screen && request.Supersample <= 1 && !request.Transparent;
            if (plain)
            {
                _kit.Value("Output", $"{outSize.x} x {outSize.y} (screen)");
            }
            else
            {
                int k = HiResRenderer.EffectiveSupersample(request);
                int tiles = HiResRenderer.TileCount(outSize.x * k);
                _kit.Value("Output", $"{outSize.x} x {outSize.y}" + (k > 1 ? $", rendered at {k}x" : "") + (tiles > 1 ? $", {tiles * tiles} tiles" : ""));
                _kit.Note(tiles > 1
                    ? $"The photo is built from {tiles * tiles} zoomed-in screen captures, so the screen steps through them for a few seconds. Keep the scene frozen. Vignette, grain and chromatic aberration are off for tiled photos (they would repeat in every tile)."
                    : "The screen is captured and scaled to this size.");
                if (_c.Decor.Active && request.Transparent)
                {
                    _kit.Note("Text, border and mask decorations are left out of transparent photos.");
                }
            }
            if (request.Transparent)
            {
                _kit.Note(request.KeepScene
                    ? "Characters keep the scene's shadows and reflections. Anything in front of them shows through the cutout, so give them a clear view."
                    : "Everything except the characters is hidden for the photo, so nothing blocks them, but shadows cast by the scene disappear too. Frames and stickers are left out of transparent photos.");
            }
            _kit.Value("Last Photo", string.IsNullOrEmpty(shots.LastPath) ? "None" : System.IO.Path.GetFileName(shots.LastPath));
            if (!string.IsNullOrEmpty(shots.LastError))
            {
                _kit.Note("Error: " + shots.LastError);
            }
            _kit.Note("Saved to " + ScreenshotController.Directory + ". The UI, grid and path visuals are hidden for the captured frame.");

            _kit.Section("Video · FFmpeg");
            VideoRecorder video = _c.Video;
            string ffmpeg = VideoRecorder.FindFfmpeg();
            _kit.Value("FFmpeg", ffmpeg == null ? "Not found" : System.IO.Path.GetFileName(System.IO.Path.GetDirectoryName(ffmpeg)) + "\\ffmpeg.exe");
            bool busy = video.Recording || video.Finishing;
            if (_kit.Action("Record Video", video.Recording ? "Stop" : (video.Finishing ? "Finishing" : KeyName(DuckCamConfig.VideoRecordKey))))
            {
                if (!video.Finishing)
                {
                    _c.ToggleVideo();
                }
            }
            if (!busy)
            {
                video.FrameRateIndex = _kit.Selector("Frame Rate", Array.ConvertAll(VideoRecorder.FrameRates, f => f + " fps"), video.FrameRateIndex);
                video.ResolutionIndex = _kit.Selector("Resolution", VideoRecorder.Resolutions, video.ResolutionIndex);
                video.Quality = 51 - _kit.IntSlider("Quality", 51 - video.Quality, 18, 51);
                video.SpeedIndex = _kit.Selector("Encoding", VideoRecorder.Speeds, video.SpeedIndex);
                video.Audio = _kit.Toggle("Record Game Audio", video.Audio);
                video.FlipVertical = _kit.Toggle("Flip Vertically", video.FlipVertical);
            }
            _kit.Value("Video", video.Status);
            _kit.Note("Press " + KeyName(DuckCamConfig.VideoRecordKey) + " to start and stop (the UI hides while recording). The game clock is locked to the video frame rate, so the video is always smooth, including above the game's own FPS: the game just runs slower while it records. "
                      + "Play a camera path or fly by hand. Game audio is recorded in sync, but the speakers are silent while recording. "
                      + (ffmpeg == null ? "FFmpeg isn't included: download ffmpeg.exe (for example a Windows build from gyan.dev) and put it in " + System.IO.Path.GetDirectoryName(VideoRecorder.PluginFfmpeg) + ". " : "")
                      + "Videos: " + VideoRecorder.Directory + ". If the video comes out upside down, turn off Flip Vertically.");
        }
    }
}
