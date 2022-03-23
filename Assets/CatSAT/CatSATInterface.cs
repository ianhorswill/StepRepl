using System;
using System.Collections.Generic;
using System.Linq;
using Step;
using Step.Interpreter;
using CatSAT;
using Step.Utilities;
using UnityEditor.ShortcutManagement;
using static CatSAT.Language;

// ReSharper disable once InconsistentNaming
public static class CatSATInterface
{
    public static void AddBuiltins(Module m)
    {
        Documentation.SectionIntroduction("constraint solving", "This is an experimental wrapper for the CatSAT SMT solver.  The API may change over time.");
        Documentation.SectionIntroduction("constraint solving//assertions", "Tasks for permanently adding an assertion (constraint) to a SAT problem.  These are imperatives that destructively modify the SAT problem, and so cannot be backtracked.");
        Documentation.SectionIntroduction("constraint solving//assumptions", "Tasks for reversible add an assertion (constraint) to a SAT problem.  These are imperatives that destructively modify the SAT problem, and so cannot currently be backtracked.  However, all assumptions can be removed from a problem using ResetAssumptions.");

        m["SATProblem"] = new GeneralPredicate<object>(
            "SATProblem",
            o => o is Problem,
            () => new[] {new Problem()})
            .Arguments("?problem")
            .Documentation("constraint solving", "Places a new, empty, SAT problem in ?problem.");

        m["DefineProblem"] = new SimplePredicate<string[]>(
            "DefineProblem",
            name =>
            {
                var n = name[0];
                m[n] = new Problem(n);
                return true;
            })
            .Arguments("Name")
            .Documentation("constraint solving", "Defines the global variable, Name, to be a new, empty, constraint problem.");

        m[nameof(Assert)] = new SimpleNAryPredicate("Assert", Assert)
            .Arguments("problem", "conclusion", "<-", "premise", "...")
            .Documentation("constraint solving//assertions", "Adds an implication as a constraint to the problem");
        m["Unique"] = new SimpleNAryPredicate(
            "Unique",
            args =>
            {
                ArgumentCountException.CheckAtLeast("Unique", 2, args);
                var p = ArgumentTypeException.Cast<Problem>("Unique", args[0], args);
                Problem.Current = p;
                p.Unique(args.Skip(1).Select(TermToLiteral));
                return true;
            })
            .Arguments("problem", "alternatives", "...")
            .Documentation("constraint solving//assertions", "Adds to the problem the constraint that one and only one of the alternatives must be true.");

        m["Exists"] = new SimpleNAryPredicate(
            "Exists",
            args =>
            {
                ArgumentCountException.CheckAtLeast("Exists", 2, args);
                var p = ArgumentTypeException.Cast<Problem>("Exists", args[0], args);
                Problem.Current = p;
                p.Exists(args.Skip(1).Select(TermToLiteral));
                return true;
            })
            .Arguments("problem", "alternatives", "...")
            .Documentation("constraint solving//assertions", "Adds to the problem the constraint that at least one of the alternatives must be true.");

        m["All"] = new SimpleNAryPredicate(
            "All",
            args =>
            {
                ArgumentCountException.CheckAtLeast("All", 2, args);
                var p = ArgumentTypeException.Cast<Problem>("All", args[0], args);
                Problem.Current = p;
                p.All(args.Skip(1).Select(TermToLiteral));
                return true;
            })
            .Arguments("problem", "propositions", "...")
            .Documentation("constraint solving//assertions", "Adds to the problem the constraint that all of the propositions must be true.");

        m["Exactly"] = new SimpleNAryPredicate(
            "Exactly",
            args =>
            {
                ArgumentCountException.CheckAtLeast("Exactly", 3, args);
                var p = ArgumentTypeException.Cast<Problem>("Exactly", args[0], args);
                var count = ArgumentTypeException.Cast<int>("Exactly", args[1], args);
                Problem.Current = p;
                p.Exactly(count, args.Skip(2).Select(TermToLiteral));
                return true;
            })
            .Arguments("problem", "count", "alternatives", "...")
            .Documentation("constraint solving//assertions", "Adds to the problem the constraint that exactly count alternatives must be true.");

        m["AtLeast"] = new SimpleNAryPredicate(
            "AtLeast",
            args =>
            {
                ArgumentCountException.CheckAtLeast("AtLeast", 3, args);
                var p = ArgumentTypeException.Cast<Problem>("AtLeast", args[0], args);
                var count = ArgumentTypeException.Cast<int>("AtLeast", args[1], args);
                Problem.Current = p;
                p.AtLeast(count, args.Skip(2).Select(TermToLiteral));
                return true;
            }).Arguments("problem", "count", "alternatives", "...")
            .Documentation("constraint solving//assertions", "Adds to the problem the constraint that at least count alternatives must be true.");

        m["AtMost"] = new SimpleNAryPredicate(
            "AtMost",
            args =>
            {
                ArgumentCountException.CheckAtLeast("AtMost", 3, args);
                var p = ArgumentTypeException.Cast<Problem>("AtMost", args[0], args);
                var count = ArgumentTypeException.Cast<int>("AtMost", args[1], args);
                Problem.Current = p;
                p.AtMost(count, args.Skip(2).Select(TermToLiteral));
                return true;
            })
            .Arguments("problem", "count", "alternatives", "...")
            .Documentation("constraint solving//assertions", "Adds to the problem the constraint that at most count alternatives must be true.");

        m["Quantify"] = new SimpleNAryPredicate(
            "Quantify",
            args =>
            {
                ArgumentCountException.CheckAtLeast("Quantify", 4, args);
                var p = ArgumentTypeException.Cast<Problem>("Quantify", args[0], args);
                var min = ArgumentTypeException.Cast<int>("Quantify", args[1], args);
                var max = ArgumentTypeException.Cast<int>("Quantify", args[2], args);
                Problem.Current = p;
                p.Quantify(min, max, args.Skip(3).Select(TermToLiteral));
                return true;
            })
            .Arguments("problem", "min", "max", "alternatives", "...")
            .Documentation("constraint solving//assertions", "Adds to the problem the constraint that between min and max of the alternatives must be true.");

        m[nameof(DefinePredicate)] = new GeneralNAryPredicate(
            nameof(DefinePredicate),
            args => DefinePredicate(m, args))
            .Arguments("Name", "args", "...")
            .Documentation("constraint solving", "Specifies the global variable Name should be a constraint predicate with the specified arguments.");
        m["DefineSymmetricPredicate"] = new SimplePredicate<string[], string, string>(
            "DefineSymmetricPredicate",
            (name, arg1, arg2) =>
            {
                var n = name[0];
                m[n] = SymmetricPredicate<string>(n);
                return true;
            })
            .Arguments("Name", "arg1", "arg2")
            .Documentation("constraint solving", "Specifies the global variable Name should be a symmetric constraint predicate with the specified arguments.");

        m["Solution"] = new GeneralPredicate<Problem, PrimitiveTask>(
            "Solution",
            null,
            ProblemSolutions,
            null,
            null)
            .Arguments("problem", "?solution")
            .Documentation("constraint solving", "Attempts to generate a solution to problem and places it in ?solution.  Upon backtracking, generates new solutions (repetition is possible).  Only fails if solving fails.");

        m["SolveOnce"] = new GeneralPredicate<Problem, PrimitiveTask>(
            "SolveOnce",
            null,
            FirstProblemSolution,
            null,
            null)
            .Arguments("problem", "?solution")
            .Documentation("constraint solving", "Attempts to generate a solution to problem and places it in ?solution.  Succeeds only once: if that solution is rejected, the call fails.");

        m["ResetAssumptions"] = new SimplePredicate<Problem>(
            "ResetAssumptions",
            p =>
            {
                p.ResetPropositions();
                return true;
            })
            .Arguments("problem")
            .Documentation("constraint solving//assumptions", "Removes any assumptions from problem.");

        m["Assume"] = new SimplePredicate<Problem, object>(
            "Assume",
            (p, t) =>
            {
                Problem.Current = p;
                var lit = TermToLiteral(t);
                if (p.IsPredetermined(lit) && !p[lit])
                    return false;
                p[lit] = true;
                return true;
            })
            .Arguments("problem", "literal")
            .Documentation("constraint solving//assumptions", "Adds to the problem the constraint that literal be true.  This can be retracted by calling ResetAssumptions.");
    }

