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

public class CommandQueue: MonoBehaviour
{
    private static ConcurrentQueue<Action> cQue = new ConcurrentQueue<Action>();
    private AudioSource audioSource;
    public static CommandQueue queue = null;
    private Action curr=null;
    public static Func<object> WhatIShouldDo;
    public GameObject popUp;
    public GameObject MultiUp;
    private Canvas canvas;
    public void Start()
    {
        audioSource = GetComponent<AudioSource>();
        canvas = FindObjectOfType<Canvas>();
        if (queue == null)
        {
            DontDestroyOnLoad(this.gameObject);
            queue = this;
        }
        else if (queue != this)
        {
            Destroy(this.gameObject);
        }
    }

  
    
    public void Update()
    {
        cQue.TryDequeue(out curr);
        
        if (curr!=null)
        {
            Debug.Log(curr); 
            curr.Invoke(); }
    }
    public static void Hit(Action i)
    {
        cQue.Enqueue(i);
    }

    public void pop()
    {
        Instantiate(popUp, canvas.transform).GetComponent<PopUp>().CreateYesNo();
    }
    public void pop(String[] a)
    {
        Instantiate(MultiUp, canvas.transform).GetComponent<PopUp>().CreateMulti(a);
    }

}