using System;
using System.Collections.Generic;
using System.Text;
using HarmonyLib;
using DuckCam.Core;
using UnityEngine;
using UnityEngine.Rendering.PostProcessing;

namespace DuckCam.Rendering
{
    public enum EffectId
    {
        Brightness,
        Saturation,
        Clarity,
        Colourfulness,
        Sharpen,
        AdaptiveSharpen,
        FilterAmount,
        Vignette,
        ChromaticAberration,
        Grain,
        Bloom,
        Exposure,
        Contrast,
        Temperature,
        Tint,
        LiftMaster,
        LiftRed,
        LiftGreen,
        LiftBlue,
        GammaMaster,
        GammaRed,
        GammaGreen,
        GammaBlue,
        GainMaster,
        GainRed,
        GainGreen,
        GainBlue,
        LensDistortion
    }

    internal sealed class PhotoEffects
    {
        public sealed class Param
        {
            public EffectId Id;
            public string Label;
            public string Backend;
            public float Min;
            public float Max;
            public float Neutral;
            public float Step;
            public string Format;
            public Func<float> Get;
            public Action<float> Set;
            public string Section;
        }

        private static readonly AccessTools.FieldRef<GradingPostFX, float> _clarity = Ref<float>("float_0");
        private static readonly AccessTools.FieldRef<GradingPostFX, float> _brightness = Ref<float>("float_1");
        private static readonly AccessTools.FieldRef<GradingPostFX, float> _saturation = Ref<float>("float_2");
        private static readonly AccessTools.FieldRef<GradingPostFX, float> _colourfulness = Ref<float>("float_3");
        private static readonly AccessTools.FieldRef<GradingPostFX, float> _lumaSharpen = Ref<float>("float_4");
        private static readonly AccessTools.FieldRef<GradingPostFX, float> _adaptiveSharpen = Ref<float>("float_5");
        private static readonly AccessTools.FieldRef<GradingPostFX, float> _filterAmount = Ref<float>("float_7");
        private static readonly AccessTools.FieldRef<GradingPostFX, CC_Vintage.Filter> _filter = Ref<CC_Vintage.Filter>("_currentFilter");

        private readonly List<Param> _params = new List<Param>();
        private readonly HashSet<string> _snapshotted = new HashSet<string>();
        private readonly Dictionary<EffectId, float> _values = new Dictionary<EffectId, float>();
        private RestoreStack _stack;

        private GradingPostFX _grading;
        private CC_FastVignette _vignette;
        private ChromaticAberration _chromatic;
        private PrismEffects _prism;
        private PostProcessLayer _ppLayer;
        private PostProcessVolume _ppVolume;
        private ColorGrading _ppGrading;
        private LensDistortion _ppLens;
        private CC_Vintage.Filter _currentFilter = CC_Vintage.Filter.None;

        public IReadOnlyList<Param> Params => _params;
        public bool FilterAvailable => _grading != null && _filter != null;
        public CC_Vintage.Filter Filter => _currentFilter;
        public string Discovery { get; private set; } = "not probed";

        public static readonly string[] FilterNames = Enum.GetNames(typeof(CC_Vintage.Filter));

