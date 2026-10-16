using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using UnityEngine;

namespace CMAClothingFix;

[BepInPlugin("community.cma.clothingfix", "CMA Clothing Fix", Plugin.Version)]
[BepInProcess("Corrupted Modeling Agency.exe")]
public sealed class Plugin : BasePlugin
{
    internal static ManualLogSource Logger;
    internal const string Version = "0.8.0"; // keep equal to <Version> in the csproj
    internal static ConfigEntry<bool> Enabled, Diagnostics;
    internal static ConfigEntry<bool> ExperimentalCoverage;
    internal static ConfigEntry<float> RefreshSeconds, CoverReachCm, CoverDepthCm, CoverHemKeepCm, PushMaxCm;
    internal static ConfigEntry<string> Mode;
    internal static ConfigEntry<KeyCode> MenuKey;
    internal static ConfigFile Settings;
    internal static string RulesPath, ReportsPath;

    public override void Load()
    {
        Logger = Log;
        Settings = Config;
        Enabled = Config.Bind("General", "Enabled", true, "Enable automatic clipping protection.");
        Diagnostics = Config.Bind("General", "Diagnostics", false, "Write outfit reports when outfits change; also available in the menu.");
        ExperimentalCoverage = Config.Bind("General", "ExperimentalCoverage", true, "Local coverage masks using all native garment layers and opaque material surfaces.");
        RefreshSeconds = Config.Bind("General", "RefreshSeconds", 2f, new ConfigDescription("Seconds between scheduled automatic pose updates.", new AcceptableValueRange<float>(2f, 30f)));
        CoverReachCm = Config.Bind("Coverage", "CoverReachCm", 2f, new ConfigDescription("How far above the skin (cm, for a 170 cm character) a garment may float and still hide skin beneath it. Lower keeps more skin near loose cups.", new AcceptableValueRange<float>(.5f, 6f)));
        CoverDepthCm = Config.Bind("Coverage", "CoverDepthCm", 5f, new ConfigDescription("How deep below the skin (cm, for a 170 cm character) a garment may sink and still hide the skin over it. Raise for very large busts.", new AcceptableValueRange<float>(1f, 10f)));
        CoverHemKeepCm = Config.Bind("Coverage", "CoverHemKeepCm", 1f, new ConfigDescription("Cloth this far (cm, for a 170 cm character) below the lower hem of an outer garment is left visible, which avoids jagged cuts under thick hems. 0 disables.", new AcceptableValueRange<float>(0f, 8f)));
        Mode = Config.Bind("Coverage", "Mode", "Hide", new ConfigDescription("Hide deletes covered triangles (jagged edges possible). Snap = Hide plus sliding boundary vertices to the garment edge for smooth cuts. Inflate keeps all geometry and lifts garments that sank into the skin out to just above it (experimental, no layer rules). Push keeps all geometry and moves vertices that poke through an outer garment back under it (experimental).", new AcceptableValueList<string>("Hide", "Snap", "Push", "Inflate")));
        PushMaxCm = Config.Bind("Coverage", "PushMaxCm", 5f, new ConfigDescription("Push mode: largest poke-through (cm, for a 170 cm character) that is pushed back. Larger pokes are ignored.", new AcceptableValueRange<float>(.5f, 8f)));
        MenuKey = Config.Bind("Keys", "Menu", KeyCode.Slash, "Open clipping menu.");
        RulesPath = Path.Combine(Paths.ConfigPath, "CMAClothingFix.rules.json");
        ReportsPath = Path.Combine(Paths.BepInExRootPath, "CMAClothingFix-reports");
        AddComponent<FixController>();
        Log.LogInfo(Version + " loaded. Automatic clothing protection; / opens settings.");
    }
}

public sealed class FixController : MonoBehaviour
{
    public FixController(IntPtr pointer) : base(pointer) { }
    private readonly RuleStore rules = new();
    private readonly VisibilityLedger ledger = new();
    private readonly CoverageFitter coverage = new();
    private readonly Dictionary<int, string> reported = new();
    private float nextScan;
    private bool initialized;
    private string lastError;
    private bool menuOpen, dumpRequested, diagnosticsOpen;
    private Assets.NewFeatures.Common.CursorInputDriver cursorDriver;
    private bool cursorDriverLive, cursorWarned;
    private GUIStyle titleStyle, textStyle, mutedStyle;
    private float nextSettingsCheck;
    private DateTime configWrite, rulesWrite;
    private CursorLockMode cursorLock;
    private bool cursorVisible;

