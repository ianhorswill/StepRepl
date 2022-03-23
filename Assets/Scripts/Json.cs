using System;
using System.Collections;
using System.IO;
using Newtonsoft.Json;
using Step;
using Step.Interpreter;
using Step.Utilities;

namespace Assets.Scripts
{
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
                var name = reader.Value;
                o[name] = ReadJsonThing(reader);
                reader.Read();
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
                o.Add(ReadJsonThing(reader, true));
                reader.Read(); // Skip to next token
            }

            // Now at EndArray
            return o;
        }
    }
}