        public void Probe(UnityEngine.Camera camera, RestoreStack stack)
        {
            _stack = stack;
            _params.Clear();
            _snapshotted.Clear();
            _values.Clear();
            _currentFilter = CC_Vintage.Filter.None;

            _grading = camera.GetComponent<GradingPostFX>();
            _vignette = camera.GetComponent<CC_FastVignette>();
            _chromatic = camera.GetComponent<ChromaticAberration>();
            _prism = camera.GetComponent<PrismEffects>();
            _ppLayer = camera.GetComponent<PostProcessLayer>();

            var sb = new StringBuilder();
            bool gradingOk = _grading != null && _clarity != null && _brightness != null && _saturation != null;
            sb.Append($"GradingPostFX {(_grading == null ? "missing" : (gradingOk ? (_grading.enabled ? "enabled" : "present (PostFX off in settings; enabled on first use)") : "fields not found"))}; ");
            sb.Append($"CC_FastVignette {(_vignette == null ? "missing" : (_vignette.enabled ? "enabled" : "present"))}; ");
            sb.Append($"ChromaticAberration {(_chromatic == null ? "missing" : (_chromatic.enabled ? "enabled" : "present"))}; ");
            sb.Append($"PrismEffects {(_prism == null ? "missing" : (_prism.enabled ? "enabled" : "present"))}; ");
            sb.Append($"PPv2 layer {(_ppLayer == null ? "missing" : (_ppLayer.enabled ? "enabled" : "disabled"))}");
            Discovery = sb.ToString();
            PhotoLog.Info("Effects discovery: " + Discovery);

            if (gradingOk)
            {
                Add(EffectId.Brightness, "Brightness", "PostFX", -100f, 100f, 0f, 2f, "0",
                    () => (_brightness(_grading) - 1f) / 0.25f * 100f,
                    v => { TouchGrading(); _grading.UpdateTechnicolorBrightness(Mathf.InverseLerp(-100f, 100f, v)); });
                Add(EffectId.Saturation, "Saturation", "PostFX", -100f, 100f, 0f, 2f, "0",
                    () => (_saturation(_grading) - 1f) * 100f,
                    v => { TouchGrading(); _grading.UpdateTechnicolorSaturation(Mathf.InverseLerp(-100f, 100f, v)); });
                Add(EffectId.Clarity, "Clarity", "PostFX", 0f, 100f, 0f, 2f, "0",
                    () => _clarity(_grading) * 100f,
                    v => { TouchGrading(); _grading.UpdateClarityStrength(v / 100f); });
                if (_colourfulness != null)
                {
                    Add(EffectId.Colourfulness, "Colorfulness", "PostFX", 0f, 100f, 0f, 2f, "0",
                        () => _colourfulness(_grading) * 100f,
                        v => { TouchGrading(); _grading.UpdateColourfullness(v / 100f); });
                }
                if (_lumaSharpen != null)
                {
                    Add(EffectId.Sharpen, "Sharpen", "PostFX", 0f, 100f, 0f, 2f, "0",
                        () => _lumaSharpen(_grading) * 100f,
                        v => { TouchGrading(); _grading.UpdateLumaSharpenStrength(v / 100f); });
                }
                if (_adaptiveSharpen != null)
                {
                    Add(EffectId.AdaptiveSharpen, "Adaptive Sharpen", "PostFX", 0f, 100f, 0f, 2f, "0",
                        () => _adaptiveSharpen(_grading) * 100f,
                        v => { TouchGrading(); _grading.UpdateAdaptiveSharpenStrength(v / 100f); });
                }
                if (FilterAvailable && _filterAmount != null)
                {
                    _currentFilter = _filter(_grading);
                    Add(EffectId.FilterAmount, "Filter Strength", "PostFX", 0f, 100f, 0f, 5f, "0",
                        () => _filterAmount(_grading) * 100f,
                        v => { TouchGrading(); _grading.UpdateFilterAmount(v / 100f); });
                }
            }

            if (_vignette != null)
            {
                Add(EffectId.Vignette, "Vignette", "CC_FastVignette", 0f, 100f, 0f, 1f, "0",
                    () => _vignette.enabled ? _vignette.darkness : 0f,
                    v => { TouchVignette(); _vignette.darkness = v; _vignette.enabled = v > 0.01f; });
            }
            if (_chromatic != null)
            {
                Add(EffectId.ChromaticAberration, "Chromatic Aberration", "ChromaticAberration", 0f, 100f, 0f, 1f, "0",
                    () => _chromatic.enabled ? _chromatic.Shift / 0.03f * 100f : 0f,
                    v => { TouchChromatic(); _chromatic.Shift = v / 100f * 0.03f; _chromatic.enabled = v > 0.01f; });
            }
            if (_prism != null)
            {
                Add(EffectId.Grain, "Grain", "Prism", 0f, 100f, 0f, 1f, "0",
                    () => _prism.useNoise ? _prism.noiseIntensity / 0.6f * 100f : 0f,
                    v => { TouchPrism(); _prism.useNoise = v > 0.01f; _prism.noiseIntensity = v / 100f * 0.6f; });
                Add(EffectId.Bloom, "Bloom", "Prism", 0f, 100f, 0f, 1f, "0",
                    () => _prism.useBloom ? _prism.bloomIntensity / 0.6f * 100f : 0f,
                    v => { TouchPrism(); _prism.useBloom = v > 0.01f; _prism.bloomIntensity = v / 100f * 0.6f; });
            }
            if (_ppLayer != null && _ppLayer.enabled)
            {
                Add(EffectId.Exposure, "Exposure", "PPv2", -3f, 3f, 0f, 0.05f, "0.00",
                    () => _ppGrading != null ? _ppGrading.postExposure.value : 0f,
                    v => { EnsurePpVolume(); Override(_ppGrading.postExposure, v); });
                Add(EffectId.Contrast, "Contrast", "PPv2", -100f, 100f, 0f, 2f, "0",
                    () => _ppGrading != null ? _ppGrading.contrast.value : 0f,
                    v => { EnsurePpVolume(); Override(_ppGrading.contrast, v); });
                Add(EffectId.Temperature, "Temperature", "PPv2", -100f, 100f, 0f, 2f, "0",
                    () => _ppGrading != null ? _ppGrading.temperature.value : 0f,
                    v => { EnsurePpVolume(); Override(_ppGrading.temperature, v); });
                Add(EffectId.Tint, "Tint", "PPv2", -100f, 100f, 0f, 2f, "0",
                    () => _ppGrading != null ? _ppGrading.tint.value : 0f,
                    v => { EnsurePpVolume(); Override(_ppGrading.tint, v); });

                AddTrackball("Colour curves: shadows (lift)", "Shadows", g => g.lift,
                    EffectId.LiftMaster, EffectId.LiftRed, EffectId.LiftGreen, EffectId.LiftBlue);
                AddTrackball("Colour curves: midtones (gamma)", "Gamma", g => g.gamma,
                    EffectId.GammaMaster, EffectId.GammaRed, EffectId.GammaGreen, EffectId.GammaBlue);
                AddTrackball("Colour curves: highlights (gain)", "Highlights", g => g.gain,
                    EffectId.GainMaster, EffectId.GainRed, EffectId.GainGreen, EffectId.GainBlue);

                Add(EffectId.LensDistortion, "Lens Distortion", "PPv2", -100f, 100f, 0f, 2f, "0",
                    () => _ppLens != null ? _ppLens.intensity.value : 0f,
                    v => { EnsurePpVolume(); Override(_ppLens.intensity, v); _ppLens.enabled.Override(Mathf.Abs(v) > 0.01f); });
                _params[_params.Count - 1].Section = "Lens";
            }
        }

