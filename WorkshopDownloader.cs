using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Steamworks;
// ReSharper disable UnusedMember.Global

namespace SteamWorkshopEmu;

public static class WorkshopDownloader
{
    public static async Task<(bool, uint)> InstallAsync(PublishedFileId_t fileId, string targetPath, int timeoutSec = 30)
    {
        var zip = targetPath + ".zip";
        var res = await DownloadAsync(fileId, zip, timeoutSec);
        if (!res.Item1)
            return res;

        try
        {
            ZipFile.ExtractToDirectory(zip, targetPath);
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
            return (false, 0);
        }
        finally
        {
            try
            {
                File.Delete(zip);
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
            }
        }

        return (true, res.Item2);
    }

    public static async Task<(bool, uint)> DownloadAsync(PublishedFileId_t fileId, string zipPath, int timeoutSec = 30)
    {
        var httpClient = new HttpClient();
        httpClient.Timeout = TimeSpan.FromSeconds(timeoutSec);
        var requestInfo = new RequestWorkshopInfo
        {
            PublishedFileId = fileId.m_PublishedFileId,
            DownloadFormat = "raw"
        };
        var jsonRequest = JsonSerializer.Serialize(requestInfo);
        var content = new StringContent(jsonRequest, Encoding.UTF8, "application/json");
        var result = await httpClient.PostAsync("https://node04.steamworkshopdownloader.io/prod/api/download/request", content);
        var resultStr = await result.Content.ReadAsStringAsync();

        if (JsonSerializer.Deserialize(resultStr, typeof(RequestWorkshopResponse)) is not RequestWorkshopResponse jsonResult)
            return (false, 0);

        var lastUpdate = DateTime.Now;
        var lastState = string.Empty;
        var lastProgress = -1;

        while (true)
        {
            await Task.Delay(1000);

            try
            {
                var request = new StatusWorkshopRequest();
                request.Uuids.Add(jsonResult.Uuid);

                var jsonRequestStatus = JsonSerializer.Serialize(request);

                var statusResponse = await httpClient.PostAsync("https://node04.steamworkshopdownloader.io/prod/api/download/status", new StringContent(jsonRequestStatus, Encoding.UTF8, "application/json"));
                var statusText = await statusResponse.Content.ReadAsStringAsync();
                var statusWorkshop = JsonSerializer.Deserialize(statusText, typeof(Dictionary<string, StatusWorkshopResponse>)) as Dictionary<string, StatusWorkshopResponse>;
                if (statusWorkshop == null)
                    continue;

                var gotResult = false;
                var appId = 0u;

                foreach (var statusWork in statusWorkshop)
                {
                    var key = statusWork.Key;
                    var value = statusWork.Value;

                    if (lastState != value.Status || lastProgress != value.Progress)
                    {
                        lastUpdate = DateTime.Now;
                        lastState = value.Status;
                        lastProgress = value.Progress;
                    }

                    if (value.Status != "prepared" || value.Progress < 100) continue;

                    var fileResponse = await httpClient.GetAsync($"https://{value.StorageNode}/prod//storage/{value.StoragePath}?uuid={key}");
                    if (fileResponse == null)
                        continue;


                    using Stream contentStream = await fileResponse.Content.ReadAsStreamAsync(),
                        stream = new FileStream(zipPath, FileMode.Create, FileAccess.Write, FileShare.None, 1024, true);
                    await contentStream.CopyToAsync(stream);

                    var slash = value.StoragePath.IndexOf('/');
                    if (slash > 0)
                        uint.TryParse(value.StoragePath.Substring(0, slash), out appId);

                    gotResult = true;
                }

                Console.WriteLine("Status: " + statusText);

                if (gotResult)
                    return (true, appId);
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
            }

            if (DateTime.Now - lastUpdate <= TimeSpan.FromSeconds(timeoutSec)) continue;
            Console.WriteLine("Downloading failed for the workshop id: " + fileId.m_PublishedFileId);
            return (false, 0);
        }
    }
}

public class RequestWorkshopInfo
{
    public ulong PublishedFileId { get; set; }
    public string CollectionId { get; set; }
    public bool Hidden { get; set; }
    public string DownloadFormat { get; set; }
    public bool AutoDownload { get; set; }
}

public class RequestWorkshopResponse
{
    [JsonPropertyName("uuid")]
    public string Uuid { get; set; }
}

public class StatusWorkshopResponse
{
    [JsonPropertyName("age")]
    public ulong Age { get; set; }
    [JsonPropertyName("bytes_size")]
    public ulong ByteSize { get; set; }
    [JsonPropertyName("bytes_transmitted")]
    public ulong ByteTransmitted { get; set; }
    [JsonPropertyName("downloadError")]
    public string DownloadError { get; set; }
    [JsonPropertyName("progress")]
    public int Progress { get; set; }
    [JsonPropertyName("progressText")]
    public string ProgressText { get; set; }
    [JsonPropertyName("status")]
    public string Status { get; set; }
    [JsonPropertyName("storageNode")]
    public string StorageNode { get; set; }
    [JsonPropertyName("storagePath")]
    public string StoragePath { get; set; }
}

public class StatusWorkshopRequest
{
    [JsonPropertyName("uuids")]
    public List<string> Uuids { get; set; } = new();
}