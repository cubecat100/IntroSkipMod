using System.Reflection;
using System.Collections;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Assets;
using MegaCrit.Sts2.Core.Bindings.MegaSpine;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Modding;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Screens.MainMenu;
using OpCodes = System.Reflection.Emit.OpCodes;

namespace IntroSkip;

[ModInitializer(nameof(Initialize))]
public static class MainFile
{
    public const string ModId = "IntroSkip";

    public static MegaCrit.Sts2.Core.Logging.Logger Logger { get; } = new(ModId, LogType.Generic);

    public static void Initialize()
    {
        new Harmony(ModId).PatchAll(Assembly.GetExecutingAssembly());
        Logger.Info("IntroSkip initialized.");
    }
}

[HarmonyPatch(typeof(NGame), "LaunchMainMenu")]
internal static class LaunchMainMenuPatch
{
    private static bool Prefix(NGame __instance, ref Task __result)
    {
        __result = LaunchMainMenuWithAnimatedLoading(__instance);
        return false;
    }

    private static async Task LaunchMainMenuWithAnimatedLoading(NGame game)
    {
        Stopwatch stopwatch = Stopwatch.StartNew();
        LoadingProgressTracker.Reset();
        MainFile.Logger.Info("Startup loading started.");
        _ = RunIntroSequence(game, stopwatch);
        Task mainMenuLoadTask = PreloadManager.LoadMainMenuEssentials();

        await mainMenuLoadTask;
        MainFile.Logger.Info($"Main menu essentials loaded in {stopwatch.Elapsed.TotalSeconds:F2}s.");
        await LoadMainMenu(game);
        MainFile.Logger.Info($"Main menu loaded in {stopwatch.Elapsed.TotalSeconds:F2}s.");
        _ = LoadDeferredStartupAssetsAsync(game);
        MainFile.Logger.Info($"Startup loading flow finished in {stopwatch.Elapsed.TotalSeconds:F2}s.");
    }

    private static async Task RunIntroSequence(NGame game, Stopwatch stopwatch)
    {
        CanvasLayer? layer = null;
        using CancellationTokenSource progressCancelToken = new();

        try
        {
            await PreloadManager.LoadLogoAnimation();

            layer = new CanvasLayer
            {
                Name = "IntroSkipIntroLayer",
                Layer = 90
            };

            NLogoAnimation logoAnimation = NLogoAnimation.Create();
            layer.AddChild(logoAnimation);
            game.AddChild(layer);

            LoadingProgressOverlay progressOverlay = LoadingProgressOverlay.Create(logoAnimation);
            Task progressTask = progressOverlay.RunAsync(progressCancelToken.Token);

            await FadeLayer(logoAnimation, 0f, 1f, 0.2f);
            MainFile.Logger.Info($"Logo fade-in finished at {stopwatch.Elapsed.TotalSeconds:F2}s.");
            Task animationTask = logoAnimation.PlayAnimation(progressCancelToken.Token);
            MainFile.Logger.Info($"Logo animation started at {stopwatch.Elapsed.TotalSeconds:F2}s.");
            Task completedTask = await Task.WhenAny(animationTask, progressTask);
            MainFile.Logger.Info(completedTask == animationTask
                ? $"Logo animation completed at {stopwatch.Elapsed.TotalSeconds:F2}s."
                : $"Logo animation skipped after loading progress completed at {stopwatch.Elapsed.TotalSeconds:F2}s.");

            progressOverlay.SetProgress(1f);
            MainFile.Logger.Info($"Intro fade-out started at {stopwatch.Elapsed.TotalSeconds:F2}s.");
            await Task.WhenAll(
                FadeLayer(logoAnimation, 1f, 0f, 0.4f),
                progressOverlay.FadeOutAsync(0.4f));
            MainFile.Logger.Info($"Intro fade-out finished at {stopwatch.Elapsed.TotalSeconds:F2}s.");
            await progressCancelToken.CancelAsync();
            _ = IgnoreCancellation(animationTask);
            _ = IgnoreCancellation(progressTask);
        }
        catch (Exception ex)
        {
            MainFile.Logger.Error($"Intro overlay failed: {ex}");
        }
        finally
        {
            if (!progressCancelToken.IsCancellationRequested)
                await progressCancelToken.CancelAsync();

            layer?.QueueFree();
        }
    }

    private static async Task IgnoreCancellation(Task task)
    {
        try
        {
            await task;
        }
        catch (OperationCanceledException)
        {
        }
    }

    private static Task LoadMainMenu(NGame game)
    {
        MethodInfo method = AccessTools.Method(typeof(NGame), "LoadMainMenu", [typeof(bool)]);
        return (Task)method.Invoke(game, [false])!;
    }

    private static Task LoadDeferredStartupAssetsAsync(NGame game)
    {
        MethodInfo method = AccessTools.Method(typeof(NGame), "LoadDeferredStartupAssetsAsync");
        return (Task)method.Invoke(game, [])!;
    }