        public float Get(Param p)
        {
            try
            {
                if (_values.TryGetValue(p.Id, out float v))
                {
                    return v;
                }
                if (p.Backend == "PostFX" && _grading != null && !_grading.enabled)
                {
                    return p.Neutral;
                }
                return p.Get();
            }
            catch (Exception)
            {
                return p.Neutral;
            }
        }

        public void Set(Param p, float value)
        {
            value = Mathf.Clamp(value, p.Min, p.Max);
            if (_values.TryGetValue(p.Id, out float current) && Mathf.Approximately(current, value))
            {
                return;
            }
            try
            {
                p.Set(value);
                _values[p.Id] = value;
            }
            catch (Exception ex)
            {
                PhotoLog.WarnOnce("fx-" + p.Id, $"Effect {p.Label} ({p.Backend}) failed: {ex.Message}");
            }
        }

        public Param Find(EffectId id)
        {
            foreach (Param p in _params)
            {
                if (p.Id == id)
                {
                    return p;
                }
            }
            return null;
        }

        public void SetFilter(CC_Vintage.Filter filter)
        {
            if (!FilterAvailable)
            {
                return;
            }
            TouchGrading();
            _grading.SetFilter(filter);
            _currentFilter = filter;
            Param amount = Find(EffectId.FilterAmount);
            if (filter != CC_Vintage.Filter.None && amount != null && Get(amount) < 1f)
            {
                Set(amount, 70f);
            }
        }

        public Dictionary<string, float> SnapshotValues()
        {
            var result = new Dictionary<string, float>();
            foreach (KeyValuePair<EffectId, float> kv in _values)
            {
                result[kv.Key.ToString()] = kv.Value;
            }
            return result;
        }

        public void RestoreValues(Dictionary<string, float> values, string filter)
        {
            ResetAll();
            if (FilterAvailable && !string.IsNullOrEmpty(filter) && Enum.TryParse(filter, out CC_Vintage.Filter f))
            {
                SetFilter(f);
            }
            if (values == null)
            {
                return;
            }
            foreach (KeyValuePair<string, float> kv in values)
            {
                if (Enum.TryParse(kv.Key, out EffectId id))
                {
                    Try(id, kv.Value);
                }
            }
        }

        public void ResetAll()
        {
            foreach (Param p in _params)
            {
                if (_values.ContainsKey(p.Id))
                {
                    Set(p, p.Neutral);
                }
            }
            if (FilterAvailable && _currentFilter != CC_Vintage.Filter.None)
            {
                SetFilter(CC_Vintage.Filter.None);
            }
        }