    private static IEnumerable<PrimitiveTask> ProblemSolutions(Problem p) => ProblemSolutions(p, true);
    private static IEnumerable<PrimitiveTask> FirstProblemSolution(Problem p) => ProblemSolutions(p, false);
    private static IEnumerable<PrimitiveTask> ProblemSolutions(Problem p, bool multiple)
    {
        Solution solution;
        do
        {
            solution = p.Solve(false);
            
            if (solution == null) 
                continue;
            
            var s = solution;
            yield return new SimplePredicate<object>(
                p.Name + "Solution",
                term => s[TermToLiteral(term)]);
        } while (multiple && solution != null);
    }

    private static IEnumerable<object[]> DefinePredicate(Module m, object[] args)
    {
        ArgumentCountException.CheckAtLeast(nameof(DefinePredicate), 2, args);
        var name = ArgumentTypeException.Cast<string[]>(nameof(DefinePredicate), args[0], args)[0];
        var nArgs = args.Length - 1;
        Delegate predicate ;
        switch (nArgs)
        {
            case 1:
                predicate = Predicate<object>(name);
                break;

            case 2:
                predicate = Predicate<object, object>(name);
                break;

            case 3:
                predicate = Predicate<object, object, object>(name);
                break;
            
            case 4:
                predicate = Predicate<object, object, object, object>(name);
                break;

            default:
                throw new ArgumentException("CatSAT predicates cannot have arities higher than 4");
        }

        m[name] = predicate;

        yield return args;
    }
    
