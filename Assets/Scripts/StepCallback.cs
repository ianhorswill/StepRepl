using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Step;
using Task = Step.Interpreter.Task;

public class StepCallback
{
    public readonly Repl Repl;
    public readonly Module Module;
    public readonly Func<State> StateThunk;
    public readonly Task Task;
    public readonly object[] TaskArguments;

    public StepCallback(Repl repl, Module module, Func<State> stateThunk, Task task, object[] taskArguments)
    {
        Repl = repl;
        Module = module;
        StateThunk = stateThunk;
        Task = task;
        TaskArguments = taskArguments;
    }

    public void Invoke()
    {
        Repl.RunStepCode(Module, StateThunk(), Task, TaskArguments);
    }
}