        private sealed class Look
        {
            public readonly string Name;
            public readonly Action<PhotoEffects> Apply;

            public Look(string name, Action<PhotoEffects> apply)
            {
                Name = name;
                Apply = apply;
            }
        }

        private static readonly Look[] Looks =
        {
            new Look("Clean", fx => { }),
            new Look("Filmic", fx =>
            {
                fx.Try(EffectId.Contrast, 12f); fx.Try(EffectId.Saturation, -10f); fx.Try(EffectId.Vignette, 25f); fx.Try(EffectId.Grain, 15f);
                fx.TryFilter(CC_Vintage.Filter.Cognac, 35f);
            }),
            new Look("Noir", fx =>
            {
                fx.Try(EffectId.Saturation, -100f); fx.Try(EffectId.Contrast, 35f); fx.Try(EffectId.Clarity, 40f); fx.Try(EffectId.Vignette, 45f); fx.Try(EffectId.Grain, 30f);
            }),
            new Look("Warm", fx =>
            {
                fx.Try(EffectId.Temperature, 25f); fx.Try(EffectId.Saturation, 8f);
                fx.TryFilter(CC_Vintage.Filter.Fahrenheit, 40f);
            }),
            new Look("Cold", fx =>
            {
                fx.Try(EffectId.Temperature, -25f); fx.Try(EffectId.Saturation, -12f);
                fx.TryFilter(CC_Vintage.Filter.Chillwave, 40f);
            }),
            new Look("Military", fx =>
            {
                fx.Try(EffectId.Saturation, -30f); fx.Try(EffectId.Contrast, 15f); fx.Try(EffectId.Clarity, 30f); fx.Try(EffectId.Tint, -8f);
                fx.TryFilter(CC_Vintage.Filter.Olive, 45f);
            }),
            new Look("Vintage", fx =>
            {
                fx.Try(EffectId.Saturation, -20f); fx.Try(EffectId.Vignette, 40f); fx.Try(EffectId.Grain, 35f);
                fx.TryFilter(CC_Vintage.Filter.Zabid, 60f);
            }),
            new Look("Bleach", fx =>
            {
                fx.Try(EffectId.Saturation, -45f); fx.Try(EffectId.Contrast, 30f); fx.Try(EffectId.Brightness, 6f); fx.Try(EffectId.Sharpen, 30f);
            }),
            new Look("Sepia", fx =>
            {
                fx.Try(EffectId.Saturation, -100f); fx.Try(EffectId.Contrast, 10f); fx.Try(EffectId.Vignette, 30f); fx.Try(EffectId.Grain, 20f);
                fx.Try(EffectId.GainRed, 0.35f); fx.Try(EffectId.GainBlue, -0.35f); fx.Try(EffectId.LiftRed, 0.2f); fx.Try(EffectId.LiftBlue, -0.25f);
                fx.WarmFallback(40f);
            }),
            new Look("High Contrast", fx =>
            {
                fx.Try(EffectId.Contrast, 50f); fx.Try(EffectId.Clarity, 45f); fx.Try(EffectId.Saturation, 10f); fx.Try(EffectId.Sharpen, 25f);
                fx.Try(EffectId.LiftMaster, -0.15f); fx.Try(EffectId.GainMaster, 0.1f);
            }),
            new Look("Night", fx =>
            {
                fx.Try(EffectId.Exposure, -1.2f); fx.Try(EffectId.Temperature, -35f); fx.Try(EffectId.Tint, -10f); fx.Try(EffectId.Saturation, -35f);
                fx.Try(EffectId.Grain, 40f); fx.Try(EffectId.Vignette, 50f); fx.Try(EffectId.LiftBlue, 0.2f);
            }),
            new Look("Cinematic", fx =>
            {
                fx.Try(EffectId.Contrast, 20f); fx.Try(EffectId.Saturation, -5f); fx.Try(EffectId.Vignette, 30f);
                fx.Try(EffectId.LiftBlue, 0.3f); fx.Try(EffectId.LiftGreen, 0.15f); fx.Try(EffectId.LiftRed, -0.2f);
                fx.Try(EffectId.GainRed, 0.25f); fx.Try(EffectId.GainGreen, 0.08f); fx.Try(EffectId.GainBlue, -0.2f);
            }),
            new Look("Desaturated", fx =>
            {
                fx.Try(EffectId.Saturation, -60f); fx.Try(EffectId.Contrast, 8f); fx.Try(EffectId.Clarity, 15f);
            }),
            new Look("Golden Hour", fx =>
            {
                fx.Try(EffectId.Temperature, 35f); fx.Try(EffectId.Tint, 6f); fx.Try(EffectId.Saturation, 12f); fx.Try(EffectId.Bloom, 25f); fx.Try(EffectId.Vignette, 20f);
                fx.Try(EffectId.GainRed, 0.2f); fx.Try(EffectId.GainGreen, 0.08f); fx.Try(EffectId.GainBlue, -0.15f);
                fx.TryFilter(CC_Vintage.Filter.Hotshot, 25f);
            }),
            new Look("Sunset", fx =>
            {
                fx.Try(EffectId.Temperature, 50f); fx.Try(EffectId.Tint, 15f); fx.Try(EffectId.Saturation, 20f); fx.Try(EffectId.Contrast, 15f); fx.Try(EffectId.Vignette, 35f);
                fx.Try(EffectId.LiftBlue, 0.15f); fx.Try(EffectId.LiftRed, 0.1f); fx.Try(EffectId.GainRed, 0.3f); fx.Try(EffectId.GainBlue, -0.25f);
            }),
            new Look("Bleak Winter", fx =>
            {
                fx.Try(EffectId.Temperature, -40f); fx.Try(EffectId.Saturation, -45f); fx.Try(EffectId.Brightness, 8f); fx.Try(EffectId.Contrast, -10f);
                fx.Try(EffectId.LiftMaster, 0.08f); fx.Try(EffectId.LiftBlue, 0.15f);
                fx.TryFilter(CC_Vintage.Filter.Feather, 30f);
            }),
            new Look("Moonlight", fx =>
            {
                fx.Try(EffectId.Exposure, -0.8f); fx.Try(EffectId.Temperature, -60f); fx.Try(EffectId.Saturation, -55f); fx.Try(EffectId.Contrast, 15f);
                fx.Try(EffectId.Vignette, 45f); fx.Try(EffectId.LiftBlue, 0.25f); fx.Try(EffectId.GainBlue, 0.1f);
            }),
            new Look("Overcast", fx =>
            {
                fx.Try(EffectId.Temperature, -12f); fx.Try(EffectId.Saturation, -30f); fx.Try(EffectId.Contrast, -12f); fx.Try(EffectId.Brightness, 4f);
                fx.Try(EffectId.LiftMaster, 0.06f);
            }),
            new Look("Desert Dust", fx =>
            {
                fx.Try(EffectId.Temperature, 30f); fx.Try(EffectId.Saturation, -25f); fx.Try(EffectId.Contrast, -8f); fx.Try(EffectId.Brightness, 5f);
                fx.Try(EffectId.LiftMaster, 0.1f); fx.Try(EffectId.LiftRed, 0.12f); fx.Try(EffectId.LiftGreen, 0.06f);
                fx.TryFilter(CC_Vintage.Filter.Bread, 35f);
            }),
            new Look("Toxic", fx =>
            {
                fx.Try(EffectId.Tint, -35f); fx.Try(EffectId.Saturation, -20f); fx.Try(EffectId.Contrast, 20f); fx.Try(EffectId.Clarity, 25f); fx.Try(EffectId.Vignette, 40f); fx.Try(EffectId.Grain, 20f);
                fx.Try(EffectId.GammaGreen, 0.15f); fx.Try(EffectId.LiftGreen, 0.15f);
            }),
            new Look("Horror", fx =>
            {
                fx.Try(EffectId.Exposure, -0.6f); fx.Try(EffectId.Saturation, -60f); fx.Try(EffectId.Contrast, 35f); fx.Try(EffectId.Tint, -12f);
                fx.Try(EffectId.Vignette, 65f); fx.Try(EffectId.Grain, 45f); fx.Try(EffectId.ChromaticAberration, 20f); fx.Try(EffectId.LiftMaster, -0.1f);
            }),
            new Look("Blood Red", fx =>
            {
                fx.Try(EffectId.Saturation, -40f); fx.Try(EffectId.Contrast, 30f); fx.Try(EffectId.Vignette, 50f);
                fx.Try(EffectId.GainRed, 0.35f); fx.Try(EffectId.GammaRed, 0.15f); fx.Try(EffectId.LiftRed, 0.1f); fx.Try(EffectId.GainGreen, -0.1f);
            }),
            new Look("Vivid", fx =>
            {
                fx.Try(EffectId.Saturation, 30f); fx.Try(EffectId.Colourfulness, 40f); fx.Try(EffectId.Contrast, 15f); fx.Try(EffectId.Clarity, 20f); fx.Try(EffectId.Sharpen, 20f);
            }),
            new Look("Comic", fx =>
            {
                fx.Try(EffectId.Saturation, 45f); fx.Try(EffectId.Contrast, 45f); fx.Try(EffectId.Clarity, 60f); fx.Try(EffectId.Sharpen, 50f); fx.Try(EffectId.AdaptiveSharpen, 40f);
                fx.Try(EffectId.LiftMaster, -0.12f);
            }),
            new Look("Gritty", fx =>
            {
                fx.Try(EffectId.Saturation, -35f); fx.Try(EffectId.Contrast, 30f); fx.Try(EffectId.Clarity, 70f); fx.Try(EffectId.Sharpen, 40f); fx.Try(EffectId.Grain, 35f); fx.Try(EffectId.Vignette, 35f);
            }),
            new Look("Faded Film", fx =>
            {
                fx.Try(EffectId.Contrast, -20f); fx.Try(EffectId.Saturation, -25f); fx.Try(EffectId.Grain, 30f); fx.Try(EffectId.Vignette, 25f);
                fx.Try(EffectId.LiftMaster, 0.15f); fx.Try(EffectId.LiftBlue, 0.08f); fx.Try(EffectId.GainMaster, -0.08f);
                fx.TryFilter(CC_Vintage.Filter.Doze, 35f);
            }),
            new Look("Old Photo", fx =>
            {
                fx.Try(EffectId.Saturation, -85f); fx.Try(EffectId.Contrast, -5f); fx.Try(EffectId.Vignette, 60f); fx.Try(EffectId.Grain, 55f);
                fx.Try(EffectId.LiftMaster, 0.12f); fx.Try(EffectId.LiftRed, 0.15f); fx.Try(EffectId.GainBlue, -0.2f);
                fx.WarmFallback(30f);
            }),
            new Look("Lomo", fx =>
            {
                fx.Try(EffectId.Saturation, 25f); fx.Try(EffectId.Contrast, 30f); fx.Try(EffectId.Vignette, 70f); fx.Try(EffectId.ChromaticAberration, 30f); fx.Try(EffectId.LensDistortion, 15f);
                fx.TryFilter(CC_Vintage.Filter.Jason, 40f);
            }),
            new Look("Retro 80s", fx =>
            {
                fx.Try(EffectId.Saturation, 15f); fx.Try(EffectId.Tint, 20f); fx.Try(EffectId.Bloom, 35f); fx.Try(EffectId.Grain, 25f); fx.Try(EffectId.ChromaticAberration, 25f);
                fx.Try(EffectId.LiftBlue, 0.2f); fx.Try(EffectId.LiftRed, 0.1f);
                fx.TryFilter(CC_Vintage.Filter.Tokyo, 45f);
            }),
            new Look("Dreamy", fx =>
            {
                fx.Try(EffectId.Bloom, 60f); fx.Try(EffectId.Contrast, -15f); fx.Try(EffectId.Brightness, 10f); fx.Try(EffectId.Saturation, -10f); fx.Try(EffectId.Vignette, 20f);
                fx.Try(EffectId.LiftMaster, 0.08f);
            }),
            new Look("Night Vision", fx =>
            {
                fx.Try(EffectId.Saturation, -100f); fx.Try(EffectId.Exposure, 0.6f); fx.Try(EffectId.Contrast, 25f); fx.Try(EffectId.Grain, 70f); fx.Try(EffectId.Vignette, 75f);
                fx.Try(EffectId.GainGreen, 0.45f); fx.Try(EffectId.GammaGreen, 0.25f); fx.Try(EffectId.GainRed, -0.3f); fx.Try(EffectId.GainBlue, -0.3f);
            }),
            new Look("Thermal", fx =>
            {
                fx.Try(EffectId.Saturation, -100f); fx.Try(EffectId.Contrast, 70f); fx.Try(EffectId.Brightness, 12f); fx.Try(EffectId.Clarity, 50f); fx.Try(EffectId.Grain, 20f);
                fx.Try(EffectId.LiftMaster, -0.25f); fx.Try(EffectId.GainMaster, 0.2f);
            }),
        };

