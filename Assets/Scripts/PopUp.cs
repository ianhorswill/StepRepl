using System;
using System.Collections;
using System.IO;
using JetBrains.Annotations;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI;
using System.Collections.Generic;
using System.Collections.Concurrent;
using UnityEngine.SceneManagement;
using TMPro;

public class PopUp : MonoBehaviour
{
    Button[] buttons;
    public Button bu;
    private void OnEnable()
    {
        transform.localScale = new Vector3(0, 0, 0);
        LeanTween.scale(this.gameObject, new Vector3(1, 1, 1), .5f);
    }
    public void CreateYesNo()
    {
        buttons = GetComponentsInChildren<Button>();
        foreach (Button b in buttons)
        {
            if (b.name.Contains("No"))
                b.onClick.AddListener(PressNo);
            else
                b.onClick.AddListener(PressYes);
        }
    }
    public void CreateMulti(String[] x)
    {
        GridLayoutGroup g = GetComponentInChildren<GridLayoutGroup>();
        
        foreach (String b in x)
        {

            Button z = Instantiate(bu, g.gameObject.transform);
            z.GetComponentInChildren<TextMeshProUGUI>().text = b;
            z.onClick.AddListener(() => Pressed(b));
        }
    }
    private void Close()
    {
        StepTask CurrentTask = Repl.CurrentTask;
        if (CurrentTask != null)
        {
            CurrentTask.Continue();
        }
        LeanTween.scale(this.gameObject, new Vector3(0, 0, 0), .5f).setOnComplete(DestroyMe);
    }

    private void DestroyMe()
    {
        Destroy(gameObject);
    }
    private void PressYes()
    {
        Debug.Log("Yes Pressed");
        CommandQueue.WhatIShouldDo = () => { return true; };
        Close();
    }
    private void PressNo()
    {
        Debug.Log("No Pressed");
        CommandQueue.WhatIShouldDo = () => { return false; };
        Close();
    }
    private void Pressed(String t)
    {
        Debug.Log(t+" Pressed");
        CommandQueue.WhatIShouldDo = () => { return t; };
        Close();
    }
}