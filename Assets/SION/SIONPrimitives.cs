using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Step;
using Step.Interpreter;
using UnityEngine.AI;

namespace Assets.SION
{
    // ReSharper disable once InconsistentNaming
    public static class SIONPrimitives
    {
        private static object fileCache;
        
        public static void AddBuiltins(Module m)
        {
            m["SIONFile"] = new SimpleFunction<string, object>(
                "SIONFile",
                fileName =>
                {
                    if (fileCache == null)
                        fileCache = SomaSim.SION.SION.Parse(
                            File.OpenText(
                                Path.Combine(
                                    Repl.CurrentRepl.ProjectPath,
                                    fileName + ".sim")));
                    return fileCache;
                });

            m["GetPath"] = new SimpleFunction<Hashtable, string[], object>("GetPath", GetPath<object>);

            m["DefineEntityType"] = new SimplePredicate<string[], string, Hashtable>(
                "DefineEntityType",
                (tokens, dataField, database) =>
                {
                    var name = tokens[0];
                    var t = new EntityType(name, dataField, database);
                    m[name] = t.TypePredicate;
                    m[name + "Type"] = t;
                    m[name + "Index"] = t.ReferencePredicate;
                    return true;
                });

            m["DefineLedger"] = new SimplePredicate<string[], Hashtable, EntityType>(
                "DefineLedger",
                (tokens, database, entities) =>
                {
                    var name = tokens[0];
                    m[name] = MakeLedgerPredicate(name, database, entities); ;
                    return true;
                });

            m["TranslateEnumeration"] = new SimplePredicate<EntityType, string, string, object[], int>(
                nameof(TranslateEnumeration),
                (entities, attribute, newName, translation, divisor) =>
                {
                    TranslateEnumeration(entities, attribute, newName, translation, divisor);
                    return true;
                });

            m[nameof(TranslateRelationshipTypes)] =
                new SimplePredicate<ArrayList>(nameof(TranslateRelationshipTypes), TranslateRelationshipTypes);

            m["DeprefixTraitList"] = new SimplePredicate<EntityType, string, string, string>(
                nameof(TranslateEnumeration),
                (entities, attribute, newName, prefix) =>
                {
                    DeprefixTraitList(entities, attribute, newName, prefix);
                    return true;
                });

            m["CleanKey"] = new SimplePredicate<EntityType, string>("CleanKey",
                (e, k) =>
                {
                    CleanKey(e, k);
                    return true;
                });

            m["CleanKeysWithPrefix"] = new SimplePredicate<EntityType, string>("CleanKeysWithPrefix",
                (e, k) =>
                {
                    CleanKeysWithPrefix(e, k);
                    return true;
                });

            m["DefineSlotPredicate"] = new SimplePredicate<string[], string, object>("DefineSlotPredicate",
                (tokens, slotName, collection) =>
                {
                    var name = tokens[0];
                    switch (collection)
                    {
                        case ArrayList a:
                            m[name] = MakeSlotPredicate(name, a, slotName);
                            break;

                        case EntityType t:
                            m[name] = MakeSlotPredicate(name, t, slotName);
                            break;

                        case List<Hashtable> l:
                            m[name] = MakeSlotPredicate(name, l, slotName);
                            break;

                        default:
                            throw new ArgumentException("Invalid collection argument");
                    }

                    return true;
                });

            m["DefineIndexedSlotPredicate"] = new SimplePredicate<string[], EntityType, string, EntityType>("DefineIndexedSlotPredicate",
                (tokens, argType, slotName, valueType) =>
                {
                    string name = tokens[0];
                    m[name] = MakeIndexedSlotPredicate(name, argType, valueType, slotName);
                    return true;
                });

            m["DefineInModeIndexedSlotPredicate"] = new SimplePredicate<string[], string, EntityType>("DefineInModeIndexedSlotPredicate",
                (tokens, slotName, valueType) =>
                {
                    string name = tokens[0];
                    m[name] = MakeIndexedSlotPredicate(name, valueType, slotName);
                    return true;
                });

            m["DefineRelationPredicate"] = new SimplePredicate<string[], Hashtable, EntityType>(
                "DefineRelationPredicate",
                (name, db, personType) =>
                {
                    var store = new RelationStore(name[0], db, personType, null);
                    m[name[0]] = store.Lookup;
                    return true;
                });

            m["DefineRelationPredicateWithKey"] = new SimplePredicate<string[], Hashtable, EntityType, string>(
                "DefineRelationPredicate",
                (name, db, personType, key) =>
                {
                    var store = new RelationStore(name[0], db, personType, key);
                    m[name[0]] = store.Lookup;
                    return true;
                });

            m["SIONDate"] =
                new SimplePredicate<object>("SIONDate", o => o is Hashtable && ((Hashtable) o).ContainsKey("days"));

            m["DecodeCoGDate"] = new SimpleFunction<Hashtable, DateTime>("SIONToDate", SIONToDateTime);

            m["FormatSIONDate"] = new SimpleFunction<string[], Hashtable, string>(
                "FormatSIONDate", 
                (f, d)=> SIONToDateTime(d).ToString(f.Untokenize()));
        }

