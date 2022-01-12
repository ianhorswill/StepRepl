using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Step;
using Step.Interpreter;

namespace Assets.SION
{
    // ReSharper disable once InconsistentNaming
    public static class SIONPrimitives
    {
        public static void AddBuiltins(Module m)
        {
            m["SIONFile"] = new SimpleFunction<string, object>(
                "SIONFile",
                fileName => SomaSim.SION.SION.Parse(
                    File.OpenText(
                        Path.Combine(
                            Repl.CurrentRepl.ProjectPath,
                            fileName + ".sim"))));

            //m["PatchReferences"] = new SimplePredicate<Hashtable>("PatchReferences",
            //    d =>
            //    {
            //        PatchEntityReferences(d);
            //        return true;
            //    });

            m["GetPath"] = new SimpleFunction<Hashtable, string[], object>("GetPath", GetPath<object>);

            //m["EntityTable"] = new SimpleFunction<Hashtable, IList>("EntityTable", db => EntityTable(db).ToList());

            //m["EntitiesOfType"] =
            //    new SimpleFunction<IEnumerable<Hashtable>, string, IEnumerable<Hashtable>>("EntitiesOfType",
            //        (entities, t) => entities.Where(e => e["type"].Equals(t)).ToList());

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

            m[nameof(IndexEntities)] =
                new SimpleFunction<IList<Hashtable>, Hashtable>(nameof(IndexEntities), IndexEntities);
        }
        public static T GetPath<T>(Hashtable d, params string[] path) => GetPath<T>(d, (IEnumerable<string>)path);
        
        public static T GetPath<T>(Hashtable d, IEnumerable<string> path)
        {
            object result = d;
            foreach (var key in path)
                result = ((Hashtable) result)[key];
            return (T)result;
        }
        
        //private static void PatchEntityReferences(Hashtable database)
        //{
        //    var entities = GetPath<ArrayList>(database, "entityman", "entities");
        //    var tableSize = entities.Count * 2;
        //    var entityReferenceTable = new Dictionary<string, Hashtable>(tableSize);
        //    // Unity's version of .NET doesn't support a size parameter for the HashSet constructor
        //    // and it can't handle autosizing as large as we need.
        //    //var entitySet = new Dictionary<Hashtable, bool>(tableSize);
        //    //var walkedEntities = new Dictionary<Hashtable, bool>(tableSize);

        //    // Move type into the data hashtables themselves, and build the reference table
        //    foreach (Hashtable e in entities)
        //    {
        //        var type = e["tmpl"];
        //        var id = (int)e["i"];
        //        var data = (Hashtable)e["data"];
        //        if (data.ContainsKey("person"))
        //            data = (Hashtable)data["person"];
        //        data["type"] = type;
        //        entityReferenceTable[$"E1_{id}"] = data;
        //        //entitySet.Add(data, true);
        //    }

        //    var remap = new Stack<(Hashtable, string, Hashtable)>();

        //    void Walk(object x, int depth)
        //    {
        //        if (depth > 3)
        //            return;
        //        switch (x)
        //        {
        //            case ArrayList a:
        //                foreach (var e in a) Walk(a, depth+1);
        //                break;

        //            case Hashtable h:
        //                //if (entitySet.ContainsKey(h) && walkedEntities.ContainsKey(h))
        //                //    return;
        //                //walkedEntities.Add(h, true);
        //                var remapCount = 0;
        //                foreach (DictionaryEntry e in h)
        //                {
        //                    var v = e.Value;
        //                    if (v is string s)
        //                    {
        //                        if (entityReferenceTable.TryGetValue(s, out var entity))
        //                        {
        //                            // We can't actually do the remapping now, because we can't modify the dictionary
        //                            // while walking it, so we save it in a stack
        //                            remap.Push((h, s, entity));
        //                            remapCount++;
        //                        }
        //                    }
        //                    else 
        //                        Walk(v, depth+1);
        //                }

        //                for (; remapCount > 0; remapCount--)
        //                {
        //                    var (t, s, e) = remap.Pop();
        //                    Debug.Assert(t == h);  // paranoid
        //                    h[s] = e;
        //                }
        //                break;
        //        }
        //    }
            
        //    Walk(database, 0);
        //}
        
        private static void TranslateEnumeration(EntityType t, string oldName, string newName, object[] values, int divisor)
        {
            foreach (var e in t.Entities)
            {
                var index = (int) e[oldName];
                e[newName] = values[index/divisor];
                if (oldName != newName)
                    e.Remove(oldName);
            }
        }

        private static void DeprefixTraitList(EntityType t, string oldName, string newName, string prefix)
        {
            foreach (var e in t.Entities)
            {
                var traits = (IEnumerable)e[oldName];
                e[newName] = traits.Cast<string>().Select(s => s.Replace(prefix, "")).Cast<object>().ToArray();
                if (oldName != newName)
                    e.Remove(oldName);
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

        private static Hashtable IndexEntities(IList<Hashtable> entities)
        {
            var index = new Hashtable(entities.Count*20);
            foreach (var e in entities)
            {
                var uid = (int) e["uid"];
                index[uid] = e;
                index[$"E1_{uid}"] = e;
            }

            return index;
        }

        private static string[] relNames =
        {
            "self", "spouse", "child", "mother", "father", "sibling", "mother_sib", "father_sib", "sib_child", "cousin",
            "acquaintance"
        };

        private static bool TranslateRelationshipTypes(ArrayList rels)
        {
            foreach (Hashtable relSet in rels)
            foreach (Hashtable rel in (ArrayList) relSet["data"])
                rel["type"] = relNames[((int) rel["type"]) / 10];
            return true;
        }

    }
}
