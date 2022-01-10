using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Step.Interpreter;

namespace Assets.SION
{
    public class EntityType
    {
        public const string TypeTagName = "type";
        
        public readonly string Name;
        public readonly string TemplateName;
        
        public readonly List<Hashtable> Entities = new List<Hashtable>();
        public readonly Dictionary<string, Hashtable> IdToEntity = new Dictionary<string, Hashtable>();
        public readonly Dictionary<Hashtable, string> EntityToId = new Dictionary<Hashtable, string>();

        public EntityType(string name, string templateName, string dataField, Hashtable database)
        {
            Name = name;
            TemplateName = templateName;

            // ReSharper disable once StringLiteralTypo
            foreach (Hashtable entityWrapper in SIONPrimitives.GetPath<ArrayList>(database, "entityman", "entities"))
            {
                if (entityWrapper["tmpl"].Equals(templateName))
                {
                    var payload = (Hashtable) entityWrapper["data"];
                    var entity = (Hashtable) payload[dataField];
                    var idString = SIONPrimitives.GetPath<string>(payload, "ident", "id");
                    Entities.Add(entity);
                    IdToEntity[idString] = entity;
                    EntityToId[entity] = idString;
                    entity[TypeTagName] = templateName;
                }
            }
        }

        public bool IsMember(Hashtable entity) => entity[TypeTagName].Equals(TemplateName);

        // The Step predicate Type(?x).  This works in both in and out modes
        public GeneralPredicate<object> TypePredicate => new GeneralPredicate<object>(Name,
            o => o is Hashtable h && IsMember(h),
            () => Entities);

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
