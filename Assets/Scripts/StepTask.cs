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

    public StepTask(Module m, bool singleStep, string code)
    {
        Module = m;
        SingleStep = singleStep;

        thread = new Thread(() =>
        {
            CurrentStepTask = this;
            try
            {
                Text = m.ParseAndExecute(code);
            }
            catch (Exception e)
            {
                Exception = e;
                Text = null;
            }
        }, 4000000);
        thread.Start();
    }

    public void Continue()
    {
#pragma warning disable CS0618 // Type or member is obsolete
        thread.Resume();
#pragma warning restore CS0618 // Type or member is obsolete
    }
    
    public void SingleStepTraceHandler(Step.Module.MethodTraceEvent e, Method method, object[] args, TextBuffer output, BindingEnvironment env)
    {
        if (StepOverFrame == null 
            || (StepOverFrame == MethodCallFrame.CurrentFrame.Caller && (e == Module.MethodTraceEvent.Succeed || e == Module.MethodTraceEvent.CallFail))
            || (StepOverFrame.Caller == MethodCallFrame.CurrentFrame))
        {
            TraceEvent = e;
            Text = output.AsString;
            State = env.State;
            Pause(true);
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