        public static readonly string[] PresetNames = Array.ConvertAll(Looks, l => l.Name);

        public void ApplyPreset(int index)
        {
            ResetAll();
            if (index >= 0 && index < Looks.Length)
            {
                Looks[index].Apply(this);
            }
        }

        private void WarmFallback(float temperature)
        {
            if (Find(EffectId.GainRed) == null)
            {
                Try(EffectId.Temperature, temperature);
            }
        }

        private void Try(EffectId id, float value)
        {
            Param p = Find(id);
            if (p != null)
            {
                Set(p, value);
            }
        }

        private void TryFilter(CC_Vintage.Filter filter, float amount)
        {
            if (FilterAvailable)
            {
                SetFilter(filter);
                Try(EffectId.FilterAmount, amount);
            }
        }

        private void TouchGrading()
        {
            if (!_snapshotted.Add("grading"))
            {
                return;
            }
            GradingPostFX g = _grading;
            bool enabled = g.enabled;
            float clarity = _clarity(g);
            float brightness = _brightness(g);
            float saturation = _saturation(g);
            float colourfulness = _colourfulness != null ? _colourfulness(g) : 0f;
            float luma = _lumaSharpen != null ? _lumaSharpen(g) : 0f;
            float adaptive = _adaptiveSharpen != null ? _adaptiveSharpen(g) : 0f;
            float filterAmount = _filterAmount != null ? _filterAmount(g) : 0f;
            CC_Vintage.Filter filter = _filter != null ? _filter(g) : CC_Vintage.Filter.None;

            _stack.Push("effects: PostFX grading", () =>
            {
                if (g == null)
                {
                    return;
                }
                g.UpdateClarityStrength(clarity);
                g.UpdateTechnicolorBrightness(Mathf.InverseLerp(0.75f, 1.25f, brightness));
                g.UpdateTechnicolorSaturation(Mathf.InverseLerp(0.5f, 1.5f, saturation));
                g.UpdateColourfullness(colourfulness);
                g.UpdateLumaSharpenStrength(luma);
                g.UpdateAdaptiveSharpenStrength(adaptive);
                if (_filter != null)
                {
                    g.SetFilter(filter);
                }
                g.UpdateFilterAmount(filterAmount);
                g.enabled = enabled;
            });

            if (!enabled)
            {
                g.UpdateClarityStrength(0f);
                g.UpdateTechnicolorBrightness(0.5f);
                g.UpdateTechnicolorSaturation(0.5f);
                g.UpdateColourfullness(0f);
                g.UpdateLumaSharpenStrength(0f);
                g.UpdateAdaptiveSharpenStrength(0f);
                g.SetFilter(CC_Vintage.Filter.None);
                g.UpdateFilterAmount(0f);
                g.enabled = true;
            }
        }

