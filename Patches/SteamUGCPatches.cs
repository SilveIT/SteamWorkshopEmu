using System;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using HarmonyLib;
using Steamworks;

// ReSharper disable InconsistentNaming

namespace SteamWorkshopEmu.Patches;

public static class SteamUGCPatches
{
    //System.UInt32 Steamworks.SteamUGC::GetNumSubscribedItems()
    [MethodRequired("Steamworks.SteamUGC", "GetNumSubscribedItems", "Steamworks.SteamUGC::GetNumSubscribedItems hook", 0)]
    [HarmonyPatch]
    public class SteamUGC_GetNumSubscribedItems
    {
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static MethodBase TargetMethod() => R.M[0];
        public static bool Prefix(out uint __result)
        {
            __result = 0;

            var maps = SteamWorkshopEmuPlugin.I.UGC.WorkshopItems.Select(j => j.ID.m_PublishedFileId).Aggregate(string.Empty, (j, k) => j + "\r\n" + k);
            Console.WriteLine("Available workshop items:" + maps);

            __result = (uint)SteamWorkshopEmuPlugin.I.UGC.WorkshopItems.Count;
            return false;
        }
    }

    //System.UInt32 Steamworks.SteamUGC::GetSubscribedItems(Steamworks.PublishedFileId_t[],System.UInt32)
    [MethodRequired("Steamworks.SteamUGC", "GetSubscribedItems", "Steamworks.SteamUGC::GetSubscribedItems hook", 2)]
    [HarmonyPatch]
    public class SteamUGC_GetSubscribedItems
    {
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static MethodBase TargetMethod() => R.M[0];
        public static bool Prefix(out uint __result, ref PublishedFileId_t[] pvecPublishedFileID, uint cMaxEntries)
        {
            Console.WriteLine("SteamUGC::GetSubscribedItems, count = " + cMaxEntries);
            __result = SteamWorkshopEmuPlugin.I.UGC.FillPublishedFileIds(ref pvecPublishedFileID, cMaxEntries);
            return false;
        }
    }

    //System.Boolean Steamworks.SteamUGC::GetItemInstallInfo(Steamworks.PublishedFileId_t,System.UInt64&,System.String&,System.UInt32,System.UInt32&)
    [MethodRequired("Steamworks.SteamUGC", "GetItemInstallInfo", "Steamworks.SteamUGC::GetItemInstallInfo hook", 5)]
    [HarmonyPatch]
    public class SteamUGC_GetItemInstallInfo
    {
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static MethodBase TargetMethod() => R.M[0];
        public static bool Prefix(out bool __result, ref PublishedFileId_t nPublishedFileID, out ulong punSizeOnDisk, out string pchFolder, uint cchFolderSize, out uint punTimeStamp)
        {
            punSizeOnDisk = 1000;
            punTimeStamp = 1000;
            pchFolder = SteamWorkshopEmuPlugin.I.UGC.GetItemPath(nPublishedFileID);
            __result = !string.IsNullOrEmpty(pchFolder);
            //Console.WriteLine("SteamUGC::GetItemInstallInfo, result: " + __result + "; " + pchFolder);
            return false;
        }
    }

    //System.UInt32 Steamworks.SteamUGC::GetItemState(Steamworks.PublishedFileId_t)
    [MethodRequired("Steamworks.SteamUGC", "GetItemState", "Steamworks.SteamUGC::GetItemState hook", 1)]
    [HarmonyPatch]
    public class SteamUGC_GetItemState
    {
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static MethodBase TargetMethod() => R.M[0];
        public static bool Prefix(out uint __result, ref PublishedFileId_t nPublishedFileID)
        {
            //Console.WriteLine("SteamUGC::GetItemState: " + nPublishedFileID.m_PublishedFileId);
            var itemState = SteamWorkshopEmuPlugin.I.UGC.GetItemState(nPublishedFileID);
            switch (itemState)
            {
                case WorkshopItem.WIStates.Installing:
                    __result = 16u;
                    break;
                case WorkshopItem.WIStates.Installed:
                    __result = 4;
                    break;
                case WorkshopItem.WIStates.None:
                case WorkshopItem.WIStates.Subscribed:
                default:
                    __result = 0;
                    break;
            }
            return false;
        }
    }

