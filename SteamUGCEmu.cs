using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using JetBrains.Annotations;
using Steamworks;
using UnityEngine;
// ReSharper disable InconsistentNaming

namespace SteamWorkshopEmu;

//this.itemSubscribedCallback.Dispose();
//this.itemUnsubscribedCallback.Dispose();
//this.itemInstalledCallback.Dispose();
//this.itemNeedsUpdateCallback.Dispose();
//this.downloadItemResultCallback.Dispose();

// ReSharper disable once InconsistentNaming
//System.Int32 Steamworks.CallbackIdentities::GetCallbackIdentity(System.Type)
//System.Void Steamworks.Callback::OnRunCallback(System.IntPtr)
[MethodRequired("Steamworks.CallbackIdentities", "GetCallbackIdentity", "SteamUGCEmu", 1)]
[MethodRequired("Steamworks.Callback", "OnRunCallback", "SteamUGCEmu", 1)]
[FieldRequired("Steamworks.CallbackDispatcher", "m_registeredCallbacks", "SteamUGCEmu")]
[FieldRequired("Steamworks.CallbackDispatcher", "m_registeredGameServerCallbacks", "SteamUGCEmu")]
[FieldRequired("Steamworks.CallbackDispatcher", "m_registeredCallResults", "SteamUGCEmu")]
public class SteamUGCEmu
{
    //TODO find appid for workshop item
    public static AppId_t KnownAppID { get; private set; } = new AppId_t(0);

    private static Dictionary<int, List<Callback>> m_registeredCallbacks => (Dictionary<int, List<Callback>>)R.F[0].GetValue(null);
    //private static Dictionary<int, List<Callback>> m_registeredGameServerCallbacks => (Dictionary<int, List<Callback>>)R.F[1].GetValue(null);
    //private static Dictionary<ulong, List<CallResult>> m_registeredCallResults => (Dictionary<ulong, List<CallResult>>)R.F[2].GetValue(null);

    public object WorkshopItemsLock { get; }
    public List<WorkshopItem> WorkshopItems { get; }
    public string ItemsPath { get; }

    public SteamUGCEmu()
    {
        //ItemsPath = Path.Combine(Application.persistentDataPath, "WorkshopEmu");
        ItemsPath = Path.Combine(Application.persistentDataPath, "WorkshopEmu").Replace("\\", "/");
        if (!Directory.Exists(ItemsPath))
            Directory.CreateDirectory(ItemsPath);
        WorkshopItems = new List<WorkshopItem>();
        WorkshopItemsLock = new object();
        LoadItemList();
    }

    public void SubscribeItem(PublishedFileId_t fileId)
    {
        if (IsItemSubscribed(fileId))
            return;

        SteamWorkshopEmuPlugin.I.ShowState("Requested subscribe to " + fileId.m_PublishedFileId, 1000);

        var fileSubscribed = new RemoteStoragePublishedFileSubscribed_t
        {
            m_nAppID = KnownAppID,
            m_nPublishedFileId = fileId
        };

        var filePath = Path.Combine(ItemsPath, fileId.m_PublishedFileId.ToString()).Replace("\\", "/");
        var witem = new WorkshopItem(filePath, WorkshopItem.WIStates.Subscribed);
        lock (WorkshopItemsLock)
        {
            WorkshopItems.Add(new WorkshopItem(filePath, WorkshopItem.WIStates.Subscribed));
        }
        try
        {
            SendCallback(fileSubscribed);
            Console.WriteLine("[SubscribeItem] Callback<RemoteStoragePublishedFileSubscribed_t> successfull for " + fileId.m_PublishedFileId);
        }
        catch (Exception e)
        {
            Console.WriteLine($"[SubscribeItem] Error while sending RemoteStoragePublishedFileSubscribed_t for {fileId.m_PublishedFileId}\r\n" + e);
        }

        SteamWorkshopEmuPlugin.I.ShowState("Subscribe successful for " + fileId.m_PublishedFileId, 2000);
    }

    public async Task<bool> InstallItem(PublishedFileId_t fileId)
    {
        if (IsItemInstalledOrInstalling(fileId))
            return true;

        var witem = GetWorkshopItem(fileId);
        if (witem == null)
            SubscribeItem(fileId);
        witem = GetWorkshopItem(fileId)!;

        witem.State = WorkshopItem.WIStates.Installing;

        SteamWorkshopEmuPlugin.I.ShowState("Requested install of " + fileId.m_PublishedFileId);

        var filePath = Path.Combine(ItemsPath, fileId.m_PublishedFileId.ToString()).Replace("\\", "/");
        var (downloadSuccess, appId) = await WorkshopDownloader.InstallAsync(fileId, filePath);

        if (KnownAppID.m_AppId == 0 && appId != 0)
            KnownAppID = new AppId_t(appId);

        if (downloadSuccess) 
            witem.State = WorkshopItem.WIStates.Installed;

        SendInstalledCallback(fileId, downloadSuccess);

        if (downloadSuccess)
        {
            SteamWorkshopEmuPlugin.I.ShowState("Install successful for " + fileId.m_PublishedFileId);
            Console.WriteLine("[InstallItem] Install successfull for " + fileId.m_PublishedFileId);
        }
        else
        {
            SteamWorkshopEmuPlugin.I.ShowState("Install error for " + fileId.m_PublishedFileId, 5000);
            Console.WriteLine("[InstallItem] Install error for " + fileId.m_PublishedFileId);
        }
        return true;
    }

    public void RefreshInstalledItems()
    {
        foreach (var workshopItem in WorkshopItems.Where(workshopItem => workshopItem.State == WorkshopItem.WIStates.Installed))
            SendInstalledCallback(workshopItem.ID, true);
    }