        private void TouchVignette()
        {
            if (!_snapshotted.Add("vignette"))
            {
                return;
            }
            CC_FastVignette v = _vignette;
            bool enabled = v.enabled;
            float darkness = v.darkness;
            float sharpness = v.sharpness;
            Vector2 center = v.center;
            _stack.Push("effects: vignette", () =>
            {
                if (v != null)
                {
                    v.darkness = darkness;
                    v.sharpness = sharpness;
                    v.center = center;
                    v.enabled = enabled;
                }
            });
        }

        private void TouchChromatic()
        {
            if (!_snapshotted.Add("chromatic"))
            {
                return;
            }
            ChromaticAberration c = _chromatic;
            bool enabled = c.enabled;
            float shift = c.Shift;
            _stack.Push("effects: chromatic aberration", () =>
            {
                if (c != null)
                {
                    c.Shift = shift;
                    c.enabled = enabled;
                }
            });
        }

        private void TouchPrism()
        {
            if (!_snapshotted.Add("prism"))
            {
                return;
            }
            PrismEffects p = _prism;
            bool useNoise = p.useNoise;
            float noise = p.noiseIntensity;
            bool useBloom = p.useBloom;
            float bloom = p.bloomIntensity;
            _stack.Push("effects: prism", () =>
            {
                if (p != null)
                {
                    p.useNoise = useNoise;
                    p.noiseIntensity = noise;
                    p.useBloom = useBloom;
                    p.bloomIntensity = bloom;
                }
            });
        }