    //System.Boolean Steamworks.SteamUGC::DownloadItem(Steamworks.PublishedFileId_t,System.Boolean)
    [MethodRequired("Steamworks.SteamUGC", "DownloadItem", "Steamworks.SteamUGC::DownloadItem hook", 2)]
    [HarmonyPatch]
    public class SteamUGC_DownloadItem
    {
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static MethodBase TargetMethod() => R.M[0];
        public static bool Prefix(ref PublishedFileId_t nPublishedFileID, bool bHighPriority)
        {
            //Console.WriteLine("SteamUGC::DownloadItem: " + nPublishedFileID.m_PublishedFileId + ", installing...");
            _ = SteamWorkshopEmuPlugin.I.UGC.InstallItem(nPublishedFileID);
            //Console.WriteLine("SteamUGC::DownloadItem: installing initialized");
            return false;
        }
    }

    //Steamworks.SteamAPICall_t Steamworks.SteamUGC::SubscribeItem(Steamworks.PublishedFileId_t)
    [MethodRequired("Steamworks.SteamUGC", "SubscribeItem", "Steamworks.SteamUGC::SubscribeItem hook", 1)]
    [HarmonyPatch]
    public class SteamUGC_SubscribeItem
    {
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static MethodBase TargetMethod() => R.M[0];
        public static bool Prefix(ref PublishedFileId_t nPublishedFileID)
        {
            //Console.WriteLine("SteamUGC::SubscribeItem: " + nPublishedFileID.m_PublishedFileId);
            SteamWorkshopEmuPlugin.I.UGC.SubscribeItem(nPublishedFileID);
            return false;
        }
    }

    //Steamworks.SteamAPICall_t Steamworks.SteamUGC::SubscribeItem(Steamworks.PublishedFileId_t)
    [MethodRequired("Steamworks.SteamUGC", "UnsubscribeItem", "Steamworks.SteamUGC::UnsubscribeItem hook", 1)]
    [HarmonyPatch]
    public class SteamUGC_UnsubscribeItem
    {
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static MethodBase TargetMethod() => R.M[0];
        public static bool Prefix(out SteamAPICall_t __result, ref PublishedFileId_t nPublishedFileID)
        {
            __result = new SteamAPICall_t();
            //Console.WriteLine("SteamUGC::UnsubscribeItem: " + nPublishedFileID.m_PublishedFileId);
            _ = SteamWorkshopEmuPlugin.I.UGC.UnsubscribeItem(nPublishedFileID);
            return false;
        }
    }

    //Steamworks.UGCQueryHandle_t Steamworks.SteamUGC::CreateQueryAllUGCRequest(Steamworks.EUGCQuery,Steamworks.EUGCMatchingUGCType,Steamworks.AppId_t,Steamworks.AppId_t,System.UInt32)
    //[MethodRequired("Steamworks.SteamUGC", "CreateQueryAllUGCRequest", "Steamworks.SteamUGC::CreateQueryAllUGCRequest hook", 5)]
    //[HarmonyPatch]
    //public class SteamUGC_CreateQueryAllUGCRequest
    //{
    //    [MethodImpl(MethodImplOptions.NoInlining)]
    //    public static MethodBase TargetMethod() => R.M[0];
    //    public static bool Prefix(EUGCQuery eQueryType, EUGCMatchingUGCType eMatchingeMatchingUGCTypeFileType, AppId_t nCreatorAppID, AppId_t nConsumerAppID, uint unPage)
    //    {
    //        Console.WriteLine("SteamUGC::CreateQueryAllUGCRequest: \r\n" +
    //                          "eQueryType: " + eQueryType + "\r\n" +
    //                          "eMatchingeMatchingUGCTypeFileType: " + eMatchingeMatchingUGCTypeFileType + "\r\n" +
    //                          "nCreatorAppID: " + nCreatorAppID + "\r\n" +
    //                          "nConsumerAppID: " + nConsumerAppID + "\r\n" +
    //                          "unPage: " + unPage);
    //        return true;
    //    }
    //}

    ////System.Boolean Steamworks.SteamUGC::GetQueryUGCResult(Steamworks.UGCQueryHandle_t,System.UInt32,Steamworks.SteamUGCDetails_t&)
    //[MethodRequired("Steamworks.SteamUGC", "GetQueryUGCResult", "Steamworks.SteamUGC::GetQueryUGCResult hook", 3)]
    //[HarmonyPatch]
    //public class SteamUGC_GetQueryUGCResult
    //{
    //    [MethodImpl(MethodImplOptions.NoInlining)]
    //    public static MethodBase TargetMethod() => R.M[0];
    //    public static bool Prefix(UGCQueryHandle_t handle, uint index, SteamUGCDetails_t pDetails)
    //    {
    //        Console.WriteLine("SteamUGC::GetQueryUGCResult");
    //        return true;
    //    }
    //}
}