    private static async Task FadeLayer(Control control, float fromAlpha, float toAlpha, float duration)
    {
        control.Modulate = new Color(1f, 1f, 1f, fromAlpha);
        ulong startTicks = Time.GetTicksMsec();
        ulong durationMs = (ulong)(duration * 1000f);

        while (true)
        {
            ulong elapsed = Time.GetTicksMsec() - startTicks;
            float t = durationMs == 0 ? 1f : Mathf.Clamp((float)elapsed / durationMs, 0f, 1f);
            control.Modulate = new Color(1f, 1f, 1f, Mathf.Lerp(fromAlpha, toAlpha, t));

            if (t >= 1f)
                return;

            await Task.Delay(16);
        }
    }
}

[HarmonyPatch(typeof(PreloadManager), "LoadAssets")]
internal static class TrackAssetLoadingSessionPatch
{
    private static void Postfix(AssetLoadingSession __result)
    {
        LoadingProgressTracker.Track(__result);
    }
}

[HarmonyPatch]
internal static class LogoAnimationStartupDelayPatch
{
    private const double StartupDelaySeconds = 0.0;
    private const float AnimationTimeScale = 8.0f;

    private static MethodBase TargetMethod()
    {
        Type? stateMachineType = Array.Find(
            typeof(NLogoAnimation).GetNestedTypes(BindingFlags.NonPublic),
            type => type.Name.Contains("<PlayAnimation>"));

        return AccessTools.Method(stateMachineType, "MoveNext");
    }

    private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        MethodInfo tweenIntervalMethod = AccessTools.Method(typeof(Tween), "TweenInterval", [typeof(double)]);
        MethodInfo setAnimationMethod = AccessTools.Method(typeof(MegaAnimationState), "SetAnimation", [typeof(string), typeof(bool), typeof(int)]);
        MethodInfo setTimeScaleMethod = AccessTools.Method(typeof(MegaTrackEntry), "SetTimeScale", [typeof(float)]);
        List<CodeInstruction> codes = [.. instructions];
        bool startupDelayReplaced = false;
        bool timeScaleInjected = false;

        for (int i = 1; i < codes.Count; i++)
        {
            if (startupDelayReplaced == false
                && codes[i].Calls(tweenIntervalMethod)
                && codes[i - 1].opcode == OpCodes.Ldc_R8
                && codes[i - 1].operand is double value
                && value == 1d)
            {
                codes[i - 1].operand = StartupDelaySeconds;
                startupDelayReplaced = true;
            }

            if (timeScaleInjected == false && codes[i].Calls(setAnimationMethod))
            {
                codes.InsertRange(i + 1,
                [
                    new CodeInstruction(OpCodes.Dup),
                    new CodeInstruction(OpCodes.Ldc_R4, AnimationTimeScale),
                    new CodeInstruction(OpCodes.Callvirt, setTimeScaleMethod)
                ]);
                timeScaleInjected = true;
                i += 3;
            }
        }

        return codes;
    }
}

internal static class LoadingProgressTracker
{
    private static readonly List<AssetLoadingSession> Sessions = [];
    private static readonly object Lock = new();
    private static readonly FieldInfo TotalLoadedField = AccessTools.Field(typeof(AssetLoadingSession), "_totalLoaded");
    private static readonly FieldInfo ToLoadField = AccessTools.Field(typeof(AssetLoadingSession), "_toLoad");
    private static readonly FieldInfo LoadingField = AccessTools.Field(typeof(AssetLoadingSession), "_loading");
    private static readonly FieldInfo FinalizingField = AccessTools.Field(typeof(AssetLoadingSession), "_finalizing");
    private static readonly FieldInfo VfxScenesField = AccessTools.Field(typeof(AssetLoadingSession), "_vfxScenes");
    private static readonly FieldInfo VfxLoadingField = AccessTools.Field(typeof(AssetLoadingSession), "_vfxLoading");

    public static void Track(AssetLoadingSession session)
    {
        lock (Lock)
        {
            Sessions.Add(session);
        }
    }

    public static void Reset()
    {
        lock (Lock)
        {
            Sessions.Clear();
        }
    }

    public static float Progress
    {
        get
        {
            int loaded = 0;
            int total = 0;

            AssetLoadingSession[] sessions;
            lock (Lock)
            {
                sessions = Sessions.ToArray();
            }

            foreach (AssetLoadingSession session in sessions)
            {
                int sessionLoaded = ReadInt(TotalLoadedField, session);
                int remaining = CountQueue(ToLoadField, session)
                    + CountQueue(LoadingField, session)
                    + CountQueue(FinalizingField, session)
                    + CountQueue(VfxScenesField, session)
                    + (ReadBool(VfxLoadingField, session) ? 1 : 0);

                loaded += sessionLoaded;
                total += sessionLoaded + remaining;
            }

            if (total <= 0)
                return 0f;

            return Mathf.Clamp((float)loaded / total, 0f, 1f);
        }
    }

    private static int ReadInt(FieldInfo field, object instance)
    {
        return field.GetValue(instance) is int value ? value : 0;
    }