        private void EnsurePpVolume()
        {
            if (_ppVolume != null)
            {
                return;
            }
            int layer = FirstLayer(_ppLayer.volumeLayer);
            var go = new GameObject("DuckCam_PostProcessOverride") { layer = layer };
            _ppVolume = go.AddComponent<PostProcessVolume>();
            _ppVolume.isGlobal = true;
            _ppVolume.priority = 10000f;
            _ppVolume.weight = 1f;
            PostProcessProfile profile = ScriptableObject.CreateInstance<PostProcessProfile>();
            _ppGrading = profile.AddSettings<ColorGrading>();
            _ppLens = profile.AddSettings<LensDistortion>();
            _ppLens.enabled.Override(false);
            _ppGrading.enabled.Override(true);
            _ppVolume.sharedProfile = profile;
            GameObject volumeObject = go;
            PostProcessLayer ppLayer = _ppLayer;
            ColorGrading liveGrading = ppLayer.GetSettings<ColorGrading>();
            LensDistortion liveLens = ppLayer.GetSettings<LensDistortion>();
            var gradingBefore = liveGrading != null
                ? new
                {
                    Enabled = liveGrading.enabled.value,
                    Exposure = liveGrading.postExposure.value,
                    Contrast = liveGrading.contrast.value,
                    Temperature = liveGrading.temperature.value,
                    Tint = liveGrading.tint.value,
                    Lift = liveGrading.lift.value,
                    Gamma = liveGrading.gamma.value,
                    Gain = liveGrading.gain.value
                }
                : null;
            bool lensEnabledBefore = liveLens != null && liveLens.enabled.value;
            float lensIntensityBefore = liveLens != null ? liveLens.intensity.value : 0f;
            _stack.Push("effects: PPv2 override volume", () =>
            {
                if (volumeObject != null)
                {
                    volumeObject.SetActive(false);
                    UnityEngine.Object.Destroy(volumeObject);
                }
                if (liveGrading != null && gradingBefore != null)
                {
                    liveGrading.enabled.value = gradingBefore.Enabled;
                    liveGrading.postExposure.value = gradingBefore.Exposure;
                    liveGrading.contrast.value = gradingBefore.Contrast;
                    liveGrading.temperature.value = gradingBefore.Temperature;
                    liveGrading.tint.value = gradingBefore.Tint;
                    liveGrading.lift.value = gradingBefore.Lift;
                    liveGrading.gamma.value = gradingBefore.Gamma;
                    liveGrading.gain.value = gradingBefore.Gain;
                }
                if (liveLens != null)
                {
                    liveLens.enabled.value = lensEnabledBefore;
                    liveLens.intensity.value = lensIntensityBefore;
                }
                if (profile != null)
                {
                    UnityEngine.Object.Destroy(profile);
                }
                _ppVolume = null;
                _ppGrading = null;
                _ppLens = null;
            });
        }

