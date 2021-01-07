using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Step;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using JetBrains.Annotations;
using Step.Interpreter;
using static Step.Interpreter.PrimitiveTask;
using TMPro;

[UsedImplicitly]
public class Repl
    : MonoBehaviour
{
    private string[] SearchPath = new[]
    {
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Step"),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "GitHub")
    };
    
    public string ProjectPath
    {
        get => PlayerPrefs.GetString("CurrentProject", null);
        set => PlayerPrefs.SetString("CurrentProject", value);
    }
    
    public Module StepCode;
    public StepTask CurrentTask;

    public TMP_InputField OutputText;
    public TMP_InputField Command;
    public TMP_InputField DebugOutput;

    private string lastCommand = "";

    public Module ReplUtilities;

    // Start is called before the first frame update
    [UsedImplicitly]
    void Start()
    {
        Application.quitting += AbortCurrentTask;
        
        Module.RichTextStackTraces = true;
        
        ReplUtilities = new Module("Repl utilities", Module.Global)
        {
            ["SampleOutputText"] = NamePrimitive("SampleOutputText",
                (MetaTask)((args, o, bindings, k, p) =>
                {
                    ArgumentCountException.Check("SampleOutputText", 0, args);
                    var t = StepTask.CurrentStepTask;
                    // Don't generate another sample if the last one hasn't been displayed yet.
                    if (!t.NewSample)
                    {
                        t.Text = o.AsString;
                        t.State = bindings.State;
                        t.NewSample = true;
                    }

                    return k(o, bindings.Unifications, bindings.State, p);
                })),
            
            ["EmptyCallSummary"] = GeneralRelation("EmptyCallSummary",
                _ => false,
                () => new [] { new Dictionary<CompoundTask, int>() }
                    ),
            
            ["NoteCalledTasks"] = NamePrimitive("NoteCalledTasks", 
                    (MetaTask) ((args, output, env, k, predecessor) =>
                    {
                        ArgumentCountException.Check("NoteCalledTasks", 1, args);
                        var callSummary =
                            ArgumentTypeException.Cast<Dictionary<CompoundTask, int>>("NoteCalledTasks", args[0], args);
                        foreach (var frame in predecessor.GoalChain)
                        {
                            var task = frame.Method.Task;
                            callSummary.TryGetValue(task, out var previousCount);
                            callSummary[task] = previousCount + 1;
                        }

                        return k(output, env.Unifications, env.State, predecessor);
                    })),

            ["Pause"] = NamePrimitive("Pause", (MetaTask) ((args, o, bindings, k, p) =>
            {
                ArgumentCountException.Check("Pause", 0, args);
                var t = StepTask.CurrentStepTask;
                t.Text = o.AsString;
                t.State = bindings.State;
                t.Pause(t.SingleStep);
                return k(o, bindings.Unifications, bindings.State, p);
            })),
            
            ["Break"] = NamePrimitive("Break", (MetaTask)((args, o, bindings, k, p) =>
            {
                var t = StepTask.CurrentStepTask;
                if (args.Length > 0)
                    t.BreakMessage = ((string[])args[0]).Untokenize();
                t.Text = o.AsString;
                t.State = bindings.State;
                t.Pause(true);
                t.BreakMessage = null;
                return k(o, bindings.Unifications, bindings.State, p);
            }))
        };
        
        ReplUtilities.AddDefinitions(
            "Test ?task ?testCount: [CountAttempts ?attempt] Test: ?attempt [Paragraph] [Once ?task] [SampleOutputText] [= ?attempt ?testCount]",
            "Sample ?task ?testCount ?sampling: [EmptyCallSummary ?sampling] [CountAttempts ?attempt] Test: ?attempt [Paragraph] [Once ?task] [NoteCalledTasks ?sampling] [SampleOutputText] [= ?attempt ?testCount]",
            "Debug ?task: [Break \"Press F10 to run one step, F5 to finish execution without stopping.\"] [begin ?task]",
            "CallCounts ?task ?subTaskPredicate ?count: [IgnoreOutput [Sample ?task ?count ?s]] [ForEach [?subTaskPredicate ?t] [Write ?t] [?s ?t ?value] [Write ?value] [NewLine]]",
            "Uncalled ?task ?subTaskPredicate ?count: [IgnoreOutput [Sample ?task ?count ?s]] [ForEach [?subTaskPredicate ?t] [Write ?t] [Not [?s ?t ?value]] [Write ?t] [NewLine]]");

        ReloadStepCode();
    }

    private void ReloadStepCode()
    {
        try
        {
            StepCode = new Module("main", ReplUtilities)
            {
                FormattingOptions = {ParagraphMarker = "\n\n", LineSeparator = "<br>"}
            };

            if (string.IsNullOrEmpty(ProjectPath))
                DebugOutput.text =
                    "<color=red>No project selected.  Use \"project <i>projectName</i>\" to select your project directory.</color>";
            else
            {
                StepCode.LoadDirectory(ProjectPath, true);
                // This is just to make sure the system sees HotKey as being a main routine.
                StepCode.AddDefinitions("[main] HotKey ?: [Fail]");
                var warnings = StepCode.Warnings().ToArray();
                DebugOutput.text = warnings.Length == 0
                    ? ""
                    : $"<color=yellow>I noticed some possible problems with the code.  They may be intentional, in which case, feel free to disregard them:\n\n{string.Join("\n", warnings)}</color>";
            }
        }
        catch (Exception e)
        {
            DebugOutput.text = $"<color=red>{e.Message}</color>";
        }

        Command.Select();
    }

    public string FindProject(string projectName)
    {
        foreach (var collection in SearchPath)
        {
            var dir = Path.Combine(collection, projectName);
            if (Directory.Exists(dir))
                return dir;
        }

        return null;
    }

    [UsedImplicitly]
    public void RunUserCommand()
    {
        AbortCurrentTask();
        DebugOutput.text = "";
        var commandText = Command.text.Trim();
        var command = commandText == "" ? lastCommand : commandText;
        lastCommand = command;
        
        if (command.StartsWith("project "))
        {
            var projectName = command.Substring(command.IndexOf(' ')).Trim();
            var path = FindProject(projectName);
            if (path == null)
            {
                DebugOutput.text = $"<color=red>Can't find a project named {projectName}.</color>";
                return;
            }

            ProjectPath = path;
            ReloadStepCode();
            Command.text = "";
            Command.Select();
            
        }
        else
            switch (command)
            {
                case "":
                    Command.Select();
                    break;

                case "reload":
                    ReloadStepCode();
                    OutputText.text = "";
                    DebugOutput.text = "Files reloaded\n\n"+DebugOutput.text;
                    Command.Select();
                    Command.text = "";
                    break;
            
                default:
                {
                    if (!command.StartsWith("["))
                        command = $"[{command}]";
                    StartCoroutine(RunStepCommand(command));
                    break;
                }
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
                    //t.Append("<size=20>");
                    //for (var b = Step.Interpreter.MethodCallFrame.CurrentFrame.BindingsAtCallTime; b != null; b = b.Next)
                    //    t.AppendFormat("{0} -> {1}  //  ", b.Variable.DebuggerName, b.Value);
                    //t.Append("</size>");
                    var breakMessage =
                        task.BreakMessage!=null ? $"<color=red><b>{task.BreakMessage}</b></color>\n\n" : "";
                    if (breakMessage == "")
                        switch (task.TraceEvent)
                        {
                            case Module.MethodTraceEvent.Enter:
                                breakMessage = $"<color=white>Enter method:</color> {MethodCallFrame.CurrentFrame.Method.HeadString}\n";
                                break;

                            case Module.MethodTraceEvent.Succeed:
                                breakMessage = $"<color=green>Method succeeded:</color> {MethodCallFrame.CurrentFrame.Method.HeadString}\n";
                                break;

                            case Module.MethodTraceEvent.MethodFail:
                                breakMessage = $"<color=red>Method failed:</color> {MethodCallFrame.CurrentFrame.Method.HeadString}\n";
                                break;

                            case Module.MethodTraceEvent.CallFail:
                                breakMessage = $"<color=red>Call failed:</color> {MethodCallFrame.CurrentFrame.Method.HeadString}\n";
                                break;
                        }
                    DebugOutput.text = task.ShowStackRequested ?  $"{breakMessage}{Module.StackTrace}" : "";

                    OutputText.text = task.Text ?? "";
                    previouslyPaused = true;
                }

                if (Input.GetKeyDown(KeyCode.F10) || Input.GetKeyDown(KeyCode.F11) || Input.GetKeyDown(KeyCode.F5))
                {
                    task.StepOverFrame = (Input.GetKeyDown(KeyCode.F10) && task.TraceEvent != Module.MethodTraceEvent.Succeed) ? MethodCallFrame.CurrentFrame.Caller : null;
                    task.SingleStep = Input.GetKeyDown(KeyCode.F10) || Input.GetKeyDown(KeyCode.F11);
                    task.Continue();
                    previouslyPaused = false;
                }
            }
            else if (task.NewSample)
            {
                OutputText.text = task.Text;
                task.NewSample = false;
            }

            yield return null;
        }
        
        if (task.Exception == null)
        {
            OutputText.text = task.Text;
            DebugOutput.text = "";
            Command.SetTextWithoutNotify("");
        }
        else
        {
            var exceptionMessage = (task.Exception is ThreadAbortException )? "Aborted" : task.Exception.Message;
            DebugOutput.text = $"<color=red><b>{exceptionMessage}</b></color>\n\n{Module.StackTrace}\n\n<color=#808080>Funky internal debugging stuff for Ian:\n{task.Exception.StackTrace}</color>";
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

    void ShowState()
    {
        if (CurrentTask != null)
        {
            var b = new StringBuilder();
            var gotOne = false;
            foreach (var pair in CurrentTask.State.Contents)
            {
                gotOne = true;
                b.AppendFormat("<b>{0}</b>: {1}\n\n", pair.Key, Step.Utilities.Writer.TermToString(pair.Value));
            }

            if (!gotOne)
                b.Append("State variables all have their default values");
            DebugOutput.text = b.ToString();
        }
    }

    [UsedImplicitly]
    void Update()
    {
        Thread.Sleep(50);
    }

    [UsedImplicitly]
    void OnGUI()
    {
        var e = Event.current;
        if (e.type == EventType.KeyDown)
        {
            var keyCode = e.keyCode;
            switch (keyCode)
            {
                case KeyCode.Escape:
                case KeyCode.Break:
                    AbortCurrentTask();
                    break;

                case KeyCode.R:
                    if (e.control)
                    {
                        AbortCurrentTask();
                        ReloadStepCode();
                        OutputText.text = "";
                        DebugOutput.text = "Files reloaded\n\n" + DebugOutput.text;
                        Command.Select();
                    }

                    break;

                case KeyCode.F4:
                    ShowState();
                    break;

                default:
                    if (e.alt && keyCode != KeyCode.LeftAlt && keyCode != KeyCode.RightAlt)
                        StartCoroutine(RunStepCommand($"[HotKey {keyCode.ToString().ToLowerInvariant()}]"));
                    break;
            }
        }
    }
}
