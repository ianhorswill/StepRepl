using System;
using System.Collections;
using System.IO;
using JetBrains.Annotations;
using UnityEngine;
using UnityEngine.Networking;

public class SoundController : MonoBehaviour
{
    private AudioSource audioSource;
    private string path;
    private bool pathChanged;

    public bool Loop;
    public string SoundPath
    {
        get => path;
        set
        {
            pathChanged = true;
            path = value;
        }
    }

    private Coroutine poll;

    [UsedImplicitly]
    public void Start()
    {
        audioSource = GetComponent<AudioSource>();
        poll = StartCoroutine(PollPath());
        Application.quitting += () => StopCoroutine(poll);
    }

    private IEnumerator PollPath()
    {
        while (true)
        {
            yield return new WaitUntil(() => pathChanged);
            pathChanged = false;
            if (path == null)
            {
                audioSource.Stop();
            }
            else if (File.Exists(path))
            {
                var type = FileAudioType(path);
                if (type != AudioType.UNKNOWN)
                {
                    var request = UnityWebRequestMultimedia.GetAudioClip("file://" + path, type);
                    yield return request.SendWebRequest();
                    if (!request.isNetworkError)
                        try
                        {
                            audioSource.clip = DownloadHandlerAudioClip.GetContent(request);
                            audioSource.loop = Loop;
                            audioSource.Play();
                        }
                        catch (Exception e)
                        {
                            Debug.LogException(e);
                        }
                } else 
                    Debug.LogError("Known file type for sound file: "+path);
            }
            else
                Debug.LogError("No such image file: " + path);
        }

        // ReSharper disable once IteratorNeverReturns
    }

    private AudioType FileAudioType(string soundFilePath)
    {
        switch (Path.GetExtension(soundFilePath))
        {
            case ".ogg": return AudioType.OGGVORBIS;
            case ".mp3": return AudioType.MPEG;
            case ".wav": return AudioType.WAV;
            case ".xma": return AudioType.XMA;
            default: return AudioType.UNKNOWN;
        }
    }
}