using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;

[RequireComponent(typeof(TextMeshProUGUI))]
public class LinkHandler : TextMouseHandler, IPointerClickHandler
{
    public void OnPointerClick(PointerEventData clickEvent)
    {
        var text = GetComponent<TextMeshProUGUI>();
        int linkIndex = TMP_TextUtilities.FindIntersectingLink(text, clickEvent.position, null);
        if (linkIndex != -1)
        {
            // They clicked a link
            var info = text.textInfo.linkInfo[linkIndex];
            var id = info.GetLinkID();
            var callback = LinkTable[id];
            callback.Invoke();
        }
    }
}