using System;
using System.Collections;
using System.IO;
#if NewtonSoft
using Newtonsoft.Json;
#endif
using Step;
using Step.Interpreter;
using Step.Utilities;

namespace Assets.Scripts
{
#if NewtonSoft
    public static class Json
    {
        public static void AddBuiltins(Module m)
        {
            Documentation.SectionIntroduction("JSON", "Tasks for manipulating JSON files.  JSON files read as hash tables and arraylists, but of which can be accessed as if they're predicates: [table key ?value] accesses the value associated with key in table, and [list ?element] accesses elements in a list.");
            m["ReadJson"] = new SimpleFunction<string[], object>(
                "ReadJson",
                fileName => JsonToHashtable(Path.Combine(
                    Repl.CurrentRepl.ProjectPath,
                    fileName[0] + ".json")))
                .Arguments("filename", "?json")
                .Documentation("JSON", "Reads filename.json and places the decoded data in ?json.");
        }

        private static object JsonToHashtable(string path)
        {
            using (var tr = File.OpenText(path))
                return JsonToHashtable(tr);
        }
        
        private static object JsonToHashtable(TextReader stream)
        {
            using (var reader = new Newtonsoft.Json.JsonTextReader(stream))
            {
                return ReadJsonThing(reader);
            }
        }

        private static object ReadJsonThing(JsonTextReader reader, bool firstTokenAlreadyRead = false)
        {
            // Precondition: !firstTokenAlreadyRead || reader at first token of this object
            // Postcondition: reader at last token of this object
            if (!firstTokenAlreadyRead)
                reader.Read();
            // Now at first token of value
            switch (reader.TokenType)
            {
                case JsonToken.StartObject:
                    return ReadJsonObject(reader);

                case JsonToken.StartArray:
                    return ReadJsonArray(reader);

                default:
                    return reader.Value;
            }
        }

        private static Hashtable ReadJsonObject(JsonTextReader reader)
        {
            // Precondition: reader at StartObject token
            // Postcondition: reader at EndObject token
            var o = new Hashtable();
            reader.Read();   // skip over StartObject
            while (reader.TokenType != JsonToken.EndObject)
            {
                if (reader.TokenType != JsonToken.PropertyName)
                    throw new Exception($"Invalid token type {reader.TokenType}");
                var name = (string) reader.Value;
                var value = ReadJsonThing(reader);
                reader.Read();
                if (value is long i)
                    value = (int) i;
                if (value != null)
                {
                    if (int.TryParse(name, out var numericName))
                        o[numericName] = value;
                    else o[name] = value;
                }
            }

            // Now at EndObject
            return o;
        }

        private static ArrayList ReadJsonArray(JsonTextReader reader)
        {
            // Precondition: reader at StartArray token
            // Postcondition: reader at EndArray token
            var o = new ArrayList();
            reader.Read();   // Skip over StartArray
            while (reader.TokenType != JsonToken.EndArray)
            {
                var element = ReadJsonThing(reader, true);
                if (element is long l)
                    element = (int) l;
                o.Add(element);
                reader.Read(); // Skip to next token
            }

            // Now at EndArray
            return o;
        }
    }
#endif
}
