using System.Collections;
using System.IO;
using System.Linq;
using JetBrains.Annotations;
using Step;
using Step.Interpreter;
using UnityEngine;
using UnityEngine.UI;

public class ImageController : MonoBehaviour
{
    public static ImageController Singleton;
    
    private static readonly string[] ImageFileExtensions = { ".jpg", ".jpeg", ".png", ".bmp", ".gif", ".tiff", "tiff" };

    static ImageController()
    {
        Module.Global["ImageHere"] = new GeneralPredicate<object, string>("ImageHere", null,
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
                    return ImageFileExtensions.Select(p => Path.ChangeExtension(path, p)).Where(File.Exists);
                if (File.Exists(path))
                    return new[] {path};
                return new string[0];
            },
            null, null);
        Module.Global["ShowImage"] = new SimplePredicate<string>("ShowImage", path =>
        {
            if (path == "nothing")
            {
                Singleton.ImagePath = null;
                return true;
            }

            if (!File.Exists(path))
                return false;
            Singleton.ImagePath = path;
            return true;
        });


    }

    private Image image;
    private string path;
    private bool pathChanged;

    public string ImagePath
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
        image = GetComponent<Image>();
        poll = StartCoroutine(PollPath());
        Repl.EnterDebug = EnterDebug;
    }

    [UsedImplicitly]
    private void EnterDebug()
    {
        ImagePath = null;
    }

    [UsedImplicitly]
    private void OnDisable()
    {
        StopCoroutine(poll);
        Repl.EnterDebug -= EnterDebug;
    }

    private IEnumerator PollPath()
    {
        while (true)
        {
            yield return new WaitUntil(() => pathChanged);
            pathChanged = false;
            if (path == null)
            {
                image.sprite = null;
                image.enabled = false;
            }
            else if (File.Exists(path))
            {
                image.sprite = ReadSpriteFromFile(path);
                image.enabled = true;
                image.preserveAspect = true;
            }
            else
                Debug.LogError("No such image file: " + path);
        }

        // ReSharper disable once IteratorNeverReturns
    }

    private Sprite ReadSpriteFromFile(string imagePath)
    {
        var texture = new Texture2D(2048, 2048);
        texture.LoadImage(File.ReadAllBytes(imagePath));
        return Sprite.Create(texture,
            new Rect(0, 0, texture.width, texture.height),
            Vector2.zero);
    }
}