using System;
using System.Collections;
using System.IO;
using System.Security.Cryptography;
using System.Threading;
using UnityEngine;
using UnityEngine.Networking;

namespace BetterSpeech;

/// <summary>
/// Downloads one model to "&lt;file&gt;.part", verifies its SHA256 on a worker thread (hashing 500+ MB on the
/// main thread would freeze the game for seconds) and only then renames it into place.
/// </summary>
internal sealed class ModelDownload
{
    public enum State
    {
        Downloading,
        Verifying,
        Done,
        Failed,
        Cancelled,
    }

    public readonly ModelInfo Model;
    public State Status { get; private set; } = State.Downloading;
    public float Progress { get; private set; }
    public string Error { get; private set; } = "";

    private readonly string target;
    private UnityWebRequest request;
    private bool cancelRequested;

    public ModelDownload(ModelInfo model, string modelDirectory)
    {
        Model = model;
        target = Path.Combine(modelDirectory, model.FileName);
    }

    public bool Finished => Status == State.Done || Status == State.Failed || Status == State.Cancelled;

    public void Cancel()
    {
        cancelRequested = true;
        request?.Abort();
    }

    public IEnumerator Run()
    {
        string part = target + ".part";
        Directory.CreateDirectory(Path.GetDirectoryName(target));
        TryDelete(part);

        using (request = new UnityWebRequest(Model.Url, UnityWebRequest.kHttpVerbGET))
        {
            // The game's stripped UnityWebRequest has neither downloadProgress nor removeFileOnAbort.
            request.downloadHandler = new DownloadHandlerFile(part, false);
            UnityWebRequestAsyncOperation operation = request.SendWebRequest();
            while (!operation.isDone)
            {
                Progress = Model.SizeBytes > 0 ? Mathf.Clamp01((float)request.downloadedBytes / Model.SizeBytes) : 0f;
                yield return null;
            }
            if (cancelRequested)
            {
                Status = State.Cancelled;
                TryDelete(part);
                yield break;
            }
            if (request.result != UnityWebRequest.Result.Success)
            {
                Fail("Download failed: " + request.error, part);
                yield break;
            }
        }
        request = null;

        Status = State.Verifying;
        Progress = 1f;
        string hash = null;
        Exception hashError = null;
        var worker = new Thread(() =>
        {
            try
            {
                hash = Sha256(part);
            }
            catch (Exception ex)
            {
                hashError = ex;
            }
        }) { IsBackground = true };
        worker.Start();
        while (worker.IsAlive)
        {
            yield return null;
        }

        if (hashError != null)
        {
            Fail("Could not verify the file: " + hashError.Message, part);
            yield break;
        }
        if (!string.Equals(hash, Model.Sha256, StringComparison.OrdinalIgnoreCase))
        {
            Fail("Checksum mismatch, the file is damaged. Try again.", part);
            yield break;
        }

        TryDelete(target);
        File.Move(part, target);
        Status = State.Done;
        Debug.Log($"[BetterSpeech] Downloaded and verified {Model.Id} -> {target}");
    }

    private void Fail(string message, string part)
    {
        Error = message;
        Status = State.Failed;
        TryDelete(part);
        Debug.LogWarning($"[BetterSpeech] {Model.Id}: {message}");
    }

    private static string Sha256(string file)
    {
        using (SHA256 sha = SHA256.Create())
        using (FileStream stream = File.OpenRead(file))
        {
            byte[] digest = sha.ComputeHash(stream);
            var hex = new char[digest.Length * 2];
            const string digits = "0123456789abcdef";
            for (int i = 0; i < digest.Length; i++)
            {
                hex[i * 2] = digits[digest[i] >> 4];
                hex[i * 2 + 1] = digits[digest[i] & 15];
            }
            return new string(hex);
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException)
        {
        }
    }
}
