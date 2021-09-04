using System;
using System.Threading;
using Step;
using Step.Interpreter;

public class StepTask
{
    private readonly Thread thread;
    public readonly Module Module;

    [ThreadStatic] public static StepTask CurrentStepTask;

    private ThreadState ThreadState => thread.ThreadState;
    public string Text { get; set; }

    public BindingEnvironment Environment { get; private set; }
    public State State { get; set; }
    public Exception Exception { get; private set; }

    public Module.MethodTraceEvent TraceEvent { get; private set; }

    public bool Completed => ThreadState == ThreadState.Stopped;
    public bool Paused => ThreadState == ThreadState.Suspended;
    
    public bool Active => ThreadState == ThreadState.Running || ThreadState == ThreadState.Suspended;

    public bool ShowStackRequested;

    public bool NewSample;

    public string BreakMessage;

    public bool SingleStep
    {
        get => Module.Trace != null;
        set
        {
            if (value != SingleStep)
                Module.Trace = value ? SingleStepTraceHandler : (Module.TraceHandler)null;
        }
    }

    public MethodCallFrame StepOverFrame;

    public delegate (string output, State outState) StepInvocation();

    public StepTask(Module m, bool singleStep, string code, State state)
        : this(m, singleStep, () => m.ParseAndExecute(code, state))
    { }

    public StepTask(Module m, bool singleStep, StepInvocation start)
    {
        MethodCallFrame.MaxStackDepth = 1000;
        Module = m;
        SingleStep = singleStep;

        thread = new Thread(() =>
        {
            CurrentStepTask = this;
            try
            {
                var (output, newState) = start();
                Text = output;
                if (Repl.RetainState)
                    Repl.ExecutionState = newState;
            }
            catch (Exception e)
            {
                Exception = e;
                Text = null;
            }
        }, 8000000);
        thread.Start();
    }

    public void Continue()
    {
#pragma warning disable CS0618 // Type or member is obsolete
        thread.Resume();
#pragma warning restore CS0618 // Type or member is obsolete
    }
    
    public void SingleStepTraceHandler(Module.MethodTraceEvent e, Method method, object[] args, TextBuffer output, BindingEnvironment env)
    {
        if (StepOverFrame == null
            || (StepOverFrame.StackDepth >= MethodCallFrame.CurrentFrame.Caller.StackDepth && (e == Module.MethodTraceEvent.Succeed || e == Module.MethodTraceEvent.CallFail)))
        {
            TraceEvent = e;
            Text = output.AsString;
            State = env.State;
            Environment = env;
            Pause(true);
            TraceEvent = Module.MethodTraceEvent.None;
        }
    }

    public void Pause(bool showStack = false)
    {
        ShowStackRequested = showStack;
#pragma warning disable CS0618 // Type or member is obsolete
        Thread.CurrentThread.Suspend();
#pragma warning restore CS0618 // Type or member is obsolete
    }

    public void Abort()
    {
        thread.Abort();
    }
}