    public void SendInstalledCallback(PublishedFileId_t fileId, bool downloadSuccess)
    {
        var downloadItemResult = new DownloadItemResult_t
        {
            m_eResult = downloadSuccess ? EResult.k_EResultOK : EResult.k_EResultTimeout,
            m_nPublishedFileId = fileId,
            m_unAppID = KnownAppID
        };

        try
        {
            SendCallback(downloadItemResult);
            Console.WriteLine("[InstallItem] Callback<DownloadItemResult_t> successfull for " + fileId.m_PublishedFileId);
        }
        catch (Exception e)
        {

            Console.WriteLine($"[InstallItem] Error while sending DownloadItemResult_t for {fileId.m_PublishedFileId}\r\n" + e);
        }
    }

    public async Task UnsubscribeItem(PublishedFileId_t fileId)
    {
        WorkshopItem witem;
        lock (WorkshopItemsLock)
        {
            witem = GetWorkshopItem(fileId);
            if (witem == null)
                return;
            WorkshopItems.Remove(witem);
        }

        try
        {
            var count = 0;
            while (witem.State == WorkshopItem.WIStates.Installing && count < 60)
            {
                //TODO attach cancellationtoken to the workshop item to prevent installation while the item is not needed anymore
                await Task.Delay(1000);
                count++;
            }

            if (Directory.Exists(witem.ItemPath))
                Directory.Delete(witem.ItemPath, true);
            if (File.Exists(witem.ItemPath + ".zip"))
                File.Delete(witem.ItemPath + ".zip");

            //TODO unsubscribed callback
        }
        catch (Exception e)
        {
            Console.WriteLine($"[UnsubscribeItem] Unsubscribe error for {fileId.m_PublishedFileId}:\r\n" + e);
        }
        Console.WriteLine("[UnsubscribeItem] Unsubscribe successfull for " + fileId.m_PublishedFileId);
    }

    public void SendCallback<T>(T result)
        where T : struct
    {
        var callbackIdentity = (int)R.M[0].Invoke(null, new object[] { typeof(T) });
        if (!m_registeredCallbacks.TryGetValue(callbackIdentity, out var list)) return;
        if (list.Count == 0)
            return;
        GCHandle gch = GCHandle.Alloc(result, GCHandleType.Pinned);
        foreach (var callback in list)
            try
            {
                R.M[1].Invoke(callback, new object[] { gch.AddrOfPinnedObject() });
            }
            catch (Exception e)
            {
                Console.WriteLine($"[SendCallback<{typeof(T)}>] Error while calling callback function\r\n" + e);
            }
        gch.Free();
    }

    public uint FillPublishedFileIds(ref PublishedFileId_t[] pvecPublishedFileID, uint cMaxEntries)
    {
        for (var i = 0; i < WorkshopItems.Count && i < cMaxEntries; i++)
            pvecPublishedFileID[i] = WorkshopItems[i].ID;
        return (uint)WorkshopItems.Count;
    }

    [CanBeNull]
    public WorkshopItem GetWorkshopItem(PublishedFileId_t fileId) =>
        WorkshopItems.FirstOrDefault(j => j.ID.m_PublishedFileId == fileId.m_PublishedFileId);

    //TODO fix path being null in the game
    [CanBeNull]
    public string GetItemPath(PublishedFileId_t id) =>
        //WorkshopItems.FirstOrDefault(k => k.ID.m_PublishedFileId == id.m_PublishedFileId)?.ItemPath.Replace("\\", "/");
        Path.Combine(ItemsPath, id.m_PublishedFileId.ToString()).Replace("\\", "/");

    public WorkshopItem.WIStates GetItemState(PublishedFileId_t fileId) =>
        WorkshopItems.FirstOrDefault(j => j.ID.m_PublishedFileId == fileId.m_PublishedFileId)?.State ?? WorkshopItem.WIStates.None;

    public bool IsItemSubscribed(PublishedFileId_t fileId) =>
        WorkshopItems.Any(j => j.ID.m_PublishedFileId == fileId.m_PublishedFileId && j.State is WorkshopItem.WIStates.Subscribed);

    public bool IsItemInstalled(PublishedFileId_t fileId) =>
        WorkshopItems.Any(j => j.ID.m_PublishedFileId == fileId.m_PublishedFileId &&
                               j.State is WorkshopItem.WIStates.Installed);

    public bool IsItemInstalledOrInstalling(PublishedFileId_t fileId) =>
        WorkshopItems.Any(j => j.ID.m_PublishedFileId == fileId.m_PublishedFileId &&
                               j.State is WorkshopItem.WIStates.Installed or WorkshopItem.WIStates.Installing);

    public void LoadItemList()
    {
        lock (WorkshopItemsLock)
        {
            WorkshopItems.Clear();
            foreach (var mapPath in GetItemsDirectory())
                WorkshopItems.Add(new WorkshopItem(mapPath, WorkshopItem.WIStates.Installed));
        }
    }

    public IEnumerable<string> GetItemsDirectory() =>
        Directory.EnumerateDirectories(ItemsPath).Where(j => Path.GetFileName(j)!.All(char.IsDigit));
}

public class WorkshopItem
{
    public WIStates State { get; set; }
    public PublishedFileId_t ID { get; set; }
    public string ItemPath { get; set; }

    public WorkshopItem(string path, WIStates state = WIStates.None)
    {
        ItemPath = path;
        ID = new PublishedFileId_t(ulong.Parse(Path.GetFileName(path)));
        State = state;
    }

    public enum WIStates
    {
        None,
        Subscribed,
        Installing,
        Installed
    }
}