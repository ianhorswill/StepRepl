using System.Collections;
using System.IO;
using JetBrains.Annotations;
using UnityEngine;
using UnityEngine.UI;

public class ImageController : MonoBehaviour
{
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
        image = GetComponent<Image>();
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