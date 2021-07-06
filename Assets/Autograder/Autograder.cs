using System;
using System.IO;
using Step;
using Step.Interpreter;
using static Step.Interpreter.PrimitiveTask;

public static class Autograder
{
    public static void AddBuiltins()
    {
        var g = Repl.ReplUtilities;

        g["EraseMethods"] = NamePrimitive("EraseMethods",
            Predicate<CompoundTask>("EraseMethods", m =>
            {
                m.EraseMethods();
                return true;
            }));

        g["MakeModule"] = NamePrimitive("MakeModule", UnaryFunction<string, Module>("MakeModule",
            path =>
            {
                var m = new Module(Path.GetFileName(path), g);
                m.LoadDirectory(path);
                return m;
            }));

        g["LoadDirectory"] = NamePrimitive("LoadDirectory", Predicate<string, Module>("LoadDirectory",
            (path, m) =>
            {
                m.LoadDirectory(path);
                return true;
            }));

        g["LoadFile"]= NamePrimitive("LoadFile", Predicate<string, Module>("LoadFile",
            (path, m) =>
            {
                m.LoadDefinitions(path);
                return true;
            }));

        g["PathFileName"] =
            NamePrimitive("PathFileName", SimpleFunction<string, string>("PathFileName", Path.GetFileName));

        g["DirectoryFilePath"] =
            NamePrimitive("DirectoryFilePath", SimpleFunction<string, string, string>("DirectoryFilePath", Path.Combine));

        g["DirectoryFile"] = NamePrimitive("DirectoryFile", GeneralRelation<string, string>("DirectoryFile",
            (d,f) => Path.GetDirectoryName(f) == d,
            Directory.GetFiles, 
            f=> new [] { Path.GetDirectoryName(f) },
            null));

        g["DirectorySubdirectory"] = NamePrimitive("DirectorySubdirectory", GeneralRelation<string, string>("DirectorySubdirectory",
            (d, f) => Path.GetDirectoryName(f) == d,
            Directory.GetDirectories,
            f => new[] { Path.GetDirectoryName(f) },
            null));

        g["ProjectDirectory"] = NamePrimitive("SubdirectoryHere", SimpleFunction<string, string>("SubdirectoryHere", Repl.FindProject));

        g[nameof(CallInModule)] = NamePrimitive(nameof(CallInModule), (MetaTask) CallInModule);
        g[nameof(CallResult)] = NamePrimitive(nameof(CallResult), (MetaTask)CallResult);
        g["LookupGlobal"] = NamePrimitive("LookupGlobal",
            SimpleFunction<string[], Module, object>("LookupGlobal", 
                (name, module) => module[name[0]]));
        
        g["ParseSubmissionName"] = NamePrimitive("ParseSubmissionName",
            (NonDeterministicRelation) ((args, env) =>
            {
                ArgumentCountException.Check("ParseSubmissionName", 3, args);
                var path = ArgumentTypeException.Cast<string>("ParseSubmissionName", args[0], args);
                var fileName = Path.GetFileNameWithoutExtension(path);
                var elements = fileName.Split('_');
                if (elements.Length < 2)
                    throw new ArgumentException($"Invalid file name format: {fileName}");
                var student = elements[0];
                var id = elements[1];
                if (id == "LATE")
                    id = elements[2];
                if (env.UnifyArrays(args, new object[]{ path, student, id }, out BindingList<LogicVariable> bindings))
                    // Succeed once
                    return new[] { bindings };
                // Fail
                return new BindingList<LogicVariable>[0];
            }));
    }

    private static bool CallInModule(object[] args, TextBuffer output, BindingEnvironment env,
        Step.Interpreter.Step.Continuation k, MethodCallFrame predecessor)
    {
        ArgumentCountException.CheckAtLeast(nameof(CallInModule), 2, args);
        var call = ArgumentTypeException.Cast<object[]>(nameof(CallInModule), args[0], args);
        var module = ArgumentTypeException.Cast<Module>(nameof(CallInModule), args[1], args);

        var task = call[0] as CompoundTask;
        if (task == null)
            throw new InvalidOperationException(
                "Task argument to Call must be a compound task, i.e. a user-defined task with methods.");

        var taskArgs = new object[call.Length - 1 + args.Length - 2];

        var i = 0;
        for (var callIndex = 2; callIndex < call.Length; callIndex++)
            taskArgs[i++] = call[callIndex];
        for (var argsIndex = 2; argsIndex < args.Length; argsIndex++)
            taskArgs[i++] = args[argsIndex];

        return task.Call(taskArgs, output, new BindingEnvironment(module, env.Frame, env.Unifications, env.State), predecessor, k);
    }

    private static bool CallResult(object[] args, TextBuffer output, BindingEnvironment env,
        Step.Interpreter.Step.Continuation k, MethodCallFrame predecessor)
    {
        ArgumentCountException.Check(nameof(CallResult), 2, args);
        var call = ArgumentTypeException.Cast<object[]>(nameof(CallResult), args[0], args);

        // Kluge
        if (call.Length == 2 && call[0] == Module.Global["Not"])
            call = call[1] as object[];

        var task = call[0] as CompoundTask;
        if (task == null)
            throw new InvalidOperationException(
                "Task argument to Call must be a compound task, i.e. a user-defined task with methods.");

        var taskArgs = new object[call.Length - 1];

        var i = 0;
        for (var callIndex = 1; callIndex < call.Length; callIndex++)
            taskArgs[i++] = call[callIndex];

        object result;
        var newOutput = output;
        var unifications= env.Unifications;
        var state = env.State;
        // ReSharper disable once IdentifierTypo
        var pred = predecessor;

        try
        {
            result = task.Call(taskArgs, output, env, predecessor, (o, u, s, p) =>
            {
                newOutput = o;
                unifications = u;
                state = s;
                pred = p;
                return true;
            });
        }
        catch (Exception e)
        {
            result = e.Message;
        }
        
        return env.Unify(args[1], result, unifications, out var finalUnifications)
                && k(newOutput, finalUnifications, state, pred);
    }
}