        private static DateTime SIONToDateTime(Hashtable date)
        {
            var days = (int) date["days"];
            var year = days / 365;
            var dayOfYear = days % 365;
            return new DateTime(year, 1, 1) + new TimeSpan(dayOfYear, 0, 0, 0);
        }

        public static T GetPath<T>(Hashtable d, params string[] path) => GetPath<T>(d, (IEnumerable<string>)path);
        
        public static T GetPath<T>(Hashtable d, IEnumerable<string> path)
        {
            object result = d;
            foreach (var key in path)
                result = ((Hashtable) result)[key];
            return (T)result;
        }

        private static void TranslateEnumeration(EntityType t, string oldName, string newName, object[] values, int divisor)
        {
            foreach (var e in t.Entities)
            {
                if (e.ContainsKey(oldName))
                {
                    var index = (int) e[oldName];
                    e[newName] = values[index / divisor];
                    if (oldName != newName)
                        e.Remove(oldName);
                }
            }
        }

        private static void DeprefixTraitList(EntityType t, string oldName, string newName, string prefix)
        {
            foreach (var e in t.Entities)
            {
                if (e.ContainsKey(oldName))
                {
                    var traits = (IEnumerable) e[oldName];
                    e[newName] = traits.Cast<string>().Select(s => s.Replace(prefix, "")).Cast<object>().ToArray();
                    if (oldName != newName)
                        e.Remove(oldName);
                }
            }
        }

        private static void CleanKey(EntityType t, string key)
        {
            foreach (var e in t.Entities)
                if (e.ContainsKey(key))
                    e.Remove(key);
        }

        private static void CleanKeysWithPrefix(EntityType t, string keyPrefix)
        {
            var badKeys = new List<object>();
            foreach (var e in t.Entities)
            {
                badKeys.Clear();
                foreach (var k in e.Keys)
                    if (k is string s && s.StartsWith(keyPrefix))
                        badKeys.Add(k);
                foreach (var k in badKeys)
                    if (k is string s && s.StartsWith(keyPrefix))
                        e.Remove(k);
            }
        }

        //private static Hashtable IndexEntities(IList<Hashtable> entities)
        //{
        //    var index = new Hashtable(entities.Count*20);
        //    foreach (var e in entities)
        //    {
        //        var uid = (int) e["uid"];
        //        index[uid] = e;
        //        index[$"E1_{uid}"] = e;
        //    }

        //    return index;
        //}
        
        private static string[] relNames =
        {
            "self", "spouse", "child", "mother", "father", "sibling", "mother_sib", "father_sib", "sib_child", "cousin",
            "acquaintance"
        };

        private static bool TranslateRelationshipTypes(ArrayList rels)
        {
            foreach (Hashtable relSet in rels)
            foreach (Hashtable rel in (ArrayList) relSet["data"])
                if (rel["type"] is int)
                    rel["type"] = relNames[((int) rel["type"]) / 10];
            return true;
        }

        private static readonly Dictionary<ArrayList, List<Hashtable>> ListTable = new Dictionary<ArrayList, List<Hashtable>>();

        public static List<Hashtable> Listify(ArrayList a)
        {
            if (ListTable.TryGetValue(a, out var list))
                return list;
            list = a.Cast<Hashtable>().ToList();
            ListTable[a] = list;
            return list;
        }

