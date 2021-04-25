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
    private static CommandQueue queue = null;
    private Action curr=null;
    public void Start()
    {
        audioSource = GetComponent<AudioSource>();
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

    public static void rl()
    {
        SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
    }
    public static void next()
    {
        SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex+1);
    }
}