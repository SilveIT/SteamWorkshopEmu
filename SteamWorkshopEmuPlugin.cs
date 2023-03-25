using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Web;
using BepInEx;
using HarmonyLib;
using Steamworks;
using UnityEngine;
// ReSharper disable InconsistentNaming

namespace SteamWorkshopEmu;

[BepInPlugin(PluginInfo.PLUGIN_GUID, PluginInfo.PLUGIN_NAME, PluginInfo.PLUGIN_VERSION)]
public class SteamWorkshopEmuPlugin : BaseUnityPlugin
{
    public Queue<EmuState> StateQueue { get; private set; }
    public bool MenuIsOpen { get; private set; }
    private string _currentInput = string.Empty;
    private Vector2 _currentScroll = new(0, Mathf.Infinity);
    public string CurrentStatus { get; set; }
    private bool _installing;

    public static SteamWorkshopEmuPlugin I { get; private set; }
    // ReSharper disable once InconsistentNaming
    public SteamUGCEmu UGC { get; set; }
    public SReflectionHelper ReflectionHelper { get; private set; }
    // ReSharper disable once UnusedMember.Local
    private void Awake()
    {
        StateQueue = new Queue<EmuState>();

        ShowState("Initializing reflection helper...", 1000);
        I = this;
        ReflectionHelper = new SReflectionHelper(true);
        var failed = ReflectionHelper.LoadRequiredMemberInfoForAssembly(Assembly.GetExecutingAssembly());
        if (failed.Count != 0)
        {
            var r = "";
            r += "These requirements were not satisfied:\r\n";
            r = failed.Aggregate(r, (current, s) => current + (s + "\r\n"));
            Logger.LogFatal(r);
        }

        ShowState("Initializing Harmony...", 1000);
        var harmony = new Harmony("SteamWorkshopEmuMain");
        harmony.PatchAll();
        var patchedCount = harmony.GetPatchedMethods().Count();
        ShowState("Harmony successful patches: " + patchedCount, 1000);
        Logger.LogInfo("Patch count: " + patchedCount);

        ShowState("Initializing SteamUGCEmu...", 1000);
        Logger.LogInfo("Initializing SteamUGCEmu...");
        UGC = new SteamUGCEmu();
        ShowState("SteamWorkshopEmu initialized!");
        Logger.LogInfo("SteamWorkshopEmu initialized!");

        // Plugin startup logic
        Logger.LogInfo($"Plugin {PluginInfo.PLUGIN_GUID} is loaded!");
    }

    public void ShowState(string text, int durationMs = 3000) => 
        StateQueue.Enqueue(new EmuState(text, TimeSpan.FromMilliseconds(durationMs)));

    // ReSharper disable once UnusedMember.Local
    private void Update()
    {
        if (Input.GetKeyDown(KeyCode.BackQuote)) 
            ToggleMenu();
    }

    private void ToggleMenu() => MenuIsOpen = !MenuIsOpen;

    // ReSharper disable once UnusedMember.Local
    private void OnGUI()
    {
        ShowCurrentState();
        if (MenuIsOpen)
        {
            var areaRect = new Rect(Screen.width / 4f, Screen.height / 4f, Screen.width / 2f, Screen.height / 2f);
            GUILayout.BeginArea(areaRect);
            GUILayout.Box("SteamWorkshopEmu Item Installer");
            _currentScroll = GUILayout.BeginScrollView(_currentScroll);
            _currentInput = GUILayout.TextArea(_currentInput, 100000, GUILayout.MaxHeight(Screen.width / 2f - 30));
            GUILayout.EndScrollView(true);
            if (_installing)
                GUILayout.Label("Installing...");
            else
                if (GUILayout.Button("Install")) 
                    _ = ProcessInstallText(_currentInput);
            GUILayout.EndArea();
        }
        else
            _currentInput = string.Empty;
    }

    private async Task ProcessInstallText(string text)
    {
        _installing = true;

        try
        {
            var lines = text.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);
            var ids = lines.Select(GetFileID).Where(id => id != 0).ToList();
            foreach (var id in ids)
                await UGC.InstallItem(new PublishedFileId_t(id));
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
            ShowState("Installation process critial error!", 8000);
        }
        

        _installing = false;
    }

    private static ulong GetFileID(string idOrURL)
    {
        var containsOnlyDigits = idOrURL.All(char.IsDigit);
        string id;
        if (containsOnlyDigits)
            id = idOrURL;
        else
        {
            var uri = new Uri(idOrURL);
            id = HttpUtility.ParseQueryString(uri.Query).Get("id");
        }
        ulong.TryParse(id, out var res);
        return res;
    }

    private void ShowCurrentState()
    {
        if (string.IsNullOrEmpty(CurrentStatus))
            if(StateQueue.Count <= 0)
                return;
            else
            {
                var gotState = StateQueue.Dequeue();
                CurrentStatus = gotState.Text;
                Task.Delay(gotState.ShowTimeMs).ContinueWith(_ => CurrentStatus = string.Empty);
            }

        GUILayout.BeginArea(new Rect(10, 10, Screen.width / 4f, 25f));
        GUILayout.Box(CurrentStatus);
        GUILayout.EndArea();
    }
}

public class EmuState
{
    public string Text { get; }
    public TimeSpan ShowTimeMs { get; }

    public EmuState(string text, TimeSpan time)
    {
        Text = text;
        ShowTimeMs = time;
    }
}