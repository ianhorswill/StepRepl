using System;
using System.Collections;
using System.IO;
using System.Linq;
using JetBrains.Annotations;
using Step;
using Step.Interpreter;
using Step.Output;
using UnityEngine;
using UnityEngine.Networking;

public class SoundController : MonoBehaviour
{
    public static SoundController Singleton;
    
    private static readonly string[] SoundFileExtensions = { ".mp3", ".ogg", ".wav" };

    static SoundController()
    {
        Module.Global["SoundHere"] = new GeneralPredicate<object, string>("SoundHere", null,
            // ReSharper disable once AssignNullToNotNullAttribute
            fileName =>
            {
                string stringName;
                switch (fileName)
                {
                    case string[] tokens:
                        stringName = tokens.Untokenize(new FormattingOptions() {Capitalize = false});
                        break;

                    default:
                        stringName = fileName.ToString();
                        break;
                }

                var path = Path.Combine(Path.GetDirectoryName(MethodCallFrame.CurrentFrame.Method.FilePath),
                    stringName);

                if (string.IsNullOrEmpty(Path.GetExtension(path)))
                    return SoundFileExtensions.Select(p => Path.ChangeExtension(path, p)).Where(File.Exists);
                if (File.Exists(path))
                    return new[] {path};
                return new string[0];
            },
            null, null);


        Module.Global["PlaySound"] = new SimplePredicate<string>("PlaySound", path =>
        {
            if (path == "nothing")
            {
                Singleton.SoundPath = null;
                return true;
            }

            if (!File.Exists(path))
                return false;
            Singleton.Loop = false;
            Singleton.SoundPath = path;
            return true;
        });

        Module.Global["PlaySoundLoop"] = new SimplePredicate<string>("PlaySound", path =>
        {
            if (path == "nothing")
            {
                Singleton.SoundPath = null;
                return true;
            }

            if (!File.Exists(path))
                return false;
            Singleton.Loop = true;
            Singleton.SoundPath = path;
            return true;
        });
    }


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
        Singleton = this;
        audioSource = GetComponent<AudioSource>();
        poll = StartCoroutine(PollPath());
        Repl.EnterDebug += StopSound;
    }

    private void StopSound() => SoundPath = null;

    [UsedImplicitly]
    private void OnDisable()
    {
        StopCoroutine(poll);
        Repl.EnterDebug -= StopSound;
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