        private static void Override(FloatParameter parameter, float value)
        {
            parameter.Override(value);
        }

        private static int FirstLayer(LayerMask mask)
        {
            int bits = mask.value;
            for (int i = 0; i < 32; i++)
            {
                if ((bits & (1 << i)) != 0)
                {
                    return i;
                }
            }
            return 0;
        }

        private void AddTrackball(string section, string name, Func<ColorGrading, Vector4Parameter> select,
            EffectId master, EffectId red, EffectId green, EffectId blue)
        {
            EffectId[] ids = { red, green, blue, master };
            string[] labels = { name + " Red", name + " Green", name + " Blue", name };
            foreach (int component in new[] { 3, 0, 1, 2 })
            {
                int c = component;
                Add(ids[c], labels[c], "PPv2", -1f, 1f, 0f, 0.02f, "0.00",
                    () => _ppGrading != null && select(_ppGrading).overrideState ? select(_ppGrading).value[c] : 0f,
                    v =>
                    {
                        EnsurePpVolume();
                        Vector4Parameter p = select(_ppGrading);
                        Vector4 value = p.overrideState ? p.value : Vector4.zero;
                        value[c] = v;
                        p.Override(value);
                    });
            }
            _params[_params.Count - 4].Section = section;
        }

        private void Add(EffectId id, string label, string backend, float min, float max, float neutral, float step, string format, Func<float> get, Action<float> set)
        {
            _params.Add(new Param { Id = id, Label = label, Backend = backend, Min = min, Max = max, Neutral = neutral, Step = step, Format = format, Get = get, Set = set });
        }

        private static AccessTools.FieldRef<GradingPostFX, T> Ref<T>(string field)
        {
            try
            {
                return AccessTools.FieldRefAccess<GradingPostFX, T>(field);
            }
            catch (Exception)
            {
                return null;
            }
        }
    }
}