    private static bool ReadBool(FieldInfo field, object instance)
    {
        return field.GetValue(instance) is bool value && value;
    }

    private static int CountQueue(FieldInfo field, object instance)
    {
        return field.GetValue(instance) is ICollection collection ? collection.Count : 0;
    }
}

internal sealed class LoadingProgressOverlay
{
    private readonly Control _container;
    private readonly ColorRect _fill;
    private readonly Label _label;

    private LoadingProgressOverlay(Control container, ColorRect fill, Label label)
    {
        _container = container;
        _fill = fill;
        _label = label;
    }

    public static LoadingProgressOverlay Create(Control parent)
    {
        CanvasLayer layer = new()
        {
            Name = "IntroSkipLoadingProgressLayer",
            Layer = 100
        };

        Control container = new()
        {
            Name = "IntroSkipLoadingProgress",
            MouseFilter = Control.MouseFilterEnum.Ignore,
            AnchorLeft = 0.5f,
            AnchorRight = 0.5f,
            AnchorTop = 0.68f,
            AnchorBottom = 0.68f,
            OffsetLeft = -240f,
            OffsetRight = 240f,
            OffsetTop = 0f,
            OffsetBottom = 72f
        };

        ColorRect barBackground = new()
        {
            Name = "IntroSkipProgressBarBackground",
            Color = new Color(0f, 0f, 0f, 0.55f),
            AnchorLeft = 0f,
            AnchorRight = 1f,
            AnchorTop = 0f,
            AnchorBottom = 0f,
            OffsetLeft = 0f,
            OffsetRight = 0f,
            OffsetTop = 0f,
            OffsetBottom = 16f,
            MouseFilter = Control.MouseFilterEnum.Ignore
        };

        ColorRect barFill = new()
        {
            Name = "IntroSkipProgressBarFill",
            Color = new Color(1f, 0.86f, 0.28f, 0.95f),
            AnchorLeft = 0f,
            AnchorRight = 0f,
            AnchorTop = 0f,
            AnchorBottom = 0f,
            OffsetLeft = 0f,
            OffsetRight = 0f,
            OffsetTop = 0f,
            OffsetBottom = 16f,
            MouseFilter = Control.MouseFilterEnum.Ignore
        };

        Label label = new()
        {
            Name = "IntroSkipProgressLabel",
            Text = "0%",
            HorizontalAlignment = HorizontalAlignment.Center,
            AnchorLeft = 0f,
            AnchorRight = 1f,
            AnchorTop = 0f,
            AnchorBottom = 0f,
            OffsetLeft = 0f,
            OffsetRight = 0f,
            OffsetTop = 24f,
            OffsetBottom = 64f,
            MouseFilter = Control.MouseFilterEnum.Ignore
        };
        label.AddThemeColorOverride("font_color", Colors.White);
        label.AddThemeColorOverride("font_shadow_color", Colors.Black);
        label.AddThemeConstantOverride("shadow_offset_x", 2);
        label.AddThemeConstantOverride("shadow_offset_y", 2);
        label.AddThemeFontSizeOverride("font_size", 28);

        container.AddChild(barBackground);
        container.AddChild(barFill);
        container.AddChild(label);
        layer.AddChild(container);
        parent.AddChild(layer);

        return new LoadingProgressOverlay(container, barFill, label);
    }

    public void SetProgress(float progress)
    {
        int percent = Mathf.Clamp(Mathf.RoundToInt(progress * 100f), 0, 100);
        _fill.OffsetRight = 480f * percent / 100f;
        _label.Text = $"{percent}%";
    }

    public async Task RunAsync(CancellationToken token)
    {
        float displayedProgress = 0f;

        while (!token.IsCancellationRequested)
        {
            float actualProgress = LoadingProgressTracker.Progress;

            if (actualProgress >= 0.99f)
            {
                SetProgress(1f);
                return;
            }

            float targetProgress = Mathf.Min(actualProgress, 0.99f);
            displayedProgress = Mathf.Max(displayedProgress, Mathf.Lerp(displayedProgress, targetProgress, 0.18f));
            SetProgress(displayedProgress);

            await Task.Delay(33, token);
        }
    }

    public static async Task FadeControl(Control control, float fromAlpha, float toAlpha, float duration)
    {
        control.Modulate = new Color(1f, 1f, 1f, fromAlpha);
        ulong startTicks = Time.GetTicksMsec();
        ulong durationMs = (ulong)(duration * 1000f);

        while (true)
        {
            ulong elapsed = Time.GetTicksMsec() - startTicks;
            float t = durationMs == 0 ? 1f : Mathf.Clamp((float)elapsed / durationMs, 0f, 1f);
            control.Modulate = new Color(1f, 1f, 1f, Mathf.Lerp(fromAlpha, toAlpha, t));

            if (t >= 1f)
                return;

            await Task.Delay(16);
        }
    }

    public Task FadeOutAsync(float duration)
    {
        return FadeControl(_container, 1f, 0f, duration);
    }
}