    private static bool Assert(object[] args)
    {
        ArgumentCountException.CheckAtLeast(nameof(Assert), 4, args);
        
        var p = ArgumentTypeException.Cast<Problem>(nameof(Assert), args[0], args);
        Problem.Current = p;

        var implicationType = args[2];
        var conclusion = TermToLiteral(args[1]);
        var antecedents = Conjunction(args.Skip(3).ToArray());

        if (implicationType.Equals("<-"))
                p.Assert(antecedents > conclusion);
        else if (implicationType is PrimitiveTask pt && pt.Name == "<=")
        {
            var prop = conclusion as Proposition;
            if (ReferenceEquals(prop, null))
                throw new ArgumentException("Conclusion of <= rule must be a proposition, not a negation");
            p.Assert(prop <= antecedents);
        } else
                throw new ArgumentException(
                    $"Assert should be used to assert an implication using either <- or <=.  {implicationType} isn't a valid implication operator.");

        return true;
    }

    private static Expression Conjunction(object[] literals, int position = 0)
    {
        if (position == literals.Length - 1)
            return TermToLiteral(literals[position]);
        return TermToLiteral(literals[position]) & Conjunction(literals, position + 1);
    }

    private static Literal TermToLiteral(object term)
    {
        if (term is object[] tuple)
        {
            switch (tuple.Length)
            {
                case 0:
                    throw new ArgumentException("[] is not a valid predicate expression");

                case 2 when tuple[0].Equals("not"):
                    return Not(TermToLiteral(tuple[1]));

                default:
                    var p = tuple[0] as Delegate;
                    if (p == null)
                        throw new ArgumentException(
                            $"Invalid predicate in SAT proposition expression: {Writer.TermToString(tuple[0])}");
                    return (Proposition) p.DynamicInvoke(tuple.Skip(1).ToArray());
            }
        }
        else
        {
            return Proposition.MakeProposition(term);
        }
    }
}
