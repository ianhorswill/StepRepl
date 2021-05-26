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
using Step.Utilities;
using static Step.Interpreter.PrimitiveTask;
using TMPro;

[UsedImplicitly]
public class Repl
    : MonoBehaviour
{
    private readonly string[] searchPath = new[]
    {
        // NOTE: THIS MUST BE THE FIRST ELEMENT OF THE PATH!
        // EnsureProjectDirectory() relies on it!
#if UNITY_STANDALONE_OSX && !UNITY_EDITOR_WIN
        // This turns out to be a documented Mono issue.
        // See https://xamarin.github.io/bugzilla-archives/41/41258/bug.html
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)+"/Documents", "Step")
#else
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Step")
#endif
        ,
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "GitHub")
    };
    
    public string ProjectPath
    {
        get => PlayerPrefs.GetString("CurrentProject", null);
        set => PlayerPrefs.SetString("CurrentProject", value);
    }
    
    public Module StepCode;
    public StepTask CurrentTask;

    public bool TaskActive => CurrentTask != null && CurrentTask.Active;

    public TMP_InputField OutputText;
    public TMP_InputField Command;
    public TMP_InputField DebugOutput;
    public ImageController ImageController;
    public SoundController SoundController;

    private string DebugText
    {
        get => DebugOutput.text;
        set
        {
            ImageController.ImagePath = null;   // hide the image
            SoundController.SoundPath = null;   // stop the BGM
            DebugOutput.text = value;
        }
    }
    
    private string lastCommand = "";

    public Module ReplUtilities;

    private static readonly string[] ImageFileExtensions = {".jpg", ".jpeg", ".png", ".bmp", ".gif", ".tiff", "tiff"};
    private static readonly string[] SoundFileExtensions = { ".mp3", ".ogg", ".wav" };

    public float DefaultScreenDpi = 96;

    // Start is called before the first frame update
    [UsedImplicitly]
    void Start()
    {
        Application.quitting += AbortCurrentTask;
        MethodCallFrame.MaxStackDepth = 500;

        EnsureProjectDirectory();
        
        Module.RichTextStackTraces = true;
        
        ReplUtilities = new Module("Repl utilities", Module.Global)
        {
            ["PrintLocalBindings"] = NamePrimitive("PrintLocalBindings",
                (MetaTask)((args, o, bindings, k, p) =>
                {
                    ArgumentCountException.Check("PrintLocalBindings", 0, args);
                    var locals = bindings.Frame.Locals;
                    var output = new string[locals.Length * 4];
                    var index = 0;
                    foreach (var v in locals)
                    {
                        output[index++] = v.Name.Name;
                        output[index++] = "=";
                        output[index++] = Writer.TermToString(bindings.CopyTerm(v));
                        output[index++] = TextUtilities.NewLineToken;
                    }

                    return k(o.Append(output), bindings.Unifications, bindings.State, p);
                })),
            
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
            })),

            ["ClearOutput"] = NamePrimitive("ClearOutput",
                // ReSharper disable once UnusedParameter.Local
                (MetaTask)((args, o, bindings, k, p) => 
                    k(new TextBuffer(o.Buffer.Length), bindings.Unifications, bindings.State, p))),

            ["ImageHere"] = NamePrimitive("ImageHere", GeneralRelation<object,string>("ImageHere", null,
                // ReSharper disable once AssignNullToNotNullAttribute
                fileName =>
                {
                    string stringName;
                    switch (fileName)
                    {
                        case string[] tokens:
                            stringName = tokens.Untokenize(new FormattingOptions() {Capitalize = false});
                            break;

                        default:
                            stringName = fileName.ToString();
                            break;
                    }

                    var path = Path.Combine(Path.GetDirectoryName(MethodCallFrame.CurrentFrame.Method.FilePath),
                        stringName);

                    if (string.IsNullOrEmpty(Path.GetExtension(path)))
                        return ImageFileExtensions.Select(p => Path.ChangeExtension(path, p)).Where(File.Exists);
                    if (File.Exists(path))
                        return new[] {path};
                    return new string[0];
                },
                null,null)),
                    
            ["ShowImage"] = NamePrimitive("ShowImage", Predicate<string>("ShowImage", path =>
            {
                if (path == "nothing")
                {
                    ImageController.ImagePath = null;
                    return true;
                }
                if (!File.Exists(path))
                    return false;
                ImageController.ImagePath = path;
                return true;
            })),

            ["SoundHere"] = NamePrimitive("SoundHere", GeneralRelation<object, string>("SoundHere", null,
                // ReSharper disable once AssignNullToNotNullAttribute
                fileName =>
                {
                    string stringName;
                    switch (fileName)
                    {
                        case string[] tokens:
                            stringName = tokens.Untokenize(new FormattingOptions() { Capitalize = false });
                            break;

                        default:
                            stringName = fileName.ToString();
                            break;
                    }

                    var path = Path.Combine(Path.GetDirectoryName(MethodCallFrame.CurrentFrame.Method.FilePath),
                        stringName);

                    if (string.IsNullOrEmpty(Path.GetExtension(path)))
                        return SoundFileExtensions.Select(p => Path.ChangeExtension(path, p)).Where(File.Exists);
                    if (File.Exists(path))
                        return new[] { path };
                    return new string[0];
                },
                null, null)),


            ["PlaySound"] = NamePrimitive("PlaySound", Predicate<string>("PlaySound", path =>
            {
                if (path == "nothing")
                {
                    SoundController.SoundPath = null;
                    return true;
                }
                if (!File.Exists(path))
                    return false;
                SoundController.Loop = false;
                SoundController.SoundPath = path;
                return true;
            })),

            ["PlaySoundLoop"] = NamePrimitive("PlaySoundLoop", Predicate<string>("PlaySound", path =>
            {
                if (path == "nothing")
                {
                    SoundController.SoundPath = null;
                    return true;
                }
                if (!File.Exists(path))
                    return false;
                SoundController.Loop = true;
                SoundController.SoundPath = path;
                return true;
            }))
        };
        
        ReplUtilities.AddDefinitions(
            "predicate TestCase ?code.",
            "RunTestCases: [ForEach [TestCase ?call] [RunTestCase ?call]] All tests passed!",
            "RunTestCase ?call: Running ?call ... [Paragraph] [SampleOutputText] [Call ?call] [SampleOutputText]",
            "Test ?task ?testCount: [CountAttempts ?attempt] Test: ?attempt [Paragraph] [Once ?task] [SampleOutputText] [= ?attempt ?testCount]",
            "Sample ?task ?testCount ?sampling: [EmptyCallSummary ?sampling] [CountAttempts ?attempt] Test: ?attempt [Paragraph] [Once ?task] [NoteCalledTasks ?sampling] [SampleOutputText] [= ?attempt ?testCount]",
            "Debug ?task: [Break \"Press F10 to run one step, F5 to finish execution without stopping.\"] [begin ?task]",
            "CallCounts ?task ?subTaskPredicate ?count: [IgnoreOutput [Sample ?task ?count ?s]] [ForEach [?subTaskPredicate ?t] [Write ?t] [Write \"<pos=400>\"] [DisplayCallCount ?s ?t ?count] [NewLine]]",
            "DisplayCallCount ?s ?t ?count: [?s ?t ?value] [set ?average = ?value/?count] [Write ?average]",
            "Uncalled ?task ?subTaskPredicate ?count: [IgnoreOutput [Sample ?task ?count ?s]] [ForEach [?subTaskPredicate ?t] [Write ?t] [Not [?s ?t ?value]] [Write ?t] [NewLine]]",
            "predicate HotKey ?key ?doc ?implementation.",
            "RunHotKey ?key: [firstOf] [HotKey ?key ? ?code] [else] [= ?code [UndefinedHotKey ?key]] [end] [firstOf] [Call ?code] [else] Command failed: ?code/Write [end]",
            "UndefinedHotKey ?key: ?key/Write is not a defined hot key.",
            "ShowHotKeys: <b>Key <indent=100> Function </indent></b> [NewLine] [ForEach [HotKey ?key ?doc ?] [WriteHotKeyDocs ?key ?doc]]",
            "WriteHotKeyDocs ?k ?d: Alt- ?k/Write <indent=100> ?d/Write </indent> [NewLine]"
        );

        ReloadStepCode();
    }

    private void EnsureProjectDirectory()
    {
        var projectDirectoryPath = searchPath[0];

        // I know the test is supposed to be redundant, but I've had issues with platform ports
        if (!Directory.Exists(projectDirectoryPath))
            Directory.CreateDirectory(projectDirectoryPath);
    }

    private void ReloadStepCode()
    {
        try
        {
            if (ReplUtilities["HotKey"] is CompoundTask hotKey)
            {
                hotKey.Methods.Clear();
                // Make sure it gets executed as a predicate
                hotKey.Declare(CompoundTask.TaskFlags.Fallible | CompoundTask.TaskFlags.MultipleSolutions);
            }
            
            StepCode = new Module("main", ReplUtilities)
            {
                FormattingOptions = {ParagraphMarker = "\n\n", LineSeparator = "<br>"}
            };

            if (string.IsNullOrEmpty(ProjectPath))
                DebugText =
                    "<color=red>No project selected.  Use \"project <i>projectName</i>\" to select your project directory.</color>";
            else
            {
                StepCode.LoadDirectory(ProjectPath, true);
                DeclareMainIfDefined("HotKey", "Author", "Description");
                var warnings = StepCode.Warnings().ToArray();
                DebugText = warnings.Length == 0
                    ? ""
                    : $"<color=yellow>I noticed some possible problems with the code.  They may be intentional, in which case, feel free to disregard them:\n\n{string.Join("\n", warnings)}</color>";
                ShowHelp(warnings.Length == 0);
            }
        }
        catch (Exception e)
        {
            DebugText = $"<color=red>{e.Message}</color>";
        }

        //Command.Select();
    }

    private void ShowHelp(bool showCommandKeys = true)
    {
        if (!TaskActive)
        {
            var description = StepCode.Defines("Description") ? StepCode.Call("Description") : "";
            var author = StepCode.Defines("Author") ? $"\nAuthor: {StepCode.Call("Author")}" : "";
            var keys = "";
            if (ReplUtilities["HotKey"] is CompoundTask hot && hot.Methods.Count > 0)
                keys = StepCode.Call("ShowHotKeys");
            OutputText.text = $"Project: <b>{Path.GetFileName(ProjectPath)}</b>{author}\n\n{description}\n\n{keys}";
        }

        if (showCommandKeys)
            DebugText =
                "<size=24><b>System command keys\n\nKey\t\tFunction</b>\nESC\t\tAbort running command\nPause\tInterrupt running command\nControl-R\tReload Step code\nF1\t\tHelp\nF4\t\tShow global state\nF5\t\tContinue execution\nF10\t\tStep over\nF11\t\tStep into\nShift-F11\tStep out</size>";
    }

    private void DeclareMainIfDefined(params string[] tasks)
    {
        foreach (var taskName in tasks)
            if (StepCode.Defines(taskName))
            {
                if (StepCode[taskName] is CompoundTask task) 
                    task.Declare(CompoundTask.TaskFlags.Main);
            }
    }

    public string FindProject(string projectName)
    {
        foreach (var collection in searchPath)
        {
            var dir = Path.Combine(collection, projectName);
            if (Directory.Exists(dir))
                return dir;
        }

        return null;
    }

    [UsedImplicitly]
    public void RunReplCommand() => RunUserCommand();

    public void RunUserCommand(string commandText = null)
    {
        AbortCurrentTask();
        DebugText = "";
        OutputText.text = "";
        if (commandText == null)
        {
            commandText = Command.text.Trim();
            commandText = commandText.Replace("<b>", "").Replace("</b>", "");
        }

        var command = commandText == "" ? lastCommand : commandText;
        lastCommand = command;
        
        if (command.StartsWith("project "))
        {
            var projectName = command.Substring(command.IndexOf(' ')).Trim();
            var path = FindProject(projectName);
            if (path == null)
            {
                DebugText = $"<color=red>Can't find a project named {projectName}.</color>\n<color=#808080>I searched in:\n   \u2022  {string.Join("\n   \u2022  ", searchPath)}</color>";
                return;
            }

            ProjectPath = path;
            ReloadStepCode();
            Command.text = "";
            //Command.Select();
            
        }
        else
            switch (command)
            {
                case "":
                    //Command.Select();
                    break;

                case "reload":
                    ReloadStepCode();
                    OutputText.text = "";
                    DebugText = "Files reloaded\n\n"+DebugText;
                    //Command.Select();
                    Command.text = "";
                    break;

                case "quit":
                case "Quit":
                    Application.Quit();
                    break;
            
                default:
                {
                    if (!command.StartsWith("["))
                        command = $"[{command}]";
                    if (Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl))
                        command = $"[Test {command} 10000]";

                    command += "[PrintLocalBindings]";
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
                    DebugText = task.ShowStackRequested ?  $"{breakMessage}{Module.StackTrace}" : "";

                    OutputText.text = task.Text ?? "";
                    OutputText.caretPosition = task.Text.Length;
                    previouslyPaused = true;
                }

                var f10 = Input.GetKeyDown(KeyCode.F10);
                var f11 = Input.GetKeyDown(KeyCode.F11);
                var f5 = Input.GetKeyDown(KeyCode.F5);
                var shift = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
                
                if (f10 || f11 || f5)
                {
                    task.StepOverFrame = null;
                    if (f10 && task.TraceEvent != Module.MethodTraceEvent.Succeed)
                        task.StepOverFrame = MethodCallFrame.CurrentFrame.Caller;
                    else if (f11 && shift)
                        task.StepOverFrame = MethodCallFrame.CurrentFrame.Caller.Caller;
                    
                    task.SingleStep = f10 || f11;
                    if (!task.SingleStep)
                        DebugText = "";
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
            DebugText = $"<color=red><b>{exceptionMessage}</b></color>\n\n{Module.StackTrace}\n\n<color=#808080>Funky internal debugging stuff for Ian:\n{task.Exception.StackTrace}</color>";
        }

        CurrentTask = null;
        //Command.Select();
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
                b.AppendFormat("<b>{0}</b>: {1}\n\n", pair.Key, Writer.TermToString(pair.Value));
            }

            if (!gotOne)
                b.Append("State variables all have their default values");
            DebugText = b.ToString();
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
                    AbortCurrentTask();
                    break;

                case KeyCode.Pause:
                case KeyCode.Break:
                    if (CurrentTask != null)
                    {
                        CurrentTask.StepOverFrame = null;
                        CurrentTask.SingleStep = true;
                    }
                    break;

                case KeyCode.R:
                    if (e.control)
                    {
                        AbortCurrentTask();
                        ReloadStepCode();
                        DebugText = "Files reloaded\n\n" + DebugText;
                        //Command.Select();
                    }

                    break;

                case KeyCode.F1:
                case KeyCode.Help:
                    ShowHelp();
                    break;

                case KeyCode.F4:
                    ShowState();
                    break;

                case KeyCode.None:
                    break;

                default:
                    if (e.alt && keyCode != KeyCode.LeftAlt && keyCode != KeyCode.RightAlt)
                    {
                        DebugText = "";
                        StartCoroutine(RunStepCommand($"[RunHotKey {keyCode.ToString().ToLowerInvariant()}]"));
                    }
                    break;
            }
        }
    }
}
