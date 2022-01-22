using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Step.Interpreter;

namespace Assets.SION
{
    public class EntityType
    {
        public static bool Randomize = true;
        
        public const string TypeTagName = "entity_type";
        
        public readonly string Name;
        public readonly string DataName;

        public readonly List<Hashtable> Entities = new List<Hashtable>();

        public IEnumerable<Hashtable> ShuffledEntities => Entities.MaybeShuffle(Randomize);
        
        public readonly Dictionary<string, Hashtable> IdToEntity = new Dictionary<string, Hashtable>();
        public readonly Dictionary<Hashtable, string> EntityToId = new Dictionary<Hashtable, string>();

        // ReSharper disable once StringLiteralTypo
        private static readonly string[] EntityPath = new[] {"entityman", "entities"};

        private static readonly string[] PlayerPath = new[] {"players", "data", "players"};

        public EntityType(string name, string dataField, Hashtable database)
        {
            Name = name;
            DataName = dataField;

            if (dataField == "gang")
                foreach (Hashtable entity in SIONPrimitives.GetPath<ArrayList>(database, PlayerPath))
                {
                    var idString = (string) entity["pid"];
                    if (idString != null)
                    {
                        Entities.Add(entity);
                        IdToEntity[idString] = entity;
                        EntityToId[entity] = idString;
                        entity[TypeTagName] = dataField;
                    }
                }
            else
                foreach (Hashtable entityWrapper in SIONPrimitives.GetPath<ArrayList>(database, EntityPath))
                {
                    var payload = (Hashtable) entityWrapper["data"];
                    if (payload.ContainsKey(dataField))
                    {
                        var entity = (Hashtable) payload[dataField];
                        var idString = SIONPrimitives.GetPath<string>(payload, "ident", "id");
                        Entities.Add(entity);
                        IdToEntity[idString] = entity;
                        EntityToId[entity] = idString;
                        entity[TypeTagName] = dataField;
                        // ReSharper disable StringLiteralTypo
                        entity["tmpl"] = entityWrapper["tmpl"];
                        // ReSharper restore StringLiteralTypo
                        if (payload.ContainsKey("agent"))
                        {
                            var agent = (Hashtable)payload["agent"];
                            if (agent.ContainsKey("pid"))
                                entity["pid"] = agent["pid"];
                        }
                    }
                }
        }

        public bool IsMember(Hashtable entity)
        {
            var tag = entity[TypeTagName];
            return tag != null && tag.Equals(DataName);
        }

        // The Step predicate Type(?x).  This works in both in and out modes
        public GeneralPredicate<object> TypePredicate => new GeneralPredicate<object>(Name,
            o => o is Hashtable h && IsMember(h),
            () => ShuffledEntities);

        // The Step predicate TypeIndex(?reference, ?entity).  This works in all possible modes, although I'm not sure what the
        // use case would be for OutOut.
        public GeneralPredicate<string, Hashtable> ReferencePredicate => new GeneralPredicate<string, Hashtable>(
            Name + "Index",
            (s, e) => EntityToId[e] == s,
            s => new[] {IdToEntity[s]},
            e => new[] {EntityToId[e]},
            () => IdToEntity.Select(pair => (pair.Key, pair.Value)));

        public override string ToString()
        {
            return Name;
        }
    }
}
