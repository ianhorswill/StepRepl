using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Step;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using Assets.Scripts;
using Assets.SION;
using JetBrains.Annotations;
using Step.Interpreter;
using Step.Utilities;
using TMPro;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.UI;

[UsedImplicitly]
public class Repl
    : MonoBehaviour
{
    public float PointSize = 24;
    public Transform ButtonBarContent;
    public GameObject ButtonPrefab;

    public static Repl CurrentRepl { get; private set; }

    public static bool RetainState;
    public static State ExecutionState = State.Empty;

    public static Action EnterDebug;

    static Repl()
    {
        Step.EnvironmentOption.Handler += (option, args) =>
        {
            CurrentRepl.EnvironmentOption(option, args);
        };

        MakeUtilitiesModule();
#if UNITY_EDITOR
        Documentation.WriteHtmlReference(ReplUtilities, "Reference manual.htm");
#endif
    }

    // ReSharper disable once UnusedParameter.Local
    private void EnvironmentOption(string option, object[] args)
    {
        switch (option)
        {
            case "retainState":
                RetainState = true;
                break;
        }
    }

    private static readonly string[] SearchPath = new[]
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

    public void SetPointSize(float size)
    {
        Command.pointSize = size; 
        OutputText.fontSize = size;
        DebugOutput.fontSize = size;
    }

    private string projectPath;
    public string ProjectPath
    {
        get
        {
            if (projectPath == null)
                projectPath = PlayerPrefs.GetString("CurrentProject");
            return projectPath;
        }
        set
        {
            projectPath = value;
            PlayerPrefs.SetString("CurrentProject", value);
        }
    }

    public Module ProjectModule;
    public StepTask CurrentTask;

    public bool TaskActive => CurrentTask != null && CurrentTask.Active;

    public TMP_InputField Command;
    
    public TMP_Text OutputText;
    public LinkHandler OutputLinkHandler;

    public TMP_Text DebugOutput;

    private string DebugText
    {
        get => DebugOutput.text;
        set
        {
            EnterDebug?.Invoke();
            DebugOutput.text = value;
        }
    }
    
    private string lastCommand = "";

    public static Module ReplUtilities;


    //public float DefaultScreenDpi = 96;

    // Start is called before the first frame update
    [UsedImplicitly]
    void Start()
    {
        SetPointSize(PointSize);
        CurrentRepl = this;
        continueButton = AddButton("Continue", () => CurrentRepl.ContinueTask());
        DisableContinue();
        Application.quitting += AbortCurrentTask;
        MethodCallFrame.MaxStackDepth = 500;
        Module.RichTextStackTraces = true;
        EnsureProjectDirectory();

        ReloadStepCode();
        SelectCommandBox();
    }

    private void SelectCommandBox()
    {
        var e = EventSystem.current;
        if (e.alreadySelecting)
            return;
        
        e.SetSelectedGameObject(Command.gameObject);
        Command.ActivateInputField();
    }

    private static void MakeUtilitiesModule()
    {
        Documentation.SectionIntroduction("StepRepl", "These tasks are defined by the StepRepl IDE.  To use them within a game not running inside StepRepl, you would need to copy their source into your game.");
        Documentation.SectionIntroduction("StepRepl//internals", "These are internal functions used by StepRepl.");
        Documentation.SectionIntroduction("StepRepl//display control", "Tasks that control how and when text is displayed on the screen.");
        Documentation.SectionIntroduction("StepRepl//profiling", "Tasks used to check how often other tasks are run.");
        Documentation.SectionIntroduction("StepRepl//user interaction", "Tasks used to allow user control of Step code.");

        ReplUtilities = new Module("Repl utilities", Module.Global)
        {
            ["PrintLocalBindings"] = new GeneralPrimitive("PrintLocalBindings",
                (args, o, bindings, p, k) =>
                {
                    ArgumentCountException.Check("PrintLocalBindings", 0, args);
                    var locals = bindings.Frame.Locals;
                    var output = new string[locals.Length * 4];
                    var index = 0;
                    foreach (var v in locals)
                    {
                        output[index++] = v.Name.Name;
                        output[index++] = "=";
                        var value = bindings.CopyTerm(v);
                        output[index++] = Writer.TermToString(value); //+$":{value.GetType().Name}";
                        output[index++] = TextUtilities.NewLineToken;
                    }

                    return k(o.Append(TextUtilities.FreshLineToken).Append(output), bindings.Unifications, bindings.State, p);
                })
                .Arguments()
                .Documentation("StepRepl//internals","Prints the values of all local variables.  There probably isn't any reason for you to use this directly, but it's used by StepRepl to print the results of queries."),

            ["SampleOutputText"] = new GeneralPrimitive("SampleOutputText",
                (args, o, bindings, p, k) =>
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
                })
                .Arguments()
                .Documentation("StepRepl//display control", "Update the screen with a snapshot of the current output, even if the program hasn't finished running yet.  This is used for testing code that is running something over and over again so you can see that it's still running."),

            ["EmptyCallSummary"] = new GeneralPredicate<Dictionary<CompoundTask, int>>("EmptyCallSummary",
                _ => false,
                () => new[] {new Dictionary<CompoundTask, int>()}
            )
                .Arguments("?summary")
                .Documentation("StepRepl//profiling", "Makes a call summary object that can be used with NoteCalledTasks to record what tasks have been called."),

            ["NoteCalledTasks"] = new GeneralPrimitive("NoteCalledTasks",
                (args, output, env, predecessor, k) =>
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
                })
                .Arguments("call_summary")
                .Documentation("StepRepl//profiling", "Adds all the tasks that were successfully executed on the path leading to this call to the specified call summary."),

            ["Pause"] = new GeneralPrimitive("Pause", (args, o, bindings, p, k) =>
            {
                ArgumentCountException.Check("Pause", 0, args);
                var t = StepTask.CurrentStepTask;
                t.Text = o.AsString;
                t.State = bindings.State;
                t.Pause(t.SingleStep);
                return k(o, bindings.Unifications, bindings.State, p);
            })
                .Arguments()
                .Documentation("StepRepl//user interaction", "Stops execution and displays the current call stack in the debugger."),

            ["Break"] = new GeneralPrimitive("Break", (args, o, bindings, p, k) =>
            {
                var t = StepTask.CurrentStepTask;
                if (args.Length > 0)
                    t.BreakMessage = ((string[]) args[0]).Untokenize();
                t.Text = o.AsString;
                t.State = bindings.State;
                t.Pause(true);
                t.BreakMessage = null;
                return k(o, bindings.Unifications, bindings.State, p);
            }).Arguments()
                .Documentation("debugging", "Stops execution and displays the current call stack in the debugger."),

            ["ClearOutput"] = new GeneralPrimitive("ClearOutput",
                // ReSharper disable once UnusedParameter.Local
                (args, o, bindings, p, k) =>
                    k(new TextBuffer(o.Buffer.Length), bindings.Unifications, bindings.State, p))
                .Arguments()
                .Documentation("StepRepl//display control", "Throws away any previously generated output"),

            ["HTMLTag"] = new DeterministicTextGenerator<string, object>("HTMLTag",
                (htmlTag, value) => new[] {$"<{htmlTag}=\"{value}\">"})
                .Arguments("tag_name", "value")
                .Documentation("StepRepl//user interaction", "Outputs the HTML tag: <tagName=value>"),

            ["Link"] = new GeneralPrimitive("Link",
                (args, o, e, p, k) =>
                {
                    var repl = Repl.CurrentRepl;
                    ArgumentCountException.Check("Link", 1, args);
                    var code = ArgumentTypeException.Cast<object[]>("Link", args[0], args);
                    if (code.Length == 0)
                        throw new ArgumentException("[] is not a valid task call for a link");
                    var task = code[0] as Task;
                    if (task == null)
                        throw new ArgumentException($"{code[0]} is not a value task to call in a link");
                    var state = e.State;
                    var callback = repl.MakeCallBack(e.Module, () => state, task, code.Skip(1).Select( e.CopyTerm).ToArray());
                    var link = repl.OutputLinkHandler.RegisterLink(callback);
                    var linkToken = $"<link=\"{link}\">";
                    if (k(o.Append(new []{ linkToken}), e.Unifications, e.State, p))
                        return true;
                    repl.OutputLinkHandler.DeregisterLink(link);
                    return false;
                })
                .Arguments("code")
                .Documentation("StepRepl//user interaction", "Starts a clickable link in the output.  When the link is click, the system will run the code.  End with [EndLink]"),

            ["EndLink"] = new DeterministicTextGenerator("EndLink", () => new []{ "</link>" })
                .Arguments()
                .Documentation("StepRepl//user interaction", "Ends a link started with [Link code]."),
        };

        void AddDocumentation(string taskName, string section, string docstring) =>
            ((Task) ReplUtilities[taskName]).Documentation(section, docstring);

        ReplUtilities.AddDefinitions(
            "predicate TestCase ?code.",
            "RunTestCases: [ForEach [TestCase ?call] [RunTestCase ?call]] All tests passed!",
            "RunTestCase ?call: Running ?call ... [Paragraph] [SampleOutputText] [Call ?call] [SampleOutputText]",
            "Test ?task ?testCount: [CountAttempts ?attempt] Test: ?attempt/Write [Paragraph] [Once ?task] [SampleOutputText] [= ?attempt ?testCount]",
            "Sample ?task ?testCount ?sampling: [EmptyCallSummary ?sampling] [CountAttempts ?attempt] Test: ?attempt [Paragraph] [Once ?task] [NoteCalledTasks ?sampling] [SampleOutputText] [= ?attempt ?testCount]",
            "Debug ?task: [Break \"Press F10 to run one step, F5 to finish execution without stopping.\"] [begin ?task]",
            "CallCounts ?task ?subTaskPredicate ?count: [IgnoreOutput [Sample ?task ?count ?s]] [ForEach [?subTaskPredicate ?t] [Write ?t] [Write \"<pos=400>\"] [DisplayCallCount ?s ?t ?count] [NewLine]]",
            "DisplayCallCount ?s ?t ?count: [?s ?t ?value] [set ?average = ?value/?count] [Write ?average]",
            "Uncalled ?task ?subTaskPredicate ?count: [IgnoreOutput [Sample ?task ?count ?s]] [ForEach [?subTaskPredicate ?t] [Write ?t] [Not [?s ?t ?value]] [Write ?t] [NewLine]]",
            "predicate HotKey ?key ?doc ?implementation.",
            "RunHotKey ?key: [firstOf] [HotKey ?key ? ?code] [else] [= ?code [UndefinedHotKey ?key]] [end] [firstOf] [Call ?code] [else] Command failed: ?code/Write [end]",
            "UndefinedHotKey ?key: ?key/Write is not a defined hot key.",
            "ShowHotKeys: <b>Key <indent=100> Function </indent></b> [NewLine] [ForEach [HotKey ?key ?doc ?] [WriteHotKeyDocs ?key ?doc]]",
            "WriteHotKeyDocs ?k ?d: Alt- ?k/Write <indent=100> ?d/Write </indent> [NewLine]",
            "[main] predicate Button ?label ?code.",
            "FindAllButtons ?buttonList: [FindAll [?label ?code] [Button ?label ?code] ?buttonList]"
        );

        Documentation.SectionIntroduction("StepRepl//testing", "Tools for unit-testing Step code.");
        
        AddDocumentation("TestCase", "StepRepl//testing", "(Defined by you).  Declares that code should be run when testing your program.");
        AddDocumentation("RunTestCases", "StepRepl//testing", "Runs all test cases defined by TestCase.");
        AddDocumentation("Test", "StepRepl//testing", "Runs ?task ?testCount times, showing its output each time");
        AddDocumentation("Sample", "StepRepl//profiling", "Runs ?task ?testCount times, and returns a sampling of the call stack in ?sampling.");
        AddDocumentation("CallCounts", "StepRepl//profiling", "Runs ?Task ?count times, then displays the counts of every subtask that satisfies ?subTaskPredicate.");
        AddDocumentation("Uncalled", "StepRepl//profiling", "Runs ?task ?count times, then displays every task satisfying ?subTaskPredicate that is never called.");
        AddDocumentation("HotKey", "StepRepl//user interaction", "(Defined by you).  Tells the system to run ?implementation when you press ?key.");
        AddDocumentation("ShowHotKeys", "StepRepl//user interaction", "Print all defined hot keys.");
        AddDocumentation("Button", "StepRepl//user interaction", "(Defined by you).  When a button labeled ?label is pressed, run ?code.");

        Autograder.AddBuiltins();
        SIONPrimitives.AddBuiltins(ReplUtilities);
        CatSATInterface.AddBuiltins(ReplUtilities);
        Json.AddBuiltins(ReplUtilities);
        SExpressionReader.AddBuiltins(ReplUtilities);
    }

    private void EnsureProjectDirectory()
    {
        var projectDirectoryPath = SearchPath[0];

        // I know the test is supposed to be redundant, but I've had issues with platform ports
        if (!Directory.Exists(projectDirectoryPath))
            Directory.CreateDirectory(projectDirectoryPath);
    }

    private void ReloadStepCode()
    {
        try
        {
            if (ButtonBarContent != null)
                ClearButtonBar();

            // Get rid of any HotKey bindings from before
            if (ReplUtilities["HotKey"] is CompoundTask hotKey)
            {
                hotKey.Methods.Clear();
                // Make sure it gets executed as a predicate
                hotKey.Declare(CompoundTask.TaskFlags.Fallible | CompoundTask.TaskFlags.MultipleSolutions);
            }

            // Get rid of any Button bindings
            if (ReplUtilities["Button"] is CompoundTask button)
            {
                button.Methods.Clear();
                // Make sure it gets executed as a predicate
                button.Declare(CompoundTask.TaskFlags.Fallible | CompoundTask.TaskFlags.MultipleSolutions);
            }

            ProjectModule = new Module("main", ReplUtilities)
            {
                FormattingOptions = {ParagraphMarker = "\n\n", LineSeparator = "<br>"}
            };

            string startUpProject = null;
#if UNITY_STANDALONE
#if UNITY_STANDALONE_OSX
            var startUp = Path.Combine(Application.dataPath, "Startup");
#else
            var startUp = Path.Combine(Path.GetDirectoryName(Application.dataPath) ?? throw new InvalidOperationException("Parent directory is null"), "Startup");
#endif
            if (Directory.Exists(startUp))
                startUpProject = startUp;
#endif

            if (startUpProject == null && string.IsNullOrEmpty(ProjectPath))
                DebugText =
                    "<color=red>No project selected.  Use \"project <i>projectName</i>\" to select your project directory.</color>";
            else
            {
                ProjectModule.LoadDirectory(startUpProject??ProjectPath, true);
                DeclareMainIfDefined("HotKey", "Author", "Description");
                var warnings = ProjectModule.Warnings().ToArray();
                DebugText = warnings.Length == 0
                    ? ""
                    : $"<color=yellow>I noticed some possible problems with the code.  They may be intentional, in which case, feel free to disregard them:\n\n{string.Join("\n", warnings)}</color>";
                ShowHelp(warnings.Length == 0);
            }
        }
        catch (Exception e)
        {
            DebugText = $"<color=red><b>{e.Message}</b></color>\n\n{Module.StackTrace()}\n\n<color=#808080>Funky internal debugging stuff for Ian:\n{e.StackTrace}</color>";
        }

        // Generate the buttons
        var buttonSpecs = ProjectModule.CallFunction<object[]>("FindAllButtons");
        foreach (object[] spec in buttonSpecs)
        {
            var label = spec[0] as string[];
            if (label == null)
            {
                if (spec[0] is object[] objectArray)
                    label = objectArray.Cast<string>().ToArray();
                else
                    throw new ArgumentException($"Label on button is not text: {Writer.TermToString(spec[0])}");
            }

            var stringLabel = label.Untokenize();
            var code = spec[1] as object[];
            if (code == null)
                throw new ArgumentException(
                    $"Invalid code {Writer.TermToString(spec[1])} to run for button {stringLabel}");
            AddButton(stringLabel, code);
        }
    }

    private void ClearButtonBar()
    {
        for (var i = ButtonBarContent.childCount - 1; i >= 0; i--)
        {
            var button = ButtonBarContent.GetChild(i).gameObject;
            if (button != continueButton)
                Destroy(button);
        }
    }

    private void AddButton(string label, object[] call)
    {
        if (call.Length == 0)
            throw new ArgumentException($"Expression to run in button {label} is the empty tuple");
        if (!(call[0] is Task task))
            throw new ArgumentException($"Task to call for button {label} is not a task: {call[0]}");
        var callback = MakeCallBack(task, call.Skip(1).ToArray());
        AddButton(label, () => { callback.Invoke(); });
    }

    private GameObject AddButton(string label, UnityAction pressHandler)
    {
        var button = Instantiate(ButtonPrefab, ButtonBarContent);
        button.name = label;
        button.GetComponentInChildren<TMP_Text>().text = label;
        button.GetComponentInChildren<Button>().onClick.AddListener(pressHandler);
        return button;
    }

    private GameObject continueButton;

    private void DisableContinue()
    {
        continueButton.SetActive(false);
        LayoutRebuilder.MarkLayoutForRebuild((RectTransform)Command.transform.parent.transform);
    }

    private void EnableContinue()
    {
        continueButton.SetActive(true);
        LayoutRebuilder.MarkLayoutForRebuild((RectTransform)Command.transform.parent.transform);
    }


    private void ShowHelp(bool showCommandKeys = true)
    {
        if (!TaskActive)
        {
            var description = ProjectModule.Defines("Description") ? ProjectModule.Call("Description") : "";
            var author = ProjectModule.Defines("Author") ? $"\nAuthor: {ProjectModule.Call("Author")}" : "";
            var keys = "";
            if (ReplUtilities["HotKey"] is CompoundTask hot && hot.Methods.Count > 0)
                keys = ProjectModule.Call("ShowHotKeys");
            OutputText.text = $"Project: <b>{Path.GetFileName(ProjectPath)}</b>{author}\n\n{description}\n\n{keys}";
        }

        if (showCommandKeys)
            DebugText =
                "<b>System command keys\n\nKey\t\tFunction</b>\nESC\t\tAbort running command\nPause\tInterrupt running command\nControl-R\tReload Step code\nF1\t\tHelp\nF4\t\tShow global state\nF5\t\tContinue execution\nF10\t\tStep over\nF11\t\tStep into\nShift-F11\tStep out";
    }

    private void DeclareMainIfDefined(params string[] tasks)
    {
        foreach (var taskName in tasks)
            if (ProjectModule.Defines(taskName))
            {
                if (ProjectModule[taskName] is CompoundTask task) 
                    task.Declare(CompoundTask.TaskFlags.Main);
            }
    }

    public static string FindProject(string projectName)
    {
        foreach (var collection in SearchPath)
        {
            var dir = Path.Combine(collection, projectName);
            if (Directory.Exists(dir))
                return dir;
        }

        return null;
    }

    public StepCallback MakeCallBack(Module module, Func<State> stateThunk, Task task, params object[] taskArguments) =>
        new StepCallback(this, module, stateThunk, task, taskArguments);

    public StepCallback MakeCallBack(Task task, params object[] taskArguments)
        => MakeCallBack(ProjectModule, () => ExecutionState, task, taskArguments);

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
                DebugText = $"<color=red>Can't find a project named {projectName}.</color>\n<color=#808080>I searched in:\n   \u2022  {string.Join("\n   \u2022  ", SearchPath)}</color>";
                return;
            }

            ProjectPath = path;
            ReloadStepCode();
            Command.text = "";
        }
        else
            switch (command)
            {
                case "":
                    SelectCommandBox();
                    break;

                case "reload":
                    ReloadStepCode();
                    OutputText.text = "";
                    DebugText = "Files reloaded\n\n"+DebugText;
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
                    RunStepCode(command);
                    break;
                }
            }
        SelectCommandBox();
    }

    public void RunStepCode(Module module, State state, Task task, params object[] args)
        => RunStepTask(new StepTask(module, false, () => module.Call(state, task, args)));

    private void RunStepCode(string code) => RunStepTask(new StepTask(ProjectModule, false, code, ExecutionState));

    public void RunStepTask(StepTask task)
    {
        CurrentTask = task;
        StartCoroutine(StepTaskDriver(task));
    }

    private bool continueTask;
    
    IEnumerator StepTaskDriver(StepTask task)
    {
        DisableContinue();
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
                    {
                        var method = MethodCallFrame.CurrentFrame.Method;
                        //var headString = method.HeadString;
                        //var callString = $"{headString.Substring(1, headString.Length-2)}: ..."; //<i>at {Path.GetFileName(method.FilePath)}:{method.LineNumber}</i>";
                        var callString = method.MethodCode;
                        switch (task.TraceEvent)
                        {
                            case Module.MethodTraceEvent.Enter:
                                breakMessage = $"<color=white>Enter method:</color> {callString}\n";
                                break;

                            case Module.MethodTraceEvent.Succeed:
                                breakMessage = $"<color=green>Method succeeded:</color> {callString}\n";
                                break;

                            case Module.MethodTraceEvent.MethodFail:
                                breakMessage = $"<color=red>Method failed:</color> {callString}\n";
                                break;

                            case Module.MethodTraceEvent.CallFail:
                                breakMessage = $"<color=red>Call failed:</color> {callString}\n";
                                break;
                        }
                    }

                    var bindings = task.TraceEvent == Module.MethodTraceEvent.None?null:task.Environment.Unifications;
                    DebugText = task.ShowStackRequested ?  $"{breakMessage}{Module.StackTrace(bindings)}" : "";

                    OutputText.text = task.Text ?? "";
                    //OutputText.caretPosition = task.Text.Length;
                    previouslyPaused = true;
                    EnableContinue();
                }

                var f10 = Input.GetKeyDown(KeyCode.F10);
                var f11 = Input.GetKeyDown(KeyCode.F11);
                var f5 = Input.GetKeyDown(KeyCode.F5);
                var shift = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
                
                if (continueTask || f10 || f11 || f5)
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
                    continueTask = false;
                    DisableContinue();
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
            DebugText = $"<color=red><b>{exceptionMessage}</b></color>\n\n{Module.StackTrace()}\n\n<color=#808080>Funky internal debugging stuff for Ian:\n{task.Exception.StackTrace}</color>";
        }

        CurrentTask = null;
        DisableContinue();
        //SelectCommandBox();
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
        switch (e.type)
        {
            case EventType.KeyDown:
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
                            SelectCommandBox();
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

                    case KeyCode.UpArrow:
                        if (e.control)
                            Command.text = lastCommand;
                        break;

                    case KeyCode.PageDown:
                        OutputText.pageToDisplay++;
                        DebugOutput.pageToDisplay++;
                        break;

                    case KeyCode.PageUp:
                        if (OutputText.pageToDisplay > 0)
                            OutputText.pageToDisplay--;
                        if (DebugOutput.pageToDisplay > 0)
                            DebugOutput.pageToDisplay--;
                        break;

                    default:
                        if (e.alt && keyCode != KeyCode.LeftAlt && keyCode != KeyCode.RightAlt)
                        {
                            DebugText = "";
                            RunStepCode($"[RunHotKey {keyCode.ToString().ToLowerInvariant()}]");
                        }

                        break;
                }
            }
                break;
        }
    }

    private void ContinueTask()
    {
        continueTask = true;  // This gets noticed by StepTaskDriver and then cleared
    }
}
