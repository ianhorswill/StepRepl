using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using Step.Interpreter;
using Step.Utilities;

namespace Assets.SION
{
    public class RelationStore
    {
        private readonly IList<Hashtable> allRelations = new List<Hashtable>();
        private readonly Dictionary<Hashtable, List<Hashtable>> fromIndex = new Dictionary<Hashtable, List<Hashtable>>();
        private readonly Dictionary<Hashtable, List<Hashtable>> toIndex = new Dictionary<Hashtable, List<Hashtable>>();
        private readonly Dictionary<string, List<Hashtable>> typeIndex = new Dictionary<string, List<Hashtable>>();

        private readonly EntityType personType;
        
        private List<TValue> Index<TKey, TValue>(Dictionary<TKey, List<TValue>> index, TKey key)
        {
            if (!index.TryGetValue(key, out var list))
            {
                list = new List<TValue>();
                index[key] = list;
            }

            return list;
        }

        private void AddToIndex<TKey, TValue>(Dictionary<TKey, List<TValue>> index, TKey key, TValue value)
        {
            Debug.Assert(key != null);
            Index(index, key).Add(value);
        }

        public RelationStore(string name, Hashtable db, EntityType people, string requiredKey)
        {
            personType = people;
            // ReSharper disable once IdentifierTypo
            // ReSharper disable StringLiteralTypo
            var rels = SIONPrimitives.GetPath<ArrayList>(db, "simman", "rels", "data");
            // ReSharper restore StringLiteralTypo
            foreach (Hashtable person in rels)
            foreach (Hashtable rel in (ArrayList) person["data"])
            {
                if (requiredKey != null && !rel.ContainsKey(requiredKey))
                    continue;
                allRelations.Add(rel);
                AddToIndex(fromIndex, personType.IdToEntity[(string)rel["from"]], rel);
                AddToIndex(toIndex, personType.IdToEntity[(string)rel["to"]], rel);
                AddToIndex(typeIndex, (string)rel["type"], rel);
            }
            
            Lookup = new GeneralNAryPredicate(name, PredicateImplementation);
        }

        public readonly GeneralNAryPredicate Lookup;

        private static bool Unifiable(object arg, object value) => arg is LogicVariable || arg.Equals(value);
        
        private IEnumerable<object[]> PredicateImplementation(object[] args)
        {
            ArgumentCountException.Check(Lookup, 4, args);
            if (!(args[3] is LogicVariable))
                throw new ArgumentException($"Last argument to {Lookup.Name} must be a variable");
            var result = new object[4];
            args.CopyTo(result,0);
            var instances = allRelations;
            if (args[0] is Hashtable from)
                instances = Index(fromIndex, from);
            else if (args[1] is Hashtable to)
                instances = Index(toIndex, to);
            else if (args[2] is string type)
                instances = Index(typeIndex, type);

            foreach (var relation in instances.MaybeShuffle(EntityType.Randomize))
            {
                var fromOut = personType.IdToEntity[(string) relation["from"]];
                if (!Unifiable(args[0], fromOut))
                    continue;
                result[0] = fromOut;
                var toOut = personType.IdToEntity[(string) relation["to"]];
                if (!Unifiable(args[1], toOut))
                    continue;
                result[1] = toOut;
                var typeOut = relation["type"];
                if (!Unifiable(args[2], typeOut))
                    continue;
                result[2] = typeOut;
                result[3] = relation;
                yield return result;
            }
        }
    }
}