        public static GeneralPredicate<Hashtable, object> MakeSlotPredicate(string predicateName, List<Hashtable> collection, string slotName) =>
            new GeneralPredicate<Hashtable, object>(predicateName,
                (h, v) => h[slotName].Equals(v),
                h => new []{ h[slotName]},
                v => collection.MaybeShuffle(EntityType.Randomize).Where(h => h[slotName].Equals(v)),
                () => collection.MaybeShuffle(EntityType.Randomize).Where(h => h.ContainsKey(slotName)).Select(h => (h, h[slotName])));

        public static GeneralPredicate<Hashtable, object> MakeSlotPredicate(string predicateName, EntityType t,
            string slotName) =>
            MakeSlotPredicate(predicateName, t.Entities, slotName);

        public static GeneralPredicate<Hashtable, object> MakeSlotPredicate(string predicateName, ArrayList a,
            string slotName) =>
            MakeSlotPredicate(predicateName, Listify(a), slotName);

        public static GeneralPredicate<Hashtable, Hashtable> MakeIndexedSlotPredicate(string predicateName,
            EntityType entityType, EntityType valueType, string slotName) =>
            new GeneralPredicate<Hashtable, Hashtable>(predicateName,
                (h, v) => h.ContainsKey(slotName) && valueType.IdToEntity[(string)h[slotName]].Equals(v),
                h => h.ContainsKey(slotName)?
                    new[] { valueType.IdToEntity[(string)h[slotName]] } : new Hashtable[0],
                v => entityType.ShuffledEntities.Where(h => valueType.IdToEntity[(string)h[slotName]].Equals(v)),
                () => entityType.ShuffledEntities.Where(h => h.ContainsKey(slotName)).Select(h => (h, valueType.IdToEntity[(string)h[slotName]])));

        public static GeneralPredicate<Hashtable, Hashtable> MakeIndexedSlotPredicate(string predicateName,
            EntityType valueType, string slotName) =>
            new GeneralPredicate<Hashtable, Hashtable>(predicateName,
                (h, v) => h.ContainsKey(slotName) && valueType.IdToEntity[(string) h[slotName]].Equals(v),
                h => h.ContainsKey(slotName) ? new[] {valueType.IdToEntity[(string) h[slotName]]} : new Hashtable[0],
                null,
                null);

        public static TuplePredicate MakeLedgerPredicate(string name, Hashtable database, EntityType entities) =>
            new TuplePredicate(name,
                new []
                {
                    typeof(Hashtable),  // agent
                    typeof(string),     // event
                    typeof(Hashtable),  // patient
                    typeof(Hashtable),  // experiencer
                    typeof(int),        // valence
                    typeof(int),        // timestamp
                },
                new[]
                {
                    true,  // agent
                    true,  // event
                    true,  // patient
                    true,  // experiencer
                    true, // valence
                    false, // timestamp
                },
                EventTuples(database, entities)) { Shuffler = tuples => tuples.MaybeShuffle(EntityType.Randomize) };

        private static IEnumerable<object[]> EventTuples(Hashtable database, EntityType entities)
        {
            var players = GetPath<ArrayList>(database, "players", "data", "players");
            var humanPlayer = (Hashtable)players[1];
            var ledger = GetPath<ArrayList>(humanPlayer, "social", "historyledger");
            
            foreach (Hashtable e in ledger) 
                yield return EncodeEvent(e, entities);
        }

        private static object[] EncodeEvent(Hashtable e, EntityType entities)
        {
            var from = entities.IdToEntity[(string)e["from"]];
            var to = entities.IdToEntity[(string)e["to"]];
            // ReSharper disable once StringLiteralTypo
            var type = (string)e["eventname"];
            var time = e["time"];

            if (type == "ai" && !e.ContainsKey("method"))
                Debugger.Break();
            if (type == "ai")
                return new[]
                {
                    from,               // agent
                    // ReSharper disable once StringLiteralTypo
                    e["method"],    // event
                    "nothing",               // patient
                    "nothing",               // experiencer
                    0,                  // valence
                    time,               // timestamp
                };
            else
            {
                var info = (Hashtable) e["info"];
                var valence = info.ContainsKey("valence")?info["valence"] : 0;

                return new[]
                {
                    to,                 // agent
                    type,               // event
                    entities.IdToEntity[(string)info["entityCtx"]],  // patient
                    from,               // experiencer
                    valence,            // valence
                    time,               // timestamp
                };
            }
        }
    }
}
