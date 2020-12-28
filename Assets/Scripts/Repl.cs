using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Step;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using static Step.Interpreter.PrimitiveTask;
using TMPro;

public class Repl
    : MonoBehaviour
{
    public Module StepCode;
    public StepTask CurrentTask;

    public TMP_Text Text;
    public TMP_InputField Command;
    public string lastCommand = "";

    public Module ReplUtilities = null;

    // Start is called before the first frame update
    void Start()
    {
        Module.RichTextStackTraces = true;
        
        ReplUtilities = new Module("Repl utilities", Module.Global)
        {
            ["Pause"] = NamePrimitive("Pause", (MetaTask) ((args, o, bindings, k) =>
            {
                var t = StepTask.CurrentStepTask;
                t.Text = o.AsString;
                t.State = bindings.State;
                t.Pause(t.SingleStep);
                return k(o, bindings.Unifications, bindings.State);
            })),
            ["Break"] = NamePrimitive("Break", (MetaTask)((args, o, bindings, k) =>
            {
                var t = StepTask.CurrentStepTask;
                t.Text = o.AsString;
                t.State = bindings.State;
                t.Pause(true);
                return k(o, bindings.Unifications, bindings.State);
            }))
        };

        ReloadStepCode();
    }

    private void ReloadStepCode()
    {
        var sourceDirectory = Path.Combine(Application.dataPath, "Step code");
        var sourceFiles = Directory.GetFiles(sourceDirectory).Where(f => Path.GetExtension(f) == ".step").ToArray();
        try
        {
            StepCode = new Module("main", ReplUtilities, sourceFiles);
            Text.text = "<color=yellow>"+string.Join("\n", StepCode.Warnings())+"</color>";
        }
        catch (Exception e)
        {
            Text.text = $"<color=red>{e.Message}</color>";
        }

        Command.Select();
    }

    public void RunUserCommand()
    {
        AbortCurrentTask();
        var commandText = Command.text.Trim();
        var command = commandText == "" ? lastCommand : commandText;
        lastCommand = command;
        if (command == "")
            Command.Select();
        else
        {
            if (!command.StartsWith("["))
                command = $"[{command}]";
            StartCoroutine(RunStepCommand(command));
        }
    }

    private IEnumerator RunStepCommand(string code)
    {
        var task = CurrentTask = new StepTask(StepCode, false, code);
        var previouslyPaused = false;
        while (task.Active)
        {
            if (task.Paused)
            {
                // Only update the screen if the task has just paused as of this frame
                // Otherwise we're just recomputing the same data.
                if (!previouslyPaused)
                {
                    var t = new StringBuilder();
                    t.Append("<size=20>");
                    for (var b = Step.Interpreter.MethodCallFrame.CurrentFrame.BindingsAtCallTime; b != null; b = b.Next)
                        t.AppendFormat("{0} -> {1}  //  ", b.Variable.DebuggerName, b.Value);
                    t.Append("</size>");
                    if (task.ShowStackRequested)
                    {
                        t.Append("<color=orange><size=24>");
                        t.Append(Module.StackTrace);
                        t.Append("</size></color>\n");
                    }
                    t.Append(task.Text ?? "");
                    Text.text = t.ToString();
                    previouslyPaused = true;
                }

                if (Input.GetKeyDown(KeyCode.F10) || Input.GetKeyDown(KeyCode.F5))
                {
                    task.SingleStep = Input.GetKeyDown(KeyCode.F10);
                    task.Continue();
                    previouslyPaused = false;
                }
            }

            yield return null;
        }
        
        if (task.Exception == null)
        {
            Text.text = task.Text;
            Command.SetTextWithoutNotify("");
        }
        else
        {
            Text.text = $"<color=red>{task.Exception.Message}</color>\n\n<color=orange>{Module.StackTrace}</color>";
        }
        Command.Select();
    }

    void AbortCurrentTask()
    {
        if (CurrentTask == null)
            return;
        CurrentTask.Abort();
        CurrentTask = null;
    }

    void Update()
    {
        Thread.Sleep(20);
    }
}
