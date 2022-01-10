using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;

[RequireComponent(typeof(TextMeshProUGUI))]
public class LinkHandler : MonoBehaviour, IPointerClickHandler
{
    private readonly Dictionary<string, StepCallback> linkTable = new Dictionary<string, StepCallback>();
    private int linkCounter;

    public string RegisterLink(StepCallback callback)
    {
        var link = linkCounter++.ToString();
        linkTable[link] = callback;
        return link;
    }

    public void DeregisterLink(string link)
    {
        linkTable.Remove(link);
    }

    public void OnPointerClick(PointerEventData clickEvent)
    {
        var text = GetComponent<TextMeshProUGUI>();
        int linkIndex = TMP_TextUtilities.FindIntersectingLink(text, clickEvent.position, null);
        if (linkIndex != -1)
        {
            // They clicked a link
            var info = text.textInfo.linkInfo[linkIndex];
            var id = info.GetLinkID();
            var t = info.GetLinkText();
            var callback = linkTable[id];
            callback.Invoke();
        }
    }
}