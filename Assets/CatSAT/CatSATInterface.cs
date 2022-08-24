using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using CatSAT;
using Step;
using Step.Interpreter;
using Step.Output;
using Step.Utilities;
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
            .Arguments("conclusion", "<-", "premise", "...")
            .Documentation("constraint solving//assertions", "Adds an implication as a constraint to the current problem");
        m["Unique"] = new SimpleNAryPredicate(
            "Unique",
            args =>
            {
                ArgumentCountException.CheckAtLeast("Unique", 1, args);
                Problem.Current.Unique(TermListToLiteralList(args));
                return true;
            })
            .Arguments("alternatives", "...")
            .Documentation("constraint solving//assertions", "Adds to the current problem the constraint that one and only one of the alternatives must be true.");

        m["Exists"] = new SimpleNAryPredicate(
            "Exists",
            args =>
            {
                ArgumentCountException.CheckAtLeast("Exists", 1, args);
                Problem.Current.Exists(TermListToLiteralList(args));
                return true;
            })
            .Arguments("alternatives", "...")
            .Documentation("constraint solving//assertions", "Adds to the current problem the constraint that at least one of the alternatives must be true.");

        m["All"] = new SimpleNAryPredicate(
            "All",
            args =>
            {
                ArgumentCountException.CheckAtLeast("All", 1, args);
                Problem.Current.All(TermListToLiteralList(args));
                return true;
            })
            .Arguments("propositions", "...")
            .Documentation("constraint solving//assertions", "Adds to the problem the constraint that all of the propositions must be true.");

        m["Exactly"] = new SimpleNAryPredicate(
            "Exactly",
            args =>
            {
                ArgumentCountException.CheckAtLeast("Exactly", 2, args);
                var count = ArgumentTypeException.Cast<int>("Exactly", args[0], args);
                Problem.Current.Exactly(count, TermListToLiteralList(args.Skip(1)));
                return true;
            })
            .Arguments("count", "alternatives", "...")
            .Documentation("constraint solving//assertions", "Adds to the current problem the constraint that exactly count alternatives must be true.");

        m["AtLeast"] = new SimpleNAryPredicate(
            "AtLeast",
            args =>
            {
                ArgumentCountException.CheckAtLeast("AtLeast", 2, args);
                var count = ArgumentTypeException.Cast<int>("AtLeast", args[0], args);
                Problem.Current.AtLeast(count, TermListToLiteralList(args.Skip(1)));
                return true;
            }).Arguments("count", "alternatives", "...")
            .Documentation("constraint solving//assertions", "Adds to the current problem the constraint that at least count alternatives must be true.");

        m["AtMost"] = new SimpleNAryPredicate(
            "AtMost",
            args =>
            {
                ArgumentCountException.CheckAtLeast("AtMost", 2, args);
                var count = ArgumentTypeException.Cast<int>("AtMost", args[0], args);
                Problem.Current.AtMost(count, TermListToLiteralList(args.Skip(1)));
                return true;
            })
            .Arguments("count", "alternatives", "...")
            .Documentation("constraint solving//assertions", "Adds to the current problem the constraint that at most count alternatives must be true.");

        m["Quantify"] = new SimpleNAryPredicate(
            "Quantify",
            args =>
            {
                ArgumentCountException.CheckAtLeast("Quantify", 3, args);
                var min = ArgumentTypeException.Cast<int>("Quantify", args[0], args);
                var max = ArgumentTypeException.Cast<int>("Quantify", args[1], args);
                Problem.Current.Quantify(min, max, TermListToLiteralList(args.Skip(2)));
                return true;
            })
            .Arguments("problem", "min", "max", "alternatives", "...")
            .Documentation("constraint solving//assertions", "Adds to the problem the constraint that between min and max of the alternatives must be true.");

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

        m["AssumeAll"] = new SimplePredicate<Problem, IList<object>>(
                "AssumeAll",
                (p, l) =>
                {
                    Problem.Current = p;
                    foreach (var t in l)
                    {
                        var lit = TermToLiteral(t);
                        if (p.IsPredetermined(lit) && !p[lit])
                            return false;
                        p[lit] = true;
                    }

                    return true;
                })
            .Arguments("problem", "literal_list")
            .Documentation("constraint solving//assumptions", "Adds to the problem the constraint that all the literals in the list be true.  This can be retracted by calling ResetAssumptions.");
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

    private static bool Assert(object[] args)
    {
        if (args.Length == 1)
            Problem.Current.Assert(TermToLiteral(args[0]));
        else
        {
            ArgumentCountException.CheckAtLeast(nameof(Assert), 3, args);

            var conclusion = TermToLiteral(args[0]);
            var implicationType = args[1];
            var antecedents = Conjunction(args, 2);

            if (implicationType.Equals("<-"))
                Problem.Current.Assert(antecedents > conclusion);
            else if (implicationType is PrimitiveTask pt && pt.Name == "<=")
            {
                var prop = conclusion as Proposition;
                if (ReferenceEquals(prop, null))
                    throw new ArgumentException("Conclusion of <= rule must be a proposition, not a negation");
                Problem.Current.Assert(prop <= antecedents);
            }
            else
                throw new ArgumentException(
                    $"Assert should be used to assert an implication using either <- or <=.  {implicationType} isn't a valid implication operator.");
        }

        return true;
    }

    private static Expression Conjunction(object[] literals, int position = 0)
    {
        if (position == literals.Length - 1)
            return TermToLiteral(literals[position]);
        return TermToLiteral(literals[position]) & Conjunction(literals, position + 1);
    }

    private const string CatSatPredicateProperty = "CatSATPredicate";

    private static Literal TermToLiteral(object term)
    {
        if (term is object[] tuple)
        {
            switch (tuple.Length)
            {
                case 0:
                    throw new ArgumentException("[] is not a valid predicate expression");

                case 2 when tuple[0].Equals(HigherOrderBuiltins.Not):
                    return Not(TermToLiteral(tuple[1]));

                default:
                    Delegate p;
                    switch (tuple[0])
                    {
                        case Delegate d:
                            p = d;
                            break;

                        case Task t:
                            if (t.Properties.TryGetValue(CatSatPredicateProperty, out var pValue))
                                p = pValue as Delegate;
                            else
                            {
                                switch (tuple.Length)
                                {
                                    case 2:
                                        p = Predicate<object>(t.Name);
                                        break;

                                    case 3:
                                        p = Predicate<object, object>(t.Name);
                                        break;

                                        case 4:
                                        p = Predicate<object, object, object>(t.Name);
                                        break;

                                        default:
                                            throw new ArgumentException(
                                                $"Arity not supported for CatSAT literals: {Writer.TermToString(tuple)}");
                                }

                                t.Properties[CatSatPredicateProperty] = p;
                            }
                            break;

                        default:
                            throw new ArgumentException(
                                    $"Invalid predicate in SAT proposition expression: {Writer.TermToString(tuple[0])}");
                    }

                    for (int i=1; i<tuple.Length; i++)
                        if (tuple[i] is LogicVariable)
                            throw new ArgumentException(
                                $"Arguments to predicates in SAT proposition expressions must be instantiated: {Writer.TermToString(tuple[0])}");

                    // ReSharper disable once PossibleNullReferenceException
                    return (Proposition) p.DynamicInvoke(tuple.Skip(1).ToArray());
            }
        }

        return Proposition.MakeProposition(term);
    }

    private static IEnumerable<Literal> TermListToLiteralList(IEnumerable<object> terms)
    {
        foreach (var elt in terms)
        {
            if (elt is object[] a && a[0] is object[])
                foreach (var t in TermListToLiteralList(a))
                    yield return t;
            else yield return TermToLiteral(elt);
        }
    }
}
