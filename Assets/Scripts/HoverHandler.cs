using System.ComponentModel.Design;
using JetBrains.Annotations;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;

[RequireComponent(typeof(TextMeshProUGUI))]
public class HoverHandler : TextMouseHandler, IPointerEnterHandler, IPointerExitHandler
{
    // Can't detected whether pointer starts over us or not, but there's no harm to assuming it is
    private bool pointerOverText = true;
    private string currentlyChosenId;

    [UsedImplicitly]
    public void Update()
    {
        if (!pointerOverText)
            return;

        var text = GetComponent<TextMeshProUGUI>();
        int linkIndex = TMP_TextUtilities.FindIntersectingLink(text, Input.mousePosition, null);
        if (linkIndex != -1)
        {
            // They clicked a link
            var info = text.textInfo.linkInfo[linkIndex];
            var id = info.GetLinkID();
            if (currentlyChosenId != id)
            {
                currentlyChosenId = id;
                if (LinkTable.ContainsKey(id))
                    LinkTable[id].Invoke();
                //Debug.Log("Hover over "+id);
            }
        }
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        pointerOverText = true;
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        pointerOverText = false;
    }
}