using System.Collections.Generic;
using TMPro;
using UnityEngine;

[RequireComponent(typeof(TextMeshProUGUI))]
public class TextMouseHandler : MonoBehaviour
{
    protected readonly Dictionary<string, StepCallback> LinkTable = new Dictionary<string, StepCallback>();
    protected int LinkCounter;

    public string RegisterLink(StepCallback callback)
    {
        var link = LinkCounter++.ToString();
        LinkTable[link] = callback;
        return link;
    }

    public void DeregisterLink(string link)
    {
        //linkTable.Remove(link);
    }
}