    public void Update()
    {
        try
        {
            if (!initialized) { rules.Reload(); initialized = true; }
            if (Input.GetKeyDown(Plugin.MenuKey.Value)) SetMenu(!menuOpen);
            bool dump = dumpRequested; dumpRequested = false;
            if (Time.unscaledTime >= nextSettingsCheck)
            {
                nextSettingsCheck = Time.unscaledTime + 2;
                var currentConfig = File.GetLastWriteTimeUtc(Plugin.Settings.ConfigFilePath);
                var currentRules = File.GetLastWriteTimeUtc(Plugin.RulesPath);
                if (currentConfig != configWrite || currentRules != rulesWrite)
                {
                    Plugin.Settings.Reload(); rules.Reload();
                    configWrite = File.GetLastWriteTimeUtc(Plugin.Settings.ConfigFilePath);
                    rulesWrite = currentRules;
                    nextScan = 0;
                }
            }
            if (!dump && Time.unscaledTime < nextScan) return;
            nextScan = Time.unscaledTime + .3f;
            var outfits = Wardrobe.Capture();
            // Explicit user rules take precedence over experimental automatic geometry.
            if (!Plugin.Enabled.Value || !Plugin.ExperimentalCoverage.Value || !rules.Valid || rules.Rules.Length > 0) coverage.Restore();
            var live = outfits.Select(x => x.Character.GetInstanceID()).ToHashSet();
            foreach (int key in reported.Keys.Where(x => !live.Contains(x)).ToArray()) reported.Remove(key);
            ledger.Begin();
            foreach (var outfit in outfits)
            {
                try
                {
                    int key = outfit.Character.GetInstanceID();
                    string signature = outfit.Signature();
                    if (dump || (Plugin.Diagnostics.Value && (!reported.TryGetValue(key, out var old) || old != signature)))
                    {
                        Wardrobe.WriteReport(outfit, ledger, coverage);
                        reported[key] = signature;
                    }
                    if (Plugin.Enabled.Value) ledger.Apply(outfit, rules.Rules);
                }
                catch (Exception ex) { WarnOnce(ex); }
            }
            ledger.End();
            coverage.Tick(outfits, Plugin.Enabled.Value && Plugin.ExperimentalCoverage.Value && rules.Valid && rules.Rules.Length == 0);
            if (dump && outfits.Count == 0) Plugin.Logger.LogInfo("No active female wardrobe found. Load a game and bring a character into the scene.");
        }
        catch (Exception ex) { coverage.Restore(); ledger.Restore(); nextScan = Time.unscaledTime + 5; WarnOnce(ex); }
    }

    private void SetMenu(bool open)
    {
        if (open == menuOpen) return;
        if (open) { cursorLock = Cursor.lockState; cursorVisible = Cursor.visible; }
        menuOpen = open;
        try
        {
            cursorDriver ??= new Assets.NewFeatures.Common.CursorInputDriver(this);
            cursorDriver.SetCursorShown(open);
            cursorDriverLive = true;
        }
        catch (Exception ex)
        {
            cursorDriverLive = false;
            if (!cursorWarned) { cursorWarned = true; Plugin.Logger.LogWarning("Native menu cursor unavailable: " + ex.Message); }
        }
        if (open) KeepCursor();
        else if (!cursorDriverLive) { Cursor.lockState = cursorLock; Cursor.visible = cursorVisible; }
    }

    private void KeepCursor()
    {
        if (menuOpen) { Cursor.lockState = CursorLockMode.None; Cursor.visible = true; }
    }

    public void LateUpdate() => KeepCursor();

    public void OnGUI()
    {
        if (!menuOpen) return;
        KeepCursor();
        if (titleStyle == null)
        {
            titleStyle = new GUIStyle(GUI.skin.label) { fontSize = 22, fontStyle = FontStyle.Bold };
            textStyle = new GUIStyle(GUI.skin.label) { fontSize = 14, wordWrap = true };
            mutedStyle = new GUIStyle(textStyle);
            mutedStyle.normal.textColor = new Color(.7f, .76f, .82f, 1);
        }
        float height = diagnosticsOpen ? 340 : 255;
        var rect = new Rect(Mathf.Max(12, Screen.width - 452), 80, 430, height);
        var color = GUI.color;
        GUI.color = new Color(.065f, .085f, .12f, .98f);
        GUI.DrawTexture(rect, Texture2D.whiteTexture);
        GUI.color = color;
        GUILayout.BeginArea(new Rect(rect.x + 18, rect.y + 14, rect.width - 36, rect.height - 28));
        GUILayout.BeginHorizontal();
        GUILayout.Label("Clothing Fix", titleStyle);
        if (GUILayout.Button("Close /", GUILayout.Width(75), GUILayout.Height(28))) SetMenu(false);
        GUILayout.EndHorizontal();
        GUILayout.Space(10);
        bool enabled = GUILayout.Toggle(Plugin.Enabled.Value, " Automatic clipping protection");
        if (enabled != Plugin.Enabled.Value) { Plugin.Enabled.Value = enabled; nextScan = 0; }
        GUILayout.Space(8);
        string status = !enabled ? "Protection is paused" : !rules.Valid ? "Check the clipping rules file" : coverage.PendingMasks > 0 ? "Updating clothing coverage..." : "Monitoring outfits and poses";
        GUILayout.Label(status, textStyle);
        GUILayout.Label($"{coverage.ActiveMasks} active masks", mutedStyle);
        GUILayout.Space(6);
        GUILayout.Label("Coverage updates automatically when clothes or poses change. Nothing needs to be applied manually.", textStyle);
        GUILayout.Space(10);
        if (GUILayout.Button(diagnosticsOpen ? "Hide diagnostics" : "Diagnostics")) diagnosticsOpen = !diagnosticsOpen;
        if (diagnosticsOpen)
        {
            GUILayout.Label($"{coverage.SampledTextures} alpha textures cached", mutedStyle);
            GUILayout.Label("Fabric opacity and garment layers are checked automatically. Unknown materials and uncertain layer order are kept visible.", mutedStyle);
            if (GUILayout.Button("Save clothing report")) dumpRequested = true;
        }
        GUILayout.EndArea();
        KeepCursor();
    }

    [Il2CppInterop.Runtime.Attributes.HideFromIl2Cpp]
    private void WarnOnce(Exception ex)
    {
        string message = ex.ToString();
        if (message != lastError) { Plugin.Logger.LogWarning(message); lastError = message; }
    }
    public void OnDisable() { SetMenu(false); coverage.Restore(); ledger.Restore(); }
    public void OnDestroy() { SetMenu(false); coverage.Restore(); ledger.Restore(